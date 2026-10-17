using DevCoreBlog.Core.Retention;

namespace DevCoreBlog.Services.Retention;

/// <summary>Inspect first, then optionally apply with a fresh clock; no request can opt in through input binding.</summary>
public sealed class RetentionService(IRetentionStore store, TimeProvider clock)
{
    public async Task<RetentionReport> RunOnceAsync(bool apply, CancellationToken cancellationToken)
    {
        var preview = await store.RunAsync(clock.GetUtcNow().UtcDateTime, true, cancellationToken);
        if (!apply || preview.ClockRejected) return preview;
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < preview.ObservedUtc) return preview with { ClockRejected = true };
        return await store.RunAsync(now, false, cancellationToken);
    }
}
