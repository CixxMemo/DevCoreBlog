using System.Security.Cryptography;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Purpose-isolated, authenticated encrypted proof; no address, identity or revision appears in cleartext links.</summary>
public sealed class SubscriberExitLinks
{
    public const string Path = "/abonelik/cik";
    public const int MaximumTokenLength = 512;
    private readonly IDataProtector protector;
    private readonly string origin;

    public SubscriberExitLinks(IDataProtectionProvider protection, SubscriberVerificationLinks trustedLinks)
    {
        protector = protection.CreateProtector("AetherNotes.SubscriptionExit.v1");
        origin = trustedLinks.Origin;
    }

    // Issuance is internal to mail composition. There is no public address-to-link endpoint.
    public string Create(SubscriberSnapshot subscriber)
    {
        if (subscriber.Id == Guid.Empty || subscriber.PeriodId == Guid.Empty || subscriber.Status == SubscriptionStatus.Unsubscribed)
            throw new ArgumentException("A current subscription period is required.");
        var payload = subscriber.Id.ToString("N") + ":" + subscriber.PeriodId.ToString("N") + ":" +
            SubscriberVerificationRules.AddressBinding(subscriber.AddressKey);
        return origin + Path + "#t=" + protector.Protect(payload);
    }

    public bool TryRead(string? token, out SubscriberExitProof? proof)
    {
        proof = null;
        if (token is null || token.Length is < 100 or > MaximumTokenLength || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            if (WebEncoders.Base64UrlEncode(bytes) != token) return false;
            var parts = protector.Unprotect(token).Split(':');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var id) || !Guid.TryParseExact(parts[1], "N", out var period)
                || parts[2].Length != 64 || parts[2].Any(c => !char.IsAsciiHexDigit(c))) return false;
            proof = new(id, period, parts[2]);
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }
}

/// <summary>Validates proof before database access; exit remains available regardless of provider or worker configuration.</summary>
public sealed class SubscriberUnsubscriptionService(SubscriberExitLinks links, ISubscriberUnsubscriptionStore store, TimeProvider clock)
{
    public async Task<UnsubscriptionOutcome> UnsubscribeAsync(string? token, CancellationToken cancellationToken)
    {
        if (!links.TryRead(token, out var proof) || proof is null) return UnsubscriptionOutcome.Invalid;
        return await store.UnsubscribeAsync(proof, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
