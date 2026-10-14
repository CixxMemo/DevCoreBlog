namespace DevCoreBlog.Core.Subscriptions;

public enum SubscriptionStatus { PendingVerification = 0, Active = 1, Unsubscribed = 2 }

[Flags]
public enum SendingSuppression { None = 0, Bounce = 1, Complaint = 2, Manual = 4 }

public enum SubscriberChange { ConfirmOwnership, Unsubscribe, RequestReconfirmation, SuppressBounce, SuppressComplaint, SuppressManual }
public enum SubscriberTransition { Changed, Unchanged, InvalidTransition, InvalidTime }

/// <summary>Subscription lifecycle only; confirmation callers must first verify ownership proof.</summary>
public sealed class Subscriber
{
    private Subscriber() { }
    public Guid Id { get; private set; }
    public string EmailAddress { get; private set; } = string.Empty;
    public string AddressKey { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public SendingSuppression Suppression { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? VerifiedUtc { get; private set; }
    public DateTime? UnsubscribedUtc { get; private set; }
    public long Version { get; private set; }
    // Stable across suppression and exit; a new application starts a distinct subscription period.
    public Guid PeriodId { get; private set; }
    public bool IsSendEligible => SubscriberSendingRules.IsEligible(Status, VerifiedUtc, Suppression);

    public static Subscriber CreatePending(SubscriberAddress address, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsFiniteUtc(utcNow)) throw new ArgumentException("Finite UTC is required.", nameof(utcNow));
        return new Subscriber
        {
            Id = Guid.NewGuid(), PeriodId = Guid.NewGuid(), EmailAddress = address.Address, AddressKey = address.Key,
            CreatedUtc = utcNow, UpdatedUtc = utcNow, Version = 1
        };
    }

    public SubscriberTransition Apply(SubscriberChange change, DateTime utcNow)
    {
        if (!Enum.IsDefined(change)) return SubscriberTransition.InvalidTransition;
        if (!IsFiniteUtc(utcNow) || utcNow < UpdatedUtc) return SubscriberTransition.InvalidTime;
        var status = Status;
        var verified = VerifiedUtc;
        var exited = UnsubscribedUtc;
        var suppression = Suppression;
        switch (change)
        {
            case SubscriberChange.ConfirmOwnership:
                if (Status == SubscriptionStatus.Unsubscribed) return SubscriberTransition.InvalidTransition;
                if (Status == SubscriptionStatus.PendingVerification) { status = SubscriptionStatus.Active; verified = utcNow; }
                break;
            case SubscriberChange.Unsubscribe:
                if (Status != SubscriptionStatus.Unsubscribed) { status = SubscriptionStatus.Unsubscribed; exited = utcNow; }
                break;
            case SubscriberChange.RequestReconfirmation:
                // Existing active subscriptions are not disabled by a repeated public registration.
                if (Status == SubscriptionStatus.Active) return SubscriberTransition.InvalidTransition;
                status = SubscriptionStatus.PendingVerification;
                verified = null;
                break;
            case SubscriberChange.SuppressBounce: suppression |= SendingSuppression.Bounce; break;
            case SubscriberChange.SuppressComplaint: suppression |= SendingSuppression.Complaint; break;
            case SubscriberChange.SuppressManual: suppression |= SendingSuppression.Manual; break;
        }
        if (status == Status && verified == VerifiedUtc && exited == UnsubscribedUtc && suppression == Suppression)
            return SubscriberTransition.Unchanged;
        if (Version == long.MaxValue) return SubscriberTransition.InvalidTransition;
        if (change == SubscriberChange.RequestReconfirmation && Status == SubscriptionStatus.Unsubscribed)
            PeriodId = Guid.NewGuid();
        Status = status; VerifiedUtc = verified; UnsubscribedUtc = exited; Suppression = suppression;
        UpdatedUtc = utcNow; Version++;
        return SubscriberTransition.Changed;
    }

    private static bool IsFiniteUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        && value != DateTime.MinValue && value != DateTime.MaxValue;
}
