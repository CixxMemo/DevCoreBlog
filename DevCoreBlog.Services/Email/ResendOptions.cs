using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Services.Email;

/// <summary>Immutable environment configuration; missing or malformed fields disable only email submission.</summary>
public sealed class ResendOptions : DevCoreBlog.Core.Email.IEmailSubmissionContext
{
    internal string? ApiKey { get; }
    internal string? From { get; }
    internal string? ReplyTo { get; }
    public bool IsConfigured { get; }
    public string? Binding { get; }

    public ResendOptions(string? apiKey, string? fromEmail, string? fromName, string? replyTo = null)
    {
        if (apiKey is null || apiKey.Length is < 4 or > 256 || !apiKey.StartsWith("re_", StringComparison.Ordinal)
            || apiKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')) return;
        if (!SubscriberAddress.TryCreate(fromEmail, out var sender) || sender is null) return;
        if (string.IsNullOrWhiteSpace(fromName) || fromName.Length > 80
            || fromName.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '\\')) return;
        try { new System.Text.UTF8Encoding(false, true).GetByteCount(fromName); }
        catch (System.Text.EncoderFallbackException) { return; }
        if (replyTo == string.Empty) replyTo = null;
        if (replyTo is not null && !SubscriberAddress.TryCreate(replyTo, out _)) return;
        ApiKey = apiKey;
        From = fromName + " <" + sender.Address + ">";
        ReplyTo = replyTo;
        // Only the encrypted outbox envelope may persist this credential/configuration binding.
        // Rotating a key or sender must hold previously staged work, not silently change its wire payload.
        Binding = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new[] { "resend/v1", apiKey, From, ReplyTo }))));
        IsConfigured = true;
    }
}
