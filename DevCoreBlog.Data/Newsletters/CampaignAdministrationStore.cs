using System.Data;
using System.Linq.Expressions;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Repeatable-read metadata and bounded projections keep counters consistent during concurrent worker commits.</summary>
public sealed class CampaignAdministrationStore(ApplicationDbContext db) : ICampaignAdministrationStore
{
    private static readonly Expression<Func<NewsletterCampaign, CampaignSummary>> Summary = x =>
        new(x.Id, x.PostId, x.SourceRevision, x.Subject, x.State, x.Version, x.CreatedUtc);

    public async Task<CampaignSourceSummary?> ReadSourceSummaryAsync(int postId, CancellationToken ct)
    {
        await using var tx = await BeginAsync(ct);
        var item = await db.Posts.AsNoTracking().Where(x => x.Id == postId && x.ContentKind == PostContentKind.Newsletter
            && x.AccessScope == PostAccessScope.Subscribers && x.IsActive && x.Category.IsActive)
            .Select(x => new CampaignSourceSummary(x.Id, x.EditVersion, x.Title)).SingleOrDefaultAsync(ct);
        await tx.CommitAsync(ct);
        return item;
    }

    public async Task<CampaignListPage> FindAsync(int page, int? postId, CancellationToken ct)
    {
        if (page is < 1 or > CampaignListPage.MaximumPage || postId is <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        await using var tx = await BeginAsync(ct);
        var query = db.NewsletterCampaigns.AsNoTracking().Where(x => postId == null || x.PostId == postId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * CampaignListPage.PageSize).Take(CampaignListPage.PageSize).Select(Summary).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(items, page, count);
    }

    public async Task<CampaignControl?> ReadControlAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await BeginAsync(ct);
        var item = await db.NewsletterCampaigns.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CampaignControl(x.Id, x.Version, x.State, x.SourceSlug, x.WebVersionUrl,
                db.CampaignDispatches.Any(d => d.CampaignId == x.Id))).SingleOrDefaultAsync(ct);
        await tx.CommitAsync(ct);
        return item;
    }

    public async Task<CampaignDetail?> ReadAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await BeginAsync(ct);
        var summary = await db.NewsletterCampaigns.AsNoTracking().Where(x => x.Id == id).Select(Summary).SingleOrDefaultAsync(ct);
        if (summary is null) return null;
        var body = await db.NewsletterCampaigns.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Text, x.SenderEmail, x.SenderName, x.ReplyTo, x.WebVersionUrl, x.SourceSlug }).SingleAsync(ct);
        var selection = await db.NewsletterRecipientSelections.AsNoTracking().Where(x => x.CampaignId == id)
            .Select(x => new RecipientSelectionProgress(x.CampaignId, x.CutoffUtc, x.UpperSubscriberId, x.LastSubscriberId,
                x.ScannedCount, x.SelectedCount, x.State, x.UpdatedUtc, x.Version)).SingleOrDefaultAsync(ct);
        var dispatch = await db.CampaignDispatches.AsNoTracking().Where(x => x.CampaignId == id)
            .Select(x => new CampaignDispatchSummary(x.PreparationState, x.StagedCount, x.ExpiresUtc)).SingleOrDefaultAsync(ct);
        var counts = await db.CampaignDeliveryJobs.AsNoTracking().Where(x => x.CampaignId == id).GroupBy(x => x.State)
            .Select(g => new CampaignJobCount(g.Key, g.LongCount(), g.Sum(x => (long)x.AttemptCount),
                g.LongCount(x => x.State == CampaignDeliveryState.Pending && x.AttemptCount > 0))).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(summary, body.Text, body.SenderEmail, body.SenderName, body.ReplyTo, body.WebVersionUrl, body.SourceSlug, selection, dispatch, counts);
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SET TRANSACTION READ ONLY", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
}
