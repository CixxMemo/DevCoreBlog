using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Services.Email;

/// <summary>Webhook credentials are separate from sending credentials; invalid configuration closes only ingress.</summary>
public sealed class ResendWebhookOptions
{
    internal Svix.Webhook? Verifier { get; }
    internal HashSet<string> Senders { get; } = new(StringComparer.Ordinal);
    public bool IsConfigured { get; }
    public const int MaximumBodyBytes = 65_536;

    public ResendWebhookOptions(bool enabled, string? secret, string? allowedSenders)
    {
        if (!enabled || secret is null || secret.Length is < 20 or > 128 || !secret.StartsWith("whsec_", StringComparison.Ordinal)
            || secret.Any(char.IsWhiteSpace) || allowedSenders is null) return;
        try
        {
            var key = Convert.FromBase64String(secret[6..]);
            if (key.Length is < 16 or > 64) return;
            var values = allowedSenders.Split(',');
            if (values.Length is < 1 or > 4) return;
            foreach (var value in values)
            {
                if (!SubscriberAddress.TryCreate(value.Trim(), out var sender) || sender is null) return;
                Senders.Add(sender.Key);
            }
            Verifier = new Svix.Webhook(secret);
            IsConfigured = true;
        }
        catch (FormatException) { }
    }
    public override string ToString() => nameof(ResendWebhookOptions);
}
