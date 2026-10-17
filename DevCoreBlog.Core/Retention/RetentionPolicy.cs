namespace DevCoreBlog.Core.Retention;

/// <summary>Approved K8 bounds. Counts are capped samples, never unbounded totals or delivery metrics.</summary>
public static class RetentionPolicy
{
    public const int BatchSize = 25;
    public const int SampleLimit = BatchSize + 1;
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
}

public sealed record RetentionCounts(int VerificationTokens, int LoginTokens, int Sessions,
    int Payloads, int ProviderDetails, int OrphanPending, int PendingHistoryExceptions, int HeldReviewDue);

public sealed record RetentionReport(DateTime ObservedUtc, bool DryRun, bool ClockRejected, RetentionCounts Counts);

/// <summary>Local persistence only; no provider, key-ring, backup or file-system side effects.</summary>
public interface IRetentionStore
{
    Task<RetentionReport> RunAsync(DateTime utcNow, bool dryRun, CancellationToken cancellationToken);
}

/// <summary>Restart-persistent lower time bound; missing/invalid state must never authorize cleanup.</summary>
public sealed class RetentionCheckpoint
{
    public int Id { get; private set; }
    public DateTime LastAppliedUtc { get; private set; }
}
