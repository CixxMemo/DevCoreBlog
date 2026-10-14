namespace DevCoreBlog.Core.Subscriptions;

public enum UnsubscriptionOutcome { Unsubscribed, Invalid }

/// <summary>Authenticated link claims authorize only sending exit for one address and subscription period.</summary>
public sealed record SubscriberExitProof(Guid SubscriberId, Guid PeriodId, string AddressBinding)
{
    public override string ToString() => nameof(SubscriberExitProof);
}

/// <summary>Serializes exit with confirmation and reapplication; never grants archive access.</summary>
public interface ISubscriberUnsubscriptionStore
{
    Task<UnsubscriptionOutcome> UnsubscribeAsync(SubscriberExitProof proof, DateTime utcNow, CancellationToken cancellationToken);
}
