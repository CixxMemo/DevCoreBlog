using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Arms an explicit internal intent and prepares at most ten immutable messages; neither source save nor recipient selection calls it.</summary>
public sealed class CampaignPreparationService(ICampaignDispatchStore store,CampaignDeliveryProtector protection,
    CampaignDeliveryReadiness readiness,IEmailSubmissionContext submission,TimeProvider clock)
{
    public async Task<CampaignArmOutcome> ArmAsync(Guid campaignId,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!readiness.IsReady)return CampaignArmOutcome.NotConfigured;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var message=await store.ReadMessageAsync(campaignId,deadline.Token);
        if(message is null)return CampaignArmOutcome.NotFound;
        if(!protection.MatchesSender(message))return CampaignArmOutcome.NotConfigured;
        return await store.ArmAsync(protection.PrepareDispatch(message,clock.GetUtcNow().UtcDateTime),deadline.Token);
    }
    public async Task<bool> PrepareOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!readiness.IsReady)return false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var page=await store.ReadPreparationAsync(clock.GetUtcNow().UtcDateTime,deadline.Token);
        if(page is null)return false;
        var context=protection.ReadDispatch(page.Dispatch);
        if(context is null || context.Binding!=submission.Binding || !protection.MatchesSender(page.Message))
        {
            await store.StopPreparationAsync(page.Dispatch.CampaignId,page.Dispatch.Version,clock.GetUtcNow().UtcDateTime,deadline.Token);return true;
        }
        var now=clock.GetUtcNow().UtcDateTime;
        var jobs=page.Addresses.Select(address=>protection.Prepare(page,address,context,now)).ToArray();
        await store.CommitPreparationAsync(page,jobs,now,deadline.Token);
        return true;
    }
}
