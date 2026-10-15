using DevCoreBlog.Services.Newsletters;

namespace DevCoreBlog.Infrastructure;

/// <summary>Opt-in single-process polling; durable jobs and fenced leases provide restart behavior, not this timer.</summary>
public sealed class CampaignDeliveryWorker(IServiceScopeFactory scopes,CampaignDeliveryReadiness readiness,ILogger<CampaignDeliveryWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            if(readiness.IsReady)
            {
                try
                {
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    await using var scope=scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<CampaignDeliveryDispatcher>().RunOnceAsync(deadline.Token);
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception){logger.LogWarning("Campaign delivery could not complete; durable state is retained.");}
            }
            try{await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);}catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
