using System.Security.Cryptography;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.AspNetCore.DataProtection;

namespace DevCoreBlog.Services.Email;

/// <summary>Protects token-bearing payloads with the existing persistent key ring and per-job purpose isolation.</summary>
public sealed class TransactionalEmailProtector(IDataProtectionProvider protection, IEmailSubmissionContext submission)
{
    private const int MaximumPlaintextBytes = 2_097_152;

    public PreparedTransactionalEmail Prepare(Guid id, SubscriberSnapshot subscriber, string subject, string html,
        string text, DateTime utcNow, DateTime expiresUtc, TransactionalEmailPurpose purpose = TransactionalEmailPurpose.Verification)
    {
        TransactionalEmailRules.RequireUtc(utcNow); TransactionalEmailRules.RequireUtc(expiresUtc);
        // PostgreSQL timestamps persist microseconds; the protected identity must use the same precision.
        utcNow = utcNow.AddTicks(-(utcNow.Ticks % 10));
        expiresUtc = expiresUtc.AddTicks(-(expiresUtc.Ticks % 10));
        if (!submission.IsConfigured || submission.Binding is not { Length: 64 } binding)
            throw new InvalidOperationException("Email submission is not configured.");
        if (!TransactionalEmailRules.CanSend(purpose, subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression))
            throw new ArgumentException("Subscription is not eligible for verification mail.");
        _ = new EmailSendRequest(subscriber.EmailAddress, subject, html, text, Key(id), new DateTimeOffset(utcNow));
        var payload = new Payload(1, id, subscriber.Id, subscriber.Version, subscriber.EmailAddress,
            subject, html, text, expiresUtc, binding, purpose);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length > MaximumPlaintextBytes) throw new ArgumentException("Email envelope exceeds its byte limit.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var cipher = Convert.ToBase64String(Protector(id, subscriber.Id, subscriber.Version).Protect(bytes));
        return new(id, subscriber.Id, subscriber.Version, cipher, hash, utcNow, expiresUtc, purpose);
    }

    internal DecodedTransactionalEmail? Read(TransactionalEmailLease lease)
    {
        try
        {
            if (lease.ProtectedPayload.Length > TransactionalEmailRules.MaximumProtectedBytes) return null;
            var bytes = Protector(lease.Id, lease.SubscriberId, lease.SubscriberVersion)
                .Unprotect(Convert.FromBase64String(lease.ProtectedPayload));
            if (bytes.Length > MaximumPlaintextBytes || Convert.ToHexString(SHA256.HashData(bytes)) != lease.PayloadHash) return null;
            var payload = JsonSerializer.Deserialize<Payload>(bytes, new JsonSerializerOptions { MaxDepth = 8 });
            if (payload is null || payload.Version != 1 || payload.Id != lease.Id || payload.SubscriberId != lease.SubscriberId
                || payload.SubscriberVersion != lease.SubscriberVersion || payload.ExpiresUtc != lease.ExpiresUtc
                || payload.Binding is not { Length: 64 } || payload.Purpose != lease.Purpose) return null;
            _ = new EmailSendRequest(payload.Recipient, payload.Subject, payload.Html, payload.Text, Key(lease.Id), DateTimeOffset.UnixEpoch);
            if (!SubscriberAddress.TryCreate(payload.Recipient, out var address) || address is null) return null;
            return new(payload.Recipient, address.Key, payload.Subject, payload.Html, payload.Text, payload.Binding);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException or ArgumentException)
        { return null; }
    }

    private IDataProtector Protector(Guid id, Guid subscriberId, long version) => protection.CreateProtector(
        "AetherNotes.TransactionalEmail.v1", id.ToString("N"), subscriberId.ToString("N"),
        version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    internal static string Key(Guid id) => "tx/" + id.ToString("N");
    private sealed record Payload(int Version, Guid Id, Guid SubscriberId, long SubscriberVersion,
        string Recipient, string Subject, string Html, string Text, DateTime ExpiresUtc, string Binding,
        TransactionalEmailPurpose Purpose = TransactionalEmailPurpose.Verification);
}

/// <summary>Decrypted content is scoped to one dispatch and never logged.</summary>
internal sealed record DecodedTransactionalEmail(string Recipient, string AddressKey, string Subject,
    string Html, string Text, string Binding)
{
    public override string ToString() => nameof(DecodedTransactionalEmail);
}
