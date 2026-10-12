using System.Security.Cryptography;
using System.Text;
using DevCoreBlog.Configuration;

namespace DevCoreBlog.Security;

/// <summary>Both webhook versions authenticate before body reads; cookies never grant ingress access.</summary>
internal static class WebhookRequestAuthentication
{
    internal static bool HasValidSecret(HttpRequest request, WebhookIngressOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret) ||
            !request.Headers.TryGetValue("X-DevCore-Secret", out var header) || header.Count != 1 ||
            string.IsNullOrWhiteSpace(header[0])) return false;
        var provided = Encoding.UTF8.GetBytes(header[0] ?? string.Empty);
        var expected = Encoding.UTF8.GetBytes(options.Secret);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    internal static bool TryReadKey(HttpRequest request, out string? key, bool required = false)
    {
        key = null;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var header)) return !required;
        if (header.Count != 1 || header[0] is not { Length: >= 1 and <= 128 } value ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')) return false;
        key = value;
        return true;
    }
}
