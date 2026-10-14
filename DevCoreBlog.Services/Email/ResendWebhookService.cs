using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Services.Email;

public enum WebhookResponse { Accepted, InvalidSignature, InvalidPayload, Conflict, Unavailable }

/// <summary>Authenticates the original UTF-8 body before parsing; ignores tracking events without recording them.</summary>
public sealed class ResendWebhookService(ResendWebhookOptions options, IProviderEmailEventStore store, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, AllowDuplicateProperties = false };

    public async Task<WebhookResponse> ReceiveAsync(string body, string id, string timestamp, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.IsConfigured || options.Verifier is null) return WebhookResponse.Unavailable;
        if (id.Length is < 1 or > 128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
            || timestamp.Length is < 1 or > 12 || !timestamp.All(char.IsAsciiDigit) || signature.Length is < 1 or > 1024)
            return WebhookResponse.InvalidSignature;
        try
        {
            if (Encoding.UTF8.GetByteCount(body) > ResendWebhookOptions.MaximumBodyBytes) return WebhookResponse.InvalidPayload;
            var headers = new WebHeaderCollection { { "svix-id", id }, { "svix-timestamp", timestamp }, { "svix-signature", signature } };
            options.Verifier.Verify(body, headers); // Official library enforces HMAC and +/- five-minute attempt age.
        }
        catch (Exception) { return WebhookResponse.InvalidSignature; }
        ProviderEmailEvent? message;
        try
        {
            var payload = JsonSerializer.Deserialize<EventPayload>(body, JsonOptions);
            if (payload is null || payload.type is null || payload.type.Length > 64) return WebhookResponse.InvalidPayload;
            if (payload.type is not ("email.delivered" or "email.bounced" or "email.complained")) return WebhookResponse.Accepted;
            message = Parse(payload, body, id, clock.GetUtcNow().UtcDateTime);
            if (message is null) return WebhookResponse.InvalidPayload;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or FormatException) { return WebhookResponse.InvalidPayload; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            return await store.ApplyAsync(message, deadline.Token) switch
            {
                ProviderEventOutcome.Conflict => WebhookResponse.Conflict,
                ProviderEventOutcome.Unavailable => WebhookResponse.Unavailable,
                _ => WebhookResponse.Accepted
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return WebhookResponse.Unavailable; }
    }

    private ProviderEmailEvent? Parse(EventPayload payload, string raw, string id, DateTime now)
    {
        var data = payload.data;
        if (data is null || !Guid.TryParseExact(data.email_id, "D", out var messageId) || messageId == Guid.Empty
            || data.to?.Length != 1 || !SubscriberAddress.TryCreate(data.to[0], out var recipient) || recipient is null
            || data.from is null || data.from.Length > 400 || data.from.Any(char.IsControl)
            || !MailAddress.TryCreate(data.from, out var sender) || !SubscriberAddress.TryCreate(sender.Address, out var senderAddress)
            || senderAddress is null || !options.Senders.Contains(senderAddress.Key)
            || payload.created_at is null || payload.created_at.Length > 40
            || !DateTimeOffset.TryParseExact(payload.created_at,
                ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:sszzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var occurred)
            || occurred > new DateTimeOffset(now).AddMinutes(5)) return null;
        var kind = payload.type switch
        {
            "email.delivered" => ProviderEmailEventKind.Delivered,
            "email.complained" => ProviderEmailEventKind.Complained,
            _ => data.bounce?.type switch
            {
                "Permanent" => ProviderEmailEventKind.Bounced,
                "Temporary" => ProviderEmailEventKind.TemporaryBounce,
                _ => (ProviderEmailEventKind)(-1)
            }
        };
        if (!Enum.IsDefined(kind)) return null;
        return ProviderEmailEvent.Create(id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
            messageId, recipient.Key, kind, occurred.UtcDateTime, now);
    }

    // DTOs represent only relevant provider fields; future optional fields are bounded by the original body limit.
    private sealed class EventPayload
    {
        public string? type { get; set; }
        public string? created_at { get; set; }
        public EventData? data { get; set; }
    }
    private sealed class EventData
    {
        public string? email_id { get; set; }
        public string? from { get; set; }
        public string[]? to { get; set; }
        public BounceData? bounce { get; set; }
    }
    private sealed class BounceData { public string? type { get; set; } }
}
