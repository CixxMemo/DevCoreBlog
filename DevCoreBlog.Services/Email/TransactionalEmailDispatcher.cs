using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Services.Email;

/// <summary>Runs at most one fenced verification attempt; restart recovery never blindly replays an in-flight send.</summary>
public sealed class TransactionalEmailDispatcher(ITransactionalEmailWorkStore store, TransactionalEmailProtector protection,
    IEmailSubmissionContext submission, IEmailSender sender, TimeProvider clock)
{
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!submission.IsConfigured) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        await store.RecoverAsync(now, cancellationToken);
        var lease = await store.ClaimAsync(now, cancellationToken);
        if (lease is null) return false;
        var payload = protection.Read(lease);
        if (payload is null || payload.Binding != submission.Binding)
        {
            await store.HoldAsync(lease, payload is null ? TransactionalEmailProblem.InvalidPayload
                : TransactionalEmailProblem.ConfigurationChanged, clock.GetUtcNow().UtcDateTime, cancellationToken);
            return true;
        }
        var start = await store.StartAsync(lease, payload.AddressKey, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (start is null) return true;
        EmailSendResult result;
        try
        {
            var request = new EmailSendRequest(payload.Recipient, payload.Subject, payload.Html, payload.Text,
                TransactionalEmailProtector.Key(lease.Id), new DateTimeOffset(start.FirstAttemptUtc));
            result = await sender.SendAsync(request, cancellationToken);
            if (result.Outcome == EmailSendOutcome.Accepted && (result.ProviderMessageId is null || result.ProviderMessageId == Guid.Empty))
                result = new(EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        }
        catch (OperationCanceledException) { result = new(EmailSendOutcome.Uncertain, EmailSendReason.Cancelled); }
        catch (Exception) { result = new(EmailSendOutcome.Uncertain, EmailSendReason.Transport); }
        now = clock.GetUtcNow().UtcDateTime;
        var completion = TransactionalEmailRetryPolicy.Decide(lease, start, result, now);
        // Shutdown cancellation must not discard a known provider result. A bounded independent save is best effort.
        using var finishDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await store.CompleteAsync(lease, start.AttemptNumber, completion, now, finishDeadline.Token);
        return true;
    }
}

/// <summary>Bounded retry policy applies only to explicit retryable responses, never uncertain outcomes.</summary>
internal static class TransactionalEmailRetryPolicy
{
    public static TransactionalEmailCompletion Decide(TransactionalEmailLease lease, TransactionalEmailStart start,
        EmailSendResult result, DateTime now)
    {
        var state = result.Outcome switch
        {
            EmailSendOutcome.Accepted => TransactionalEmailState.Accepted,
            EmailSendOutcome.Rejected => TransactionalEmailState.Rejected,
            _ => TransactionalEmailState.Held
        };
        var problem = state is TransactionalEmailState.Accepted or TransactionalEmailState.Rejected
            ? TransactionalEmailProblem.None : TransactionalEmailProblem.ProviderUncertain;
        if (result.Outcome == EmailSendOutcome.NotConfigured) problem = TransactionalEmailProblem.ConfigurationUnavailable;
        if (result.Outcome != EmailSendOutcome.Retryable) return new(state, problem, now, result);
        var retry=EmailRetrySchedule.Decide(start.AttemptNumber,start.FirstAttemptUtc,lease.ExpiresUtc,result,now);
        return retry.Outcome switch
        {
            RetryScheduleOutcome.Ready=>new(TransactionalEmailState.Pending,TransactionalEmailProblem.None,retry.DueUtc,result),
            RetryScheduleOutcome.Exhausted=>new(TransactionalEmailState.Held,TransactionalEmailProblem.AttemptsExhausted,now,result),
            RetryScheduleOutcome.MonthlyQuota=>new(TransactionalEmailState.Held,TransactionalEmailProblem.ConfigurationUnavailable,now,result),
            _=>new(TransactionalEmailState.Expired,TransactionalEmailProblem.LifetimeEnded,now,result)
        };
    }
}
