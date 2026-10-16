using System.Data;
using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Bounded SQL aggregates correlate independent provider facts with the immutable message/address pair.</summary>
public sealed class CampaignMetricsStore(ApplicationDbContext db) : ICampaignMetricsStore
{
    public async Task<CampaignMetrics?> ReadAsync(Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty) throw new ArgumentException("A campaign identity is required.", nameof(campaignId));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SET TRANSACTION READ ONLY", ct);
        var campaign = await db.NewsletterCampaigns.AsNoTracking().TagWith("F41 campaign identity").Where(x => x.Id == campaignId)
            .Select(x => new CampaignMetricIdentity(x.Id, x.Subject, x.State)).SingleOrDefaultAsync(ct);
        if (campaign is null) return null;
        var selection = await db.NewsletterRecipientSelections.AsNoTracking().Where(x => x.CampaignId == campaignId)
            .Select(x => (RecipientSelectionState?)x.State).SingleOrDefaultAsync(ct);
        var selected = await db.NewsletterRecipients.TagWith("F41 selected count").LongCountAsync(x => x.CampaignId == campaignId, ct);
        var dispatch = await db.CampaignDispatches.AsNoTracking().Where(x => x.CampaignId == campaignId)
            .Select(x => new CampaignDispatchSummary(x.PreparationState, x.StagedCount, x.ExpiresUtc)).SingleOrDefaultAsync(ct);
        var jobs = await db.CampaignDeliveryJobs.AsNoTracking().TagWith("F41 job counts").Where(x => x.CampaignId == campaignId)
            .GroupBy(x => x.State).Select(g => new CampaignMetricJobs(g.Key, g.LongCount(),
                g.LongCount(x => x.State == CampaignDeliveryState.Pending && x.AttemptCount > 0),
                g.LongCount(x => x.State == CampaignDeliveryState.Held && (x.Problem == CampaignDeliveryProblem.ProviderUncertain || x.Problem == CampaignDeliveryProblem.LeaseLost))))
            .ToListAsync(ct);
        var attempts = await (from attempt in db.CampaignDeliveryAttempts.AsNoTracking()
            join job in db.CampaignDeliveryJobs on attempt.JobId equals job.Id
            where job.CampaignId == campaignId
            select attempt).TagWith("F41 attempt counts").GroupBy(x => x.Outcome)
            .Select(g => new CampaignMetricAttempts(g.Key, g.LongCount())).ToListAsync(ct);
        // Frozen recipients survive source lifecycle changes. Event receipts cannot multiply these counts.
        var matches = from job in db.CampaignDeliveryJobs.AsNoTracking()
            join recipient in db.NewsletterRecipients on new { job.CampaignId, job.SubscriberId } equals new { recipient.CampaignId, recipient.SubscriberId }
            join fact in db.ProviderEmailObservations on new { MessageId = job.ProviderMessageId, recipient.AddressKey }
                equals new { MessageId = (Guid?)fact.MessageId, fact.AddressKey }
            where job.CampaignId == campaignId && job.State == CampaignDeliveryState.Accepted
            select new { fact.DeliveredUtc, fact.BouncedUtc, fact.ComplainedUtc, fact.TemporaryBounceUtc };
        var provider = await matches.TagWith("F41 correlated provider facts").GroupBy(x => 1)
            .Select(g => new CampaignProviderFacts(g.LongCount(x => x.DeliveredUtc != null), g.LongCount(x => x.BouncedUtc != null),
                g.LongCount(x => x.ComplainedUtc != null), g.LongCount(x => x.TemporaryBounceUtc != null))).SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);
        await tx.CommitAsync(ct);
        return new(campaign, selection, selected, dispatch, jobs, attempts, provider);
    }
}
