using System.Text;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Core.Publishing;

/// <summary>One non-secret editorial preference record. Approved sender facts never override transport configuration.</summary>
public sealed class PublicationSettings
{
    public const string DefaultName = "Aether Letter";
    public const string DefaultCadence = "Haftalık kişisel deney bülteni; içerik hazır olduğunda.";
    private PublicationSettings() { }
    public int Id { get; private set; } = 1;
    public string Name { get; private set; } = string.Empty;
    public string Cadence { get; private set; } = string.Empty;
    public string? SenderEmail { get; private set; }
    public string? SenderName { get; private set; }
    public long Version { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static PublicationSettings Create(string name, string cadence, EmailSenderIdentity? sender, long version, DateTime now)
    {
        if (!ValidText(name, 80) || !ValidText(cadence, 200) || version is <= 0 or long.MaxValue
            || now.Kind != DateTimeKind.Utc || now == DateTime.MinValue || now == DateTime.MaxValue
            || (sender is not null && (!SubscriberAddress.TryCreate(sender.Email, out _) || !ValidSenderName(sender.Name))))
            throw new ArgumentException("Bounded publication preferences and a configured sender are required.");
        return new() { Name = name, Cadence = cadence, SenderEmail = sender?.Email, SenderName = sender?.Name,
            Version = version, UpdatedUtc = now.AddTicks(-(now.Ticks % 10)) };
    }
    public static bool ValidText(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value != value.Trim() || value.Any(c => char.IsControl(c) || c is '<' or '>')) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(value) <= limit * 4; }
        catch (EncoderFallbackException) { return false; }
    }
    public bool Approves(EmailSenderIdentity? sender) => sender is not null && SenderEmail == sender.Email && SenderName == sender.Name;
    private static bool ValidSenderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '\\')) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(value) <= 320; }
        catch (EncoderFallbackException) { return false; }
    }
    public override string ToString() => nameof(PublicationSettings);
}

public enum PublicationSaveStatus { Saved, Unchanged, Conflict, Invalid, SenderMismatch }
/// <summary>Singleton CAS storage; no credentials, arbitrary configuration keys or email submission operations.</summary>
public interface IPublicationSettingsStore
{
    Task<PublicationSettings?> ReadAsync(CancellationToken cancellationToken);
    Task<PublicationSaveStatus> SaveAsync(PublicationSettings proposed, long expectedVersion, CancellationToken cancellationToken);
}
