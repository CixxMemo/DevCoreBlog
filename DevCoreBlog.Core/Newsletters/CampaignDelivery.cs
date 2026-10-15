using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Core.Newsletters;

public enum CampaignPreparationState { Building, Complete, Stopped }
public enum CampaignArmOutcome { Armed, Existing, NotFound, InvalidState, NotConfigured, Unavailable }
public enum CampaignDeliveryState { Pending, Claimed, Sending, Accepted, Rejected, Held, Expired, Cancelled }
public enum CampaignDeliveryProblem { None, LeaseLost, InvalidPayload, ConfigurationChanged, SubscriberChanged, Suppressed, AttemptsExhausted, ProviderUncertain, ConfigurationUnavailable, LifetimeEnded, CampaignCancelled }

/// <summary>Campaign work shares the existing conservative per-message retry limits, never a new provider key after uncertainty.</summary>
public static class CampaignDeliveryRules
{
    public const int PreparationBatchSize = 10;
    public const int MaximumAttempts = TransactionalEmailRules.MaximumAttempts;
    public const int MaximumProtectedBytes = TransactionalEmailRules.MaximumProtectedBytes;
    public static TimeSpan Lease => TransactionalEmailRules.Lease;
    public static TimeSpan MaximumLifetime => TransactionalEmailRules.MaximumLifetime;
    public static TimeSpan MaximumRetryAge => TransactionalEmailRules.MaximumRetryAge;
    public static TimeSpan MinimumSendLifetime => TransactionalEmailRules.MinimumSendLifetime;
}

/// <summary>Explicit internal sending authorization and durable preparation progress; preparing a site campaign does not create this.</summary>
public sealed class CampaignDispatch
{
    private CampaignDispatch() { }
    public Guid CampaignId { get; private set; }
    public string ProtectedContext { get; private set; } = string.Empty;
    public string ContextHash { get; private set; } = string.Empty;
    public string SnapshotHash { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? LastSubscriberId { get; private set; }
    public long StagedCount { get; private set; }
    public CampaignPreparationState PreparationState { get; private set; }
    public long Version { get; private set; }
    public override string ToString() => nameof(CampaignDispatch);
}

/// <summary>Immutable encrypted wire message plus a fenced mutable work state; acceptance is not delivery.</summary>
public sealed class CampaignDeliveryJob
{
    private CampaignDeliveryJob() { }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public Guid PeriodId { get; private set; }
    public long PreparationVersion { get; private set; }
    public string ProtectedPayload { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime DueUtc { get; private set; }
    public DateTime? FirstAttemptUtc { get; private set; }
    public CampaignDeliveryState State { get; private set; }
    public CampaignDeliveryProblem Problem { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public Guid? ProviderMessageId { get; private set; }
    public long Version { get; private set; }
    public override string ToString() => nameof(CampaignDeliveryJob);
}

/// <summary>Each actual durable start has one append-only attempt; only its unfinished result may be completed once.</summary>
public sealed class CampaignDeliveryAttempt
{
    private CampaignDeliveryAttempt() { }
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

public sealed record CampaignMessage(Guid Id, string Subject, string Html, string Text, string SnapshotHash,
    string SenderEmail, string SenderName, string? ReplyTo, string ProviderContract)
{ public override string ToString() => nameof(CampaignMessage); }
public sealed record CampaignSelectedAddress(Guid SubscriberId, long SubscriberVersion, Guid PeriodId, string EmailAddress, string AddressKey)
{ public override string ToString() => nameof(CampaignSelectedAddress); }
public sealed record CampaignPreparation(CampaignDispatch Dispatch, CampaignMessage Message, IReadOnlyList<CampaignSelectedAddress> Addresses, bool IsFinalPage)
{ public override string ToString() => nameof(CampaignPreparation); }
public sealed record PreparedCampaignDispatch(Guid CampaignId, string SnapshotHash, string ProtectedContext, string ContextHash, DateTime CreatedUtc, DateTime ExpiresUtc)
{ public override string ToString() => nameof(PreparedCampaignDispatch); }
public sealed record PreparedCampaignDelivery(Guid Id, Guid CampaignId, Guid SubscriberId, long SubscriberVersion, Guid PeriodId, long PreparationVersion,
    string ProtectedPayload, string PayloadHash, DateTime CreatedUtc, DateTime ExpiresUtc)
{ public override string ToString() => nameof(PreparedCampaignDelivery); }
public sealed record CampaignDeliveryLease(Guid Id, Guid CampaignId, Guid SubscriberId, long SubscriberVersion, Guid PeriodId,
    string ProtectedPayload, string PayloadHash, DateTime ExpiresUtc, DateTime? FirstAttemptUtc, int AttemptCount, Guid LeaseId, DateTime LeaseUntilUtc, long Version, string SnapshotHash)
{ public override string ToString() => nameof(CampaignDeliveryLease); }
public sealed record CampaignDeliveryStart(int AttemptNumber, DateTime FirstAttemptUtc, long Version);
public sealed record CampaignDeliveryCompletion(CampaignDeliveryState State, CampaignDeliveryProblem Problem, DateTime DueUtc, EmailSendResult Result);

/// <summary>Explicit intent and one durable preparation page; no public HTTP caller or provider submission.</summary>
public interface ICampaignDispatchStore
{
    Task<CampaignMessage?> ReadMessageAsync(Guid campaignId, CancellationToken cancellationToken);
    Task<CampaignArmOutcome> ArmAsync(PreparedCampaignDispatch dispatch, CancellationToken cancellationToken);
    Task<CampaignPreparation?> ReadPreparationAsync(DateTime now, CancellationToken cancellationToken);
    Task<bool> CommitPreparationAsync(CampaignPreparation page, IReadOnlyList<PreparedCampaignDelivery> jobs, DateTime now, CancellationToken cancellationToken);
    Task<bool> StopPreparationAsync(Guid campaignId, long expectedVersion, DateTime now, CancellationToken cancellationToken);
}

/// <summary>One claim per turn, bounded recovery and fenced attempt transitions; transport runs outside a transaction.</summary>
public interface ICampaignDeliveryWorkStore
{
    Task<int> RecoverAsync(DateTime now, CancellationToken cancellationToken);
    Task<CampaignDeliveryLease?> ClaimAsync(DateTime now, CancellationToken cancellationToken);
    Task<CampaignDeliveryStart?> StartAsync(CampaignDeliveryLease lease, string addressKey, DateTime now, CancellationToken cancellationToken);
    Task<bool> HoldAsync(CampaignDeliveryLease lease, CampaignDeliveryProblem problem, DateTime now, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(CampaignDeliveryLease lease, CampaignDeliveryStart start, CampaignDeliveryCompletion completion, DateTime now, CancellationToken cancellationToken);
}
