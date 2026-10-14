namespace DevCoreBlog.Core.Email;

public enum ProviderEmailEventKind { Delivered = 0, Bounced = 1, Complained = 2, TemporaryBounce = 3 }
public enum ProviderEventOutcome { Applied, Duplicate, Conflict, Unavailable }

/// <summary>Minimal authenticated delivery fact. Payload, subject, SMTP details and tracking data are never persisted.</summary>
public sealed class ProviderEmailEvent
{
    private ProviderEmailEvent() { }
    public string EventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public string AddressKey { get; private set; } = string.Empty;
    public ProviderEmailEventKind Kind { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public DateTime ReceivedUtc { get; private set; }

    public static ProviderEmailEvent Create(string id, string hash, Guid messageId, string addressKey,
        ProviderEmailEventKind kind, DateTime occurred, DateTime received)
    {
        TransactionalEmailRules.RequireUtc(occurred); TransactionalEmailRules.RequireUtc(received);
        if (id.Length is < 1 or > 128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
            || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)) || messageId == Guid.Empty || !Enum.IsDefined(kind)
            || !Subscriptions.SubscriberAddress.TryCreate(addressKey, out var address) || address?.Key != addressKey
            || occurred > received.AddMinutes(5)) throw new ArgumentException("Invalid provider event.");
        return new() { EventId = id, PayloadHash = hash, MessageId = messageId, AddressKey = addressKey,
            Kind = kind, OccurredUtc = occurred, ReceivedUtc = received };
    }
    public override string ToString() => nameof(ProviderEmailEvent);
}

/// <summary>Independent observed facts for one message/address, not an ordered send state or proof of reading.</summary>
public sealed class ProviderEmailObservation
{
    private ProviderEmailObservation() { }
    public Guid MessageId { get; private set; }
    public string AddressKey { get; private set; } = string.Empty;
    public DateTime? DeliveredUtc { get; private set; }
    public DateTime? BouncedUtc { get; private set; }
    public DateTime? ComplainedUtc { get; private set; }
    public DateTime? TemporaryBounceUtc { get; private set; }
    public override string ToString() => nameof(ProviderEmailObservation);
}

/// <summary>Address-level provider suppression survives events that arrive before the first local registration.</summary>
public sealed class ProviderAddressSuppression
{
    private ProviderAddressSuppression() { }
    public string AddressKey { get; private set; } = string.Empty;
    public Subscriptions.SendingSuppression Flags { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public override string ToString() => nameof(ProviderAddressSuppression);
}

/// <summary>Receipt, distinct message facts and irreversible address suppression commit together.</summary>
public interface IProviderEmailEventStore
{
    Task<ProviderEventOutcome> ApplyAsync(ProviderEmailEvent message, CancellationToken cancellationToken);
}
