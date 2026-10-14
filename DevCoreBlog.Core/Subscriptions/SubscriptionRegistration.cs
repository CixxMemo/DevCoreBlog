using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Core.Subscriptions;

public enum SubscriptionRegistrationOutcome { Accepted, InvalidAddress, Unavailable }
public enum RegistrationWriteOutcome { Queued, NoAction }

/// <summary>Conservative public issuance bounds; these count queued verification jobs, not provider billing or delivery.</summary>
public static class SubscriptionRegistrationRules
{
    public const int MaximumDailyEmails = 20;
    public static readonly TimeSpan AddressCooldown = TimeSpan.FromDays(1);
    public static readonly TimeSpan MinimumResponseTime = TimeSpan.FromMilliseconds(250);
}

/// <summary>Only encrypted mail and proof digests cross the persistence callback boundary.</summary>
public sealed record PreparedSubscriberVerification(SubscriberVerificationDraft Token, PreparedTransactionalEmail Email)
{
    public override string ToString() => nameof(PreparedSubscriberVerification);
}

/// <summary>Singleton UTC-day admission counter; permanent token/outbox rows retain the issuance evidence.</summary>
public sealed class SubscriptionRegistrationBudget
{
    public int Id { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public int Count { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
}

/// <summary>Atomically stages lifecycle, proof and mail under durable admission limits. Prepare must be pure local work without I/O.</summary>
public interface ISubscriptionRegistrationStore
{
    Task<RegistrationWriteOutcome> RegisterAsync(SubscriberAddress address, DateTime utcNow,
        Func<SubscriberSnapshot, PreparedSubscriberVerification> prepare, CancellationToken cancellationToken);
}
