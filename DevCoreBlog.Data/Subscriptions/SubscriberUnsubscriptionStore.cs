using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Uses the same subscriber row lock as verification, admission and outbox start.</summary>
public sealed class SubscriberUnsubscriptionStore(ApplicationDbContext context, SubscriberRepository subscribers)
    : ISubscriberUnsubscriptionStore
{
    public async Task<UnsubscriptionOutcome> UnsubscribeAsync(SubscriberExitProof proof, DateTime utcNow, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        if (proof.SubscriberId == Guid.Empty || proof.PeriodId == Guid.Empty) return UnsubscriptionOutcome.Invalid;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var subscriber = (await context.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"Id\"={proof.SubscriberId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
        if (subscriber is null || subscriber.PeriodId != proof.PeriodId
            || SubscriberVerificationRules.AddressBinding(subscriber.AddressKey) != proof.AddressBinding)
            return UnsubscriptionOutcome.Invalid;
        // A replay does not rewrite timestamps or increment a revision, even if the clock moved back.
        if (subscriber.Status == SubscriptionStatus.Unsubscribed) return UnsubscriptionOutcome.Unsubscribed;
        var result = await subscribers.TryApplyAsync(subscriber.Id, subscriber.Version, SubscriberChange.Unsubscribe, utcNow, cancellationToken);
        if (result != SubscriberWriteOutcome.Changed) return UnsubscriptionOutcome.Invalid;
        await transaction.CommitAsync(cancellationToken);
        return UnsubscriptionOutcome.Unsubscribed;
    }
}
