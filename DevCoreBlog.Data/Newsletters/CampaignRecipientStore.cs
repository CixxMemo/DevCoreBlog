using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Campaign lock serializes batches with cancellation; subscriber share locks protect the selected lifecycle facts.</summary>
public sealed class CampaignRecipientStore(ApplicationDbContext db) : ICampaignRecipientStore
{
    public async Task<RecipientSelectionProgress?> ReadAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var selection = await db.NewsletterRecipientSelections.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);
        return selection?.Progress();
    }

    /// <summary>Commits at most one keyset page with its exact member count; a failed page leaves the previous checkpoint intact.</summary>
    public async Task<RecipientBatchResult> AdvanceAsync(Guid campaignId, DateTime now, CancellationToken cancellationToken)
    {
        if (campaignId == Guid.Empty) return new(RecipientBatchStatus.Invalid);
        now = ReaderIdentityRules.Utc(now);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"NewsletterCampaigns\" WHERE \"Id\"={campaignId} FOR UPDATE", cancellationToken);
        var campaign = await db.NewsletterCampaigns.AsNoTracking().Where(x => x.Id == campaignId)
            .Select(x => new { x.State, x.CreatedUtc }).SingleOrDefaultAsync(cancellationToken);
        if (campaign is null) return new(RecipientBatchStatus.NotFound);
        var selection = await db.NewsletterRecipientSelections.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);
        if (campaign.State != NewsletterCampaignState.Prepared) return new(RecipientBatchStatus.Cancelled, selection?.Progress());
        if (now < campaign.CreatedUtc || (selection is not null && now < selection.UpdatedUtc)) return new(RecipientBatchStatus.InvalidTime, selection?.Progress());
        if (selection?.State == RecipientSelectionState.Complete) return new(RecipientBatchStatus.Complete, selection.Progress());
        if (selection is null)
        {
            var upper = await db.Subscribers.AsNoTracking().Where(s => s.CreatedUtc <= now && s.Id != Guid.Empty)
                .OrderByDescending(s => s.Id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
            selection = NewsletterRecipientSelection.Begin(campaignId, upper, now);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "NewsletterRecipientSelections" ("CampaignId","CutoffUtc","UpperSubscriberId","LastSubscriberId",
                    "ScannedCount","SelectedCount","State","UpdatedUtc","Version")
                VALUES ({campaignId},{selection.CutoffUtc},{selection.UpperSubscriberId},NULL,0,0,0,{now},1)
                """, cancellationToken);
        }
        var candidates = selection.UpperSubscriberId is { } ceiling
            ? await db.Subscribers.FromSqlInterpolated($"""
                SELECT "Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc",
                    "VerifiedUtc","UnsubscribedUtc","Version","PeriodId" FROM "Subscribers"
                WHERE "Id">{selection.LastSubscriberId ?? Guid.Empty} AND "Id"<={ceiling} AND "CreatedUtc"<={selection.CutoffUtc}
                ORDER BY "Id" LIMIT {NewsletterRecipientSelection.BatchSize} FOR SHARE
                """).AsNoTracking().ToListAsync(cancellationToken)
            : [];
        var added = 0;
        foreach (var subscriber in candidates)
        {
            if (subscriber.UpdatedUtc > selection.CutoffUtc || !SubscriberSendingRules.IsEligible(subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression)) continue;
            var recipient = NewsletterRecipient.Select(campaignId, SubscriberRepository.Snapshot(subscriber), selection.CutoffUtc, now, checked(selection.Version + 1));
            added += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "NewsletterRecipients" ("CampaignId","SubscriberId","SubscriberVersion","SelectionVersion","PeriodId","EmailAddress","AddressKey","SelectedUtc")
                VALUES ({campaignId},{recipient.SubscriberId},{recipient.SubscriberVersion},{recipient.SelectionVersion},{recipient.PeriodId},{recipient.EmailAddress},{recipient.AddressKey},{now})
                """, cancellationToken);
        }
        var expected = selection.Version;
        selection.Advance(candidates.Count, added, candidates.Count > 0 ? candidates[^1].Id : null, now);
        var updated = await db.NewsletterRecipientSelections.Where(x => x.CampaignId == campaignId && x.Version == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSubscriberId, selection.LastSubscriberId)
                .SetProperty(x => x.ScannedCount, selection.ScannedCount).SetProperty(x => x.SelectedCount, selection.SelectedCount)
                .SetProperty(x => x.State, selection.State).SetProperty(x => x.UpdatedUtc, now).SetProperty(x => x.Version, selection.Version), cancellationToken);
        if (updated != 1) throw new InvalidOperationException("Selection progress conflict.");
        await tx.CommitAsync(cancellationToken);
        return new(selection.State == RecipientSelectionState.Complete ? RecipientBatchStatus.Complete : RecipientBatchStatus.Advanced,
            selection.Progress(), candidates.Count, added);
    }
}
