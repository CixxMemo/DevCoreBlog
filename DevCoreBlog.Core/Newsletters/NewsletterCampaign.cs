using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Core.Newsletters;

public enum NewsletterCampaignState { Prepared = 0, Cancelled = 1 }

/// <summary>One frozen, recipient-free sending intent. Source publication and editing never mutate it.</summary>
public sealed class NewsletterCampaign
{
    public const int MaximumHtmlBytes = EmailSendRequest.MaximumHtmlBytes - 65_536;
    public const int MaximumTextBytes = EmailSendRequest.MaximumTextBytes - 16_384;
    public const int CurrentRenderVersion = 1;
    private NewsletterCampaign() { }
    public Guid Id { get; private set; }
    public Guid StartKey { get; private set; }
    public int PostId { get; private set; }
    public long SourceRevision { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string SourceSlug { get; private set; } = string.Empty;
    public int DocumentVersion { get; private set; }
    public string DocumentJson { get; private set; } = string.Empty;
    public string DocumentPlainText { get; private set; } = string.Empty;
    public int DocumentWordCount { get; private set; }
    public int DocumentReadingMinutes { get; private set; }
    public int RenderVersion { get; private set; }
    public string Html { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string WebVersionUrl { get; private set; } = string.Empty;
    public string SenderEmail { get; private set; } = string.Empty;
    public string SenderName { get; private set; } = string.Empty;
    public string? ReplyTo { get; private set; }
    public string ProviderContract { get; private set; } = string.Empty;
    public string SnapshotHash { get; private set; } = string.Empty;
    public NewsletterCampaignState State { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    /// <summary>Only a trusted validated renderer may supply HTML/text; no form binds to this factory.</summary>
    public static NewsletterCampaign Prepare(Guid startKey, CampaignSource source, EmailSenderIdentity sender,
        string providerContract, string webUrl, string html, string text, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sender);
        if (startKey == Guid.Empty || source.PostId <= 0 || source.Revision is <= 0 or long.MaxValue
            || source.DocumentVersion != 1 || source.DocumentJson is null || source.DocumentPlainText is null
            || source.DocumentWordCount is null or < 0 or > 200_000 || source.DocumentReadingMinutes is null
            || source.DocumentReadingMinutes != (source.DocumentWordCount + 199) / 200)
            throw new ArgumentException("A canonical newsletter snapshot is required.");
        RequireText(source.Subject, 300, 1200, true);
        RequireText(source.Slug, 200, 800, true);
        RequireText(source.DocumentJson, 1_048_576, 1_048_576);
        RequireText(source.DocumentPlainText, 1_048_576, 1_048_576, allowEmpty: true);
        RequireText(html, MaximumHtmlBytes, MaximumHtmlBytes);
        RequireText(text, MaximumTextBytes, MaximumTextBytes);
        RequireText(sender.Name, 80, 320, true);
        if (sender.Name.Any(c => c is '<' or '>' or '"' or '\\') || !SubscriberAddress.TryCreate(sender.Email, out _)
            || (sender.ReplyTo is not null && !SubscriberAddress.TryCreate(sender.ReplyTo, out _)))
            throw new ArgumentException("A valid sender identity is required.");
        if (string.IsNullOrWhiteSpace(providerContract) || providerContract.Length > 32 || providerContract.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '.' and not '-'))
            throw new ArgumentException("A bounded provider contract is required.");
        if (webUrl.Length > 2048 || webUrl.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !Uri.TryCreate(webUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.Query.Length != 0)
            throw new ArgumentException("A trusted HTTPS web version is required.");
        RequireUtc(utcNow);
        utcNow = utcNow.AddTicks(-(utcNow.Ticks % 10));
        var snapshot = new NewsletterCampaign
        {
            Id = Guid.NewGuid(), StartKey = startKey, PostId = source.PostId, SourceRevision = source.Revision,
            Subject = source.Subject, SourceSlug = source.Slug, DocumentVersion = 1, DocumentJson = source.DocumentJson,
            DocumentPlainText = source.DocumentPlainText, DocumentWordCount = source.DocumentWordCount.Value,
            DocumentReadingMinutes = source.DocumentReadingMinutes.Value, RenderVersion = CurrentRenderVersion,
            Html = html, Text = text, WebVersionUrl = webUrl, SenderEmail = sender.Email, SenderName = sender.Name,
            ReplyTo = sender.ReplyTo, ProviderContract = providerContract, CreatedUtc = utcNow, UpdatedUtc = utcNow, Version = 1
        };
        snapshot.SnapshotHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new object?[] {
            snapshot.PostId, snapshot.SourceRevision, snapshot.Subject, snapshot.SourceSlug, snapshot.DocumentVersion,
            snapshot.DocumentJson, snapshot.DocumentPlainText, snapshot.DocumentWordCount, snapshot.DocumentReadingMinutes,
            snapshot.RenderVersion, snapshot.Html, snapshot.Text, snapshot.WebVersionUrl, snapshot.SenderEmail,
            snapshot.SenderName, snapshot.ReplyTo, snapshot.ProviderContract })));
        return snapshot;
    }

    public CampaignIntent Intent() => new(Id, PostId, SourceRevision, State, Version, SnapshotHash);

    /// <summary>Only the mutable intent state changes; cancellation never rewrites the frozen payload.</summary>
    public CampaignCancelStatus Cancel(long expectedVersion, DateTime utcNow)
    {
        RequireUtc(utcNow);
        if (expectedVersion is <= 0 or long.MaxValue) return CampaignCancelStatus.Conflict;
        if (State == NewsletterCampaignState.Cancelled) return CampaignCancelStatus.Unchanged;
        if (Version != expectedVersion) return CampaignCancelStatus.Conflict;
        if (utcNow < UpdatedUtc) return CampaignCancelStatus.InvalidTime;
        State = NewsletterCampaignState.Cancelled;
        UpdatedUtc = utcNow.AddTicks(-(utcNow.Ticks % 10));
        Version++;
        return CampaignCancelStatus.Cancelled;
    }
    public override string ToString() => nameof(NewsletterCampaign);
    public static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value == DateTime.MinValue || value == DateTime.MaxValue)
            throw new ArgumentException("Finite UTC required.");
    }
    private static void RequireText(string value, int characters, int bytes, bool noControls = false, bool allowEmpty = false)
    {
        try
        {
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > characters || value.Contains('\0')
                || (noControls && value.Any(char.IsControl)) || new UTF8Encoding(false, true).GetByteCount(value) > bytes)
                throw new ArgumentException("Invalid snapshot field.");
        }
        catch (EncoderFallbackException) { throw new ArgumentException("Invalid snapshot Unicode."); }
    }
}
