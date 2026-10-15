using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Services.Email;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>One preparation page or one fenced send per turn; in-flight uncertainty never becomes an automatic replay.</summary>
public sealed class CampaignDeliveryDispatcher(ICampaignDeliveryWorkStore store,CampaignPreparationService preparation,
    CampaignDeliveryProtector protection,CampaignDeliveryReadiness readiness,IEmailSubmissionContext submission,IEmailSender sender,TimeProvider clock)
{
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!readiness.IsReady)return false;
        await store.RecoverAsync(clock.GetUtcNow().UtcDateTime,cancellationToken);
        if(await preparation.PrepareOnceAsync(cancellationToken))return true;
        var lease=await store.ClaimAsync(clock.GetUtcNow().UtcDateTime,cancellationToken);
        if(lease is null)return false;
        var payload=protection.ReadJob(lease);
        if(payload is null || payload.Binding!=submission.Binding)
        {
            await store.HoldAsync(lease,payload is null?CampaignDeliveryProblem.InvalidPayload:CampaignDeliveryProblem.ConfigurationChanged,
                clock.GetUtcNow().UtcDateTime,cancellationToken);return true;
        }
        var start=await store.StartAsync(lease,payload.AddressKey,clock.GetUtcNow().UtcDateTime,cancellationToken);
        if(start is null)return true;
        EmailSendResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result=await sender.SendAsync(new EmailSendRequest(payload.Recipient,payload.Subject,payload.Html,payload.Text,
                CampaignDeliveryProtector.Key(lease.Id),new DateTimeOffset(start.FirstAttemptUtc),EmailPurpose.Newsletter,payload.HeaderExit),cancellationToken);
            if(result.Outcome==EmailSendOutcome.Accepted && (result.ProviderMessageId is null || result.ProviderMessageId==Guid.Empty))
                result=new(EmailSendOutcome.Uncertain,EmailSendReason.Protocol);
        }
        catch(OperationCanceledException){result=new(EmailSendOutcome.Uncertain,EmailSendReason.Cancelled);}
        catch(Exception){result=new(EmailSendOutcome.Uncertain,EmailSendReason.Transport);}
        var now=clock.GetUtcNow().UtcDateTime;
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await store.CompleteAsync(lease,start,CampaignDeliveryRetryPolicy.Decide(lease,start,result,now),now,finish.Token);
        return true;
    }
}

/// <summary>Classifies provider acceptance separately from delivery, and schedules only definitive failures inside the original key window.</summary>
public static class CampaignDeliveryRetryPolicy
{
    public static CampaignDeliveryCompletion Decide(CampaignDeliveryLease lease,CampaignDeliveryStart start,EmailSendResult result,DateTime now)
    {
        if(result.Outcome!=EmailSendOutcome.Retryable)
        {
            var state=result.Outcome switch {EmailSendOutcome.Accepted=>CampaignDeliveryState.Accepted,EmailSendOutcome.Rejected=>CampaignDeliveryState.Rejected,_=>CampaignDeliveryState.Held};
            var problem=state is CampaignDeliveryState.Accepted or CampaignDeliveryState.Rejected?CampaignDeliveryProblem.None:
                result.Outcome==EmailSendOutcome.NotConfigured?CampaignDeliveryProblem.ConfigurationUnavailable:CampaignDeliveryProblem.ProviderUncertain;
            return new(state,problem,now,result);
        }
        var retry=EmailRetrySchedule.Decide(start.AttemptNumber,start.FirstAttemptUtc,lease.ExpiresUtc,result,now);
        return retry.Outcome switch
        {
            RetryScheduleOutcome.Ready=>new(CampaignDeliveryState.Pending,CampaignDeliveryProblem.None,retry.DueUtc,result),
            RetryScheduleOutcome.Exhausted=>new(CampaignDeliveryState.Held,CampaignDeliveryProblem.AttemptsExhausted,now,result),
            RetryScheduleOutcome.MonthlyQuota=>new(CampaignDeliveryState.Held,CampaignDeliveryProblem.ConfigurationUnavailable,now,result),
            _=>new(CampaignDeliveryState.Expired,CampaignDeliveryProblem.LifetimeEnded,now,result)
        };
    }
}
