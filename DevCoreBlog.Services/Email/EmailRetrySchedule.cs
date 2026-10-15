using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Services.Email;

public enum RetryScheduleOutcome { Ready, Exhausted, MonthlyQuota, Expired }
public sealed record RetrySchedule(RetryScheduleOutcome Outcome,DateTime DueUtc);

/// <summary>Shared conservative timing for transaction and campaign messages; only definitive Retryable results enter here.</summary>
public static class EmailRetrySchedule
{
    public static RetrySchedule Decide(int number,DateTime first,DateTime expires,EmailSendResult result,DateTime now)
    {
        if(number>=TransactionalEmailRules.MaximumAttempts)return new(RetryScheduleOutcome.Exhausted,now);
        if(result.Reason==EmailSendReason.MonthlyQuota)return new(RetryScheduleOutcome.MonthlyQuota,now);
        var delay=TimeSpan.FromSeconds(number switch {1=>30,2=>120,3=>600,_=>1800});
        if(result.Reason==EmailSendReason.DailyQuota && result.RetryAfter is null)delay=TimeSpan.FromDays(1);
        if(result.RetryAfter is {} hint && hint>delay)delay=hint;
        if(delay<TimeSpan.Zero || delay>TimeSpan.FromDays(1) || now> DateTime.MaxValue-delay)return new(RetryScheduleOutcome.Expired,now);
        var due=now+delay;
        if(due>=expires-TransactionalEmailRules.MinimumSendLifetime || due-first>=TransactionalEmailRules.MaximumRetryAge)
            return new(RetryScheduleOutcome.Expired,now);
        return new(RetryScheduleOutcome.Ready,due);
    }
}
