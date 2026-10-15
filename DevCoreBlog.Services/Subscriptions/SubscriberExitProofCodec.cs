using System.Security.Cryptography;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Shared period/address encoding; separate Data Protection purposes keep human and one-click links non-interchangeable.</summary>
internal static class SubscriberExitProofCodec
{
    public static string Protect(IDataProtector protector, SubscriberExitProof proof)
    {
        if (proof.SubscriberId == Guid.Empty || proof.PeriodId == Guid.Empty || proof.AddressBinding.Length != 64
            || proof.AddressBinding.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Current exit binding required.");
        return protector.Protect(proof.SubscriberId.ToString("N") + ":" + proof.PeriodId.ToString("N") + ":" + proof.AddressBinding);
    }
    public static bool TryRead(IDataProtector protector, string? token, out SubscriberExitProof? proof)
    {
        proof = null;
        if (token is null || token.Length is < 100 or > SubscriberExitLinks.MaximumTokenLength
            || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            if (WebEncoders.Base64UrlEncode(bytes) != token) return false;
            var parts = protector.Unprotect(token).Split(':');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var id) || !Guid.TryParseExact(parts[1], "N", out var period)
                || id == Guid.Empty || period == Guid.Empty || parts[2].Length != 64 || parts[2].Any(c => !char.IsAsciiHexDigit(c))) return false;
            proof = new(id, period, parts[2]);return true;
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }
}
