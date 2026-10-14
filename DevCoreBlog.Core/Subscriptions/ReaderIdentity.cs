using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Core.Subscriptions;

/// <summary>Active ownership is checked independently of an administrator identity on every reader request.</summary>
public static class ReaderIdentityRules
{
    public const int TokenPurpose = 2;
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    public const int MaximumDailyEmails = 20;
    public static bool CanAccess(SubscriptionStatus status, DateTime? verified, SendingSuppression suppression) =>
        status == SubscriptionStatus.Active && verified.HasValue && suppression == SendingSuppression.None;
    public static DateTime Utc(DateTime value)
    {
        TransactionalEmailRules.RequireUtc(value);
        return value.AddTicks(-(value.Ticks % 10));
    }
}

/// <summary>Only purpose-bound digests and lifecycle facts persist outside encrypted mail.</summary>
public sealed class ReaderLoginToken
{
    public Guid Id { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public Guid PeriodId { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public string AddressBinding { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
}

/// <summary>A random session identity is revocable in PostgreSQL; no email address is put in a cookie.</summary>
public sealed class ReaderSession
{
    private ReaderSession() { }
    public Guid Id { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public Guid PeriodId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public static ReaderSession Create(SubscriberSnapshot subscriber, DateTime now)
    {
        now = ReaderIdentityRules.Utc(now);
        if (!ReaderIdentityRules.CanAccess(subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression) || now < subscriber.UpdatedUtc)
            throw new ArgumentException("Reader is not eligible.");
        return new() { Id = Guid.NewGuid(), SubscriberId = subscriber.Id, SubscriberVersion = subscriber.Version,
            PeriodId = subscriber.PeriodId, CreatedUtc = now, ExpiresUtc = now + ReaderIdentityRules.SessionLifetime };
    }
}

/// <summary>A durable singleton bounds login mail across IPs, restarts and subscription periods.</summary>
public sealed class ReaderLoginBudget
{
    public int Id { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public int Count { get; private set; }
}

public sealed record ReaderLoginDraft(Guid Id, Guid SubscriberId, long SubscriberVersion, Guid PeriodId,
    string Digest, string AddressBinding, DateTime CreatedUtc, DateTime ExpiresUtc)
{
    public override string ToString() => nameof(ReaderLoginDraft);
}
public sealed record PreparedReaderLogin(ReaderLoginDraft Token, PreparedTransactionalEmail Email);
public sealed record ReaderSessionGrant(Guid Id, DateTime CreatedUtc, DateTime ExpiresUtc);

/// <summary>Request admission and proof consumption are atomic; invalid requests never create a reader identity.</summary>
public interface IReaderIdentityStore
{
    Task RequestAsync(SubscriberAddress address, DateTime now, Func<SubscriberSnapshot, PreparedReaderLogin> prepare, CancellationToken ct);
    Task<ReaderSessionGrant?> ConsumeAsync(string digest, DateTime now, CancellationToken ct);
    Task<bool> IsValidAsync(Guid sessionId, DateTime now, CancellationToken ct);
    Task RevokeAsync(Guid sessionId, DateTime now, CancellationToken ct);
}
