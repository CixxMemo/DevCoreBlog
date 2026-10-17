using DevCoreBlog.Services.Retention;

namespace DevCoreBlog.Infrastructure;

public sealed record RetentionWorkerOptions(bool Apply);

/// <summary>Opt-in hourly maintenance. Sender, recipient, token, SQL and exception text never enter logs.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, RetentionWorkerOptions options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(45));
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().RunOnceAsync(options.Apply, deadline.Token);
                if (result.ClockRejected)
                    logger.LogWarning("Retention clock moved backwards; no current candidate counts are available and cleanup is closed.");
                else
                {
                    var c = result.Counts;
                    logger.LogInformation("Retention dryRun={DryRun} verification={Verification} login={Login} sessions={Sessions} payloads={Payloads} providerDetails={ProviderDetails} orphanPending={OrphanPending} pendingExceptions={PendingExceptions} heldReviewDue={HeldReviewDue}; inspection counts capped at 26, mutations at 25 per class.",
                        result.DryRun, c.VerificationTokens, c.LoginTokens, c.Sessions, c.Payloads, c.ProviderDetails, c.OrphanPending, c.PendingHistoryExceptions, c.HeldReviewDue);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Retention could not complete; no success counts are available. Inspect durable state before resuming.");
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
