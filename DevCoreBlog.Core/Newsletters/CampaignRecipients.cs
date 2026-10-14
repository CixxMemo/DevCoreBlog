using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Core.Newsletters;

public enum RecipientSelectionState { Building = 0, Complete = 1 }
public enum RecipientBatchStatus { Advanced, Complete, Cancelled, NotFound, InvalidTime, Unavailable, Invalid }

/// <summary>Durable bounded keyset progress; a completed selection never admits later subscribers.</summary>
public sealed class NewsletterRecipientSelection
{
    public const int BatchSize = 250;
    private NewsletterRecipientSelection() { }
    public Guid CampaignId { get; private set; }
    public DateTime CutoffUtc { get; private set; }
    public Guid? UpperSubscriberId { get; private set; }
    public Guid? LastSubscriberId { get; private set; }
    public long ScannedCount { get; private set; }
    public long SelectedCount { get; private set; }
    public RecipientSelectionState State { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public static NewsletterRecipientSelection Begin(Guid campaignId, Guid? upper, DateTime now)
    {
        NewsletterCampaign.RequireUtc(now);
        if (campaignId == Guid.Empty || upper == Guid.Empty) throw new ArgumentException("Valid selection identities required.");
        now = ReaderIdentityRules.Utc(now);
        return new() { CampaignId = campaignId, UpperSubscriberId = upper, CutoffUtc = now, UpdatedUtc = now, Version = 1 };
    }

    /// <summary>Cursor, inserted row count and completion advance together; storage commits this with the recipients.</summary>
    public void Advance(int scanned, int selected, Guid? last, DateTime now)
    {
        NewsletterCampaign.RequireUtc(now);
        now = ReaderIdentityRules.Utc(now);
        if (State != RecipientSelectionState.Building || now < UpdatedUtc || scanned is < 0 or > BatchSize
            || selected < 0 || selected > scanned || (scanned > 0 && (last is null || last == Guid.Empty || last == LastSubscriberId)))
            throw new ArgumentException("Invalid selection progress.");
        ScannedCount = checked(ScannedCount + scanned);
        SelectedCount = checked(SelectedCount + selected);
        Version = checked(Version + 1);
        if (scanned > 0) LastSubscriberId = last;
        if (scanned < BatchSize || LastSubscriberId == UpperSubscriberId) State = RecipientSelectionState.Complete;
        UpdatedUtc = now;
    }

    public RecipientSelectionProgress Progress() => new(CampaignId, CutoffUtc, UpperSubscriberId,
        LastSubscriberId, ScannedCount, SelectedCount, State, UpdatedUtc, Version);
}

/// <summary>Immutable selected address and lifecycle binding, never evidence that email was sent.</summary>
public sealed class NewsletterRecipient
{
    private NewsletterRecipient() { }
    public Guid CampaignId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public long SelectionVersion { get; private set; }
    public Guid PeriodId { get; private set; }
    public string EmailAddress { get; private set; } = string.Empty;
    public string AddressKey { get; private set; } = string.Empty;
    public DateTime SelectedUtc { get; private set; }

    /// <summary>Freezes eligibility observed before the cutoff; sending must later recheck current lifecycle facts.</summary>
    public static NewsletterRecipient Select(Guid campaignId, SubscriberSnapshot subscriber, DateTime cutoff, DateTime now, long selectionVersion)
    {
        NewsletterCampaign.RequireUtc(cutoff); NewsletterCampaign.RequireUtc(now);
        if (campaignId == Guid.Empty || subscriber.Id == Guid.Empty || subscriber.PeriodId == Guid.Empty || subscriber.Version <= 0 || selectionVersion < 2
            || now < cutoff || subscriber.CreatedUtc > cutoff || subscriber.UpdatedUtc > cutoff
            || !SubscriberSendingRules.IsEligible(subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression)
            || !SubscriberAddress.TryCreate(subscriber.EmailAddress, out var address) || address?.Key != subscriber.AddressKey)
            throw new ArgumentException("Recipient is not eligible for this selection.");
        return new() { CampaignId = campaignId, SubscriberId = subscriber.Id, SubscriberVersion = subscriber.Version,
            PeriodId = subscriber.PeriodId, SelectionVersion = selectionVersion, EmailAddress = subscriber.EmailAddress, AddressKey = subscriber.AddressKey,
            SelectedUtc = ReaderIdentityRules.Utc(now) };
    }
    public override string ToString() => nameof(NewsletterRecipient);
}

public sealed record RecipientSelectionProgress(Guid CampaignId, DateTime CutoffUtc, Guid? UpperSubscriberId,
    Guid? LastSubscriberId, long ScannedCount, long SelectedCount, RecipientSelectionState State, DateTime UpdatedUtc, long Version);
public sealed record RecipientBatchResult(RecipientBatchStatus Status, RecipientSelectionProgress? Progress = null,
    int Scanned = 0, int Added = 0);

/// <summary>One atomic batch per call; no transport, recipient export or caller-controlled cursor.</summary>
public interface ICampaignRecipientStore
{
    Task<RecipientBatchResult> AdvanceAsync(Guid campaignId, DateTime now, CancellationToken cancellationToken);
    Task<RecipientSelectionProgress?> ReadAsync(Guid campaignId, CancellationToken cancellationToken);
}
