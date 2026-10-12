namespace DevCoreBlog.Core.Subscriptions;

/// <summary>The shared sending rule grants no reader or administrator permission.</summary>
public static class SubscriberSendingRules
{
    public static bool IsEligible(SubscriptionStatus status, DateTime? verifiedUtc, SendingSuppression suppression) =>
        status == SubscriptionStatus.Active && verifiedUtc.HasValue && suppression == SendingSuppression.None;
}
