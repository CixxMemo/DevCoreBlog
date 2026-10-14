namespace DevCoreBlog.Core.Subscriptions;

/// <summary>Narrow lifecycle persistence; reads do not reactivate or grant archive access.</summary>
public interface ISubscriberRepository
{
    Task<SubscriberSnapshot> GetOrCreatePendingAsync(SubscriberAddress address, DateTime utcNow, CancellationToken cancellationToken);
    Task<SubscriberSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<SubscriberWriteOutcome> TryApplyAsync(Guid id, long expectedVersion, SubscriberChange change,
        DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>Immutable subscription facts; sending eligibility is independent of future archive policy.</summary>
public sealed record SubscriberSnapshot(Guid Id, string EmailAddress, string AddressKey,
    SubscriptionStatus Status, SendingSuppression Suppression, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? VerifiedUtc, DateTime? UnsubscribedUtc, long Version, Guid PeriodId)
{
    public bool IsSendEligible => SubscriberSendingRules.IsEligible(Status, VerifiedUtc, Suppression);
}

public enum SubscriberWriteOutcome { Changed, Unchanged, Conflict, NotFound, InvalidTransition, InvalidTime }
