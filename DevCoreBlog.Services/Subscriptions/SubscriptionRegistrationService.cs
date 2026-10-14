using System.Diagnostics;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Explicit ingress switch is separate from the worker, which may finish already queued jobs after ingress closes.</summary>
public sealed class SubscriptionRegistrationAvailability(bool enabled, bool workerEnabled, IEmailSubmissionContext submission)
{
    public bool CanRegister => enabled && workerEnabled && submission.IsConfigured;
}

/// <summary>Valid requests share one public outcome without exposing existing identities, suppression or admission quotas.</summary>
public sealed class SubscriptionRegistrationService(ISubscriptionRegistrationStore store, SubscriberVerificationMessageFactory messages,
    SubscriptionRegistrationAvailability availability, TimeProvider clock)
{
    public async Task<SubscriptionRegistrationOutcome> RegisterAsync(string? input, CancellationToken cancellationToken)
    {
        if (!SubscriberAddress.TryCreate(input, out var address) || address is null) return SubscriptionRegistrationOutcome.InvalidAddress;
        if (!availability.CanRegister) return SubscriptionRegistrationOutcome.Unavailable;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));
            await store.RegisterAsync(address, now, subscriber => messages.Prepare(subscriber, now), cancellationToken);
            return SubscriptionRegistrationOutcome.Accepted;
        }
        finally
        {
            // A response floor masks ordinary local processing differences; this is not a constant-time network guarantee.
            var remaining = SubscriptionRegistrationRules.MinimumResponseTime - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
                await Task.Delay(remaining, cancellationToken);
        }
    }
}
