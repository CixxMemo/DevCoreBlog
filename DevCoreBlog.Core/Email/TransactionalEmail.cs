namespace DevCoreBlog.Core.Email;

public enum TransactionalEmailPurpose { Verification = 0, ReaderLogin = 1 }

public enum TransactionalEmailState { Pending, Claimed, Sending, Accepted, Rejected, Held, Expired, Cancelled }
public enum TransactionalEmailProblem { None, LeaseLost, InvalidPayload, ConfigurationChanged, SubscriberChanged, Suppressed, AttemptsExhausted, ProviderUncertain, ConfigurationUnavailable, LifetimeEnded }
public enum EmailQueueOutcome { Queued, AlreadyQueued, Conflict, Ineligible }

/// <summary>Timing and resource bounds for ownership-verification mail, independent of reader login or newsletters.</summary>
public static class TransactionalEmailRules
{
    public const int MaximumAttempts = 5;
    public const int MaximumProtectedBytes = 4_194_304;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaximumRetryAge = TimeSpan.FromHours(23);
    public static readonly TimeSpan MinimumSendLifetime = TimeSpan.FromSeconds(30);
    public static bool CanSend(TransactionalEmailPurpose purpose, DevCoreBlog.Core.Subscriptions.SubscriptionStatus status,
        DateTime? verified, DevCoreBlog.Core.Subscriptions.SendingSuppression suppression) =>
        purpose switch
        {
            TransactionalEmailPurpose.Verification => status == DevCoreBlog.Core.Subscriptions.SubscriptionStatus.PendingVerification
                && suppression == DevCoreBlog.Core.Subscriptions.SendingSuppression.None,
            TransactionalEmailPurpose.ReaderLogin => DevCoreBlog.Core.Subscriptions.ReaderIdentityRules.CanAccess(status, verified, suppression),
            _ => false
        };
    public static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value == DateTime.MinValue || value == DateTime.MaxValue)
            throw new ArgumentException("Finite UTC is required.");
    }
}

/// <summary>Persistence model contains encrypted content and no plaintext recipient, token or body.</summary>
public sealed class TransactionalEmailJob
{
    public Guid Id { get; private set; }
    public TransactionalEmailPurpose Purpose { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public string? ProtectedPayload { get; private set; }
    public DateTime? PayloadRedactedUtc { get; private set; }
    public DateTime? TerminalUtc { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime DueUtc { get; private set; }
    public DateTime? FirstAttemptUtc { get; private set; }
    public TransactionalEmailState State { get; private set; }
    public TransactionalEmailProblem Problem { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public Guid? ProviderMessageId { get; private set; }
}

/// <summary>Durable attempt journal; unfinished attempts remain explicit after a crash.</summary>
public sealed class TransactionalEmailAttempt
{
    public Guid JobId { get; private set; }
    public int Number { get; private set; }
    public Guid LeaseId { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public EmailSendOutcome? Outcome { get; private set; }
    public EmailSendReason? Reason { get; private set; }
    public int? HttpStatus { get; private set; }
    public Guid? ProviderMessageId { get; private set; }
}

/// <summary>Encrypted immutable staging value; may enlist in the caller's subscriber/token transaction.</summary>
public sealed class PreparedTransactionalEmail
{
    public Guid Id { get; }
    public TransactionalEmailPurpose Purpose { get; }
    public Guid SubscriberId { get; }
    public long SubscriberVersion { get; }
    public string ProtectedPayload { get; }
    public string PayloadHash { get; }
    public DateTime CreatedUtc { get; }
    public DateTime ExpiresUtc { get; }
    public PreparedTransactionalEmail(Guid id, Guid subscriberId, long subscriberVersion, string protectedPayload,
        string payloadHash, DateTime createdUtc, DateTime expiresUtc, TransactionalEmailPurpose purpose = TransactionalEmailPurpose.Verification)
    {
        TransactionalEmailRules.RequireUtc(createdUtc); TransactionalEmailRules.RequireUtc(expiresUtc);
        if (!Enum.IsDefined(purpose) || id == Guid.Empty || subscriberId == Guid.Empty || subscriberVersion <= 0
            || expiresUtc - createdUtc < TransactionalEmailRules.MinimumSendLifetime
            || expiresUtc - createdUtc > TransactionalEmailRules.MaximumLifetime
            || string.IsNullOrEmpty(protectedPayload) || protectedPayload.Length > TransactionalEmailRules.MaximumProtectedBytes
            || protectedPayload.Any(c => c > 127 || char.IsWhiteSpace(c))
            || payloadHash.Length != 64 || payloadHash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid transactional email envelope.");
        Id = id; Purpose = purpose; SubscriberId = subscriberId; SubscriberVersion = subscriberVersion;
        ProtectedPayload = protectedPayload; PayloadHash = payloadHash;
        CreatedUtc = createdUtc; ExpiresUtc = expiresUtc;
    }
}

/// <summary>Worker claim; never log this object or serialize it to an HTTP response.</summary>
public sealed record TransactionalEmailLease(Guid Id, Guid SubscriberId, long SubscriberVersion,
    string ProtectedPayload, string PayloadHash, DateTime ExpiresUtc, DateTime? FirstAttemptUtc,
    int AttemptCount, Guid LeaseId, DateTime LeaseUntilUtc)
{
    public TransactionalEmailPurpose Purpose { get; init; }
    public override string ToString() => nameof(TransactionalEmailLease);
}

public sealed record TransactionalEmailStart(int AttemptNumber, DateTime FirstAttemptUtc);
public sealed record TransactionalEmailCompletion(TransactionalEmailState State, TransactionalEmailProblem Problem,
    DateTime DueUtc, EmailSendResult Result);

/// <summary>Reports readiness and an opaque transport binding; binding must stay inside the encrypted envelope.</summary>
public interface IEmailSubmissionContext
{
    bool IsConfigured { get; }
    string? Binding { get; }
}

/// <summary>Stages mail in an existing database transaction when one is present; never commits the caller's transaction.</summary>
public interface ITransactionalEmailQueue
{
    Task<EmailQueueOutcome> EnqueueAsync(PreparedTransactionalEmail email, CancellationToken cancellationToken);
}

/// <summary>Fenced claims, journal and completion; no provider calls while a database transaction is open.</summary>
public interface ITransactionalEmailWorkStore
{
    Task<int> RecoverAsync(DateTime utcNow, CancellationToken cancellationToken);
    Task<TransactionalEmailLease?> ClaimAsync(DateTime utcNow, CancellationToken cancellationToken);
    Task<TransactionalEmailStart?> StartAsync(TransactionalEmailLease lease, string addressKey, DateTime utcNow, CancellationToken cancellationToken);
    Task<bool> HoldAsync(TransactionalEmailLease lease, TransactionalEmailProblem problem, DateTime utcNow, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(TransactionalEmailLease lease, int attemptNumber, TransactionalEmailCompletion completion, DateTime utcNow, CancellationToken cancellationToken);
}
