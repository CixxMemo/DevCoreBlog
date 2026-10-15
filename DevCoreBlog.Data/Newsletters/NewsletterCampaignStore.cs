using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Locks the current source/category while committing one immutable start key; never submits mail.</summary>
public sealed class NewsletterCampaignStore(ApplicationDbContext context) : INewsletterCampaignStore
{
    private static readonly Expression<Func<Post, CampaignSource>> SourceProjection = p => new(p.Id, p.EditVersion, p.Title,
        p.Slug, p.DocumentVersion, p.DocumentJson, p.DocumentPlainText, p.DocumentWordCount, p.DocumentReadingMinutes);
    public Task<CampaignSource?> ReadSourceAsync(int postId, CancellationToken cancellationToken) =>
        context.Posts.AsNoTracking().Where(p => p.Id == postId && p.ContentKind == PostContentKind.Newsletter
            && p.AccessScope == PostAccessScope.Subscribers && p.IsActive && p.Category.IsActive)
            .Select(SourceProjection)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CampaignIntent?> FindIntentAsync(Guid startKey, CancellationToken cancellationToken) =>
        context.NewsletterCampaigns.AsNoTracking().Where(x => x.StartKey == startKey)
            .Select(x => new CampaignIntent(x.Id, x.PostId, x.SourceRevision, x.State, x.Version, x.SnapshotHash))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CampaignStartResult> TryCreateAsync(NewsletterCampaign campaign, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", cancellationToken);
        var prior = await FindIntentAsync(campaign.StartKey, cancellationToken);
        if (prior is not null) return Match(prior, campaign);
        // Shared locks allow concurrent preparation but prevent source/category edits through commit.
        var source = (await context.Posts.FromSqlInterpolated($"""
            SELECT p.* FROM "Posts" p JOIN "Categories" c ON c."Id"=p."CategoryId"
            WHERE p."Id"={campaign.PostId} AND p."IsActive" AND c."IsActive"
                AND p."ContentKind"={(int)PostContentKind.Newsletter} AND p."AccessScope"={(int)PostAccessScope.Subscribers}
            FOR SHARE OF p,c
            """).AsNoTracking().Select(SourceProjection).ToListAsync(cancellationToken)).SingleOrDefault();
        if (source is null) return new(CampaignStartStatus.NotFound);
        if (source.Revision != campaign.SourceRevision || source.Subject != campaign.Subject || source.Slug != campaign.SourceSlug
            || source.DocumentVersion != campaign.DocumentVersion || source.DocumentJson != campaign.DocumentJson
            || source.DocumentPlainText != campaign.DocumentPlainText || source.DocumentWordCount != campaign.DocumentWordCount
            || source.DocumentReadingMinutes != campaign.DocumentReadingMinutes)
            return new(CampaignStartStatus.SourceChanged);
        var added = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NewsletterCampaigns" ("Id","StartKey","PostId","SourceRevision","Subject","SourceSlug",
                "DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes",
                "RenderVersion","Html","Text","WebVersionUrl","SenderEmail","SenderName","ReplyTo","ProviderContract",
                "SnapshotHash","State","CreatedUtc","UpdatedUtc","Version")
            VALUES ({campaign.Id},{campaign.StartKey},{campaign.PostId},{campaign.SourceRevision},{campaign.Subject},{campaign.SourceSlug},
                {campaign.DocumentVersion},{campaign.DocumentJson},{campaign.DocumentPlainText},{campaign.DocumentWordCount},{campaign.DocumentReadingMinutes},
                {campaign.RenderVersion},{campaign.Html},{campaign.Text},{campaign.WebVersionUrl},{campaign.SenderEmail},{campaign.SenderName},{campaign.ReplyTo},{campaign.ProviderContract},
                {campaign.SnapshotHash},0,{campaign.CreatedUtc},{campaign.UpdatedUtc},1)
            ON CONFLICT ("StartKey") DO NOTHING
            """, cancellationToken);
        if (added == 0)
        {
            var winner = await FindIntentAsync(campaign.StartKey, cancellationToken) ?? throw new InvalidOperationException("Campaign winner unavailable.");
            return Match(winner, campaign);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(CampaignStartStatus.Created, campaign.Intent());
    }

    public async Task<CampaignCancelStatus> TryCancelAsync(Guid id, long expectedVersion, DateTime utcNow, CancellationToken cancellationToken)
    {
        NewsletterCampaign.RequireUtc(utcNow);
        if (id == Guid.Empty || expectedVersion is <= 0 or long.MaxValue) return CampaignCancelStatus.Conflict;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", cancellationToken);
        var item = (await context.NewsletterCampaigns.FromSqlInterpolated($"SELECT * FROM \"NewsletterCampaigns\" WHERE \"Id\"={id} FOR UPDATE")
            .AsNoTracking().Select(x => new { x.State, x.Version, x.UpdatedUtc }).ToListAsync(cancellationToken)).SingleOrDefault();
        if (item is null) return CampaignCancelStatus.NotFound;
        var transition = NewsletterCampaign.CancellationStatus(item.State, item.Version, item.UpdatedUtc, expectedVersion, utcNow);
        if (transition != CampaignCancelStatus.Cancelled) return transition;
        var count = await context.NewsletterCampaigns.Where(x => x.Id == id && x.Version == expectedVersion && x.State == NewsletterCampaignState.Prepared)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, NewsletterCampaignState.Cancelled)
                .SetProperty(x => x.UpdatedUtc, utcNow.AddTicks(-(utcNow.Ticks % 10))).SetProperty(x => x.Version, expectedVersion + 1), cancellationToken);
        if (count != 1) return CampaignCancelStatus.Conflict;
        await transaction.CommitAsync(cancellationToken);
        return CampaignCancelStatus.Cancelled;
    }

    private static CampaignStartResult Match(CampaignIntent prior, NewsletterCampaign proposed) =>
        new(prior.PostId == proposed.PostId && prior.SourceRevision == proposed.SourceRevision && prior.SnapshotHash == proposed.SnapshotHash
            ? CampaignStartStatus.Existing : CampaignStartStatus.Conflict, prior);
}
