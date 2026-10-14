using DevCoreBlog.Core.Email;
using DevCoreBlog.Services.Email;

namespace DevCoreBlog.Infrastructure;

/// <summary>Single-process bounded polling; opt-in and missing sender configuration both keep submission closed.</summary>
public sealed class TransactionalEmailWorker(IServiceScopeFactory scopes, IEmailSubmissionContext submission,
    ILogger<TransactionalEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (submission.IsConfigured)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TransactionalEmailDispatcher>().RunOnceAsync(deadline.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    // Exception/provider/SQL text can contain private input. Export only the fixed operational event.
                    logger.LogWarning("Transactional email processing could not complete; durable state is retained.");
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
