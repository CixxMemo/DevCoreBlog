using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Validates references without fetching URLs or trusting editor HTML.</summary>
internal static class DocumentUrlPolicy
{
    internal static string Read(string value, string path, bool httpsOnly)
    {
        DocumentGuard.Limit(DocumentJsonFields.Runes(value) <= DocumentLimits.UrlRunes, "attribute.urlLength", path);
        DocumentGuard.Require(value.Length > 0 && !UnsafeCharacters(value) && ValidEscapes(value), "url.characters", path);
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            DocumentGuard.Require(Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0,
                "url.https", path);
            return value;
        }
        var relative = value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
            || value.StartsWith('#') && value.Length > 1;
        DocumentGuard.Require(!httpsOnly && relative && Uri.TryCreate(value, UriKind.Relative, out _), "url.scheme", path);
        return value;
    }

    internal static string YouTubeId(string value, string path)
    {
        Read(value, path, true);
        var uri = new Uri(value, UriKind.Absolute);
        DocumentGuard.Require(uri.IsDefaultPort && uri.Fragment.Length == 0, "youtube.url", path);
        string? id = null;
        if (uri.Host == "youtu.be" && uri.Query.Length == 0) id = uri.AbsolutePath[1..];
        else if (uri.Host is "www.youtube.com" or "youtube.com")
        {
            if (uri.AbsolutePath == "/watch" && uri.Query.StartsWith("?v=", StringComparison.Ordinal)) id = uri.Query[3..];
            else if (uri.Query.Length == 0 && uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal)) id = uri.AbsolutePath[7..];
        }
        else if (uri.Host == "www.youtube-nocookie.com" && uri.Query.Length == 0 && uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal))
            id = uri.AbsolutePath[7..];
        DocumentGuard.Require(id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "youtube.id", path);
        // URI normalization must not broaden the accepted input grammar (escapes, dot segments, ports).
        DocumentGuard.Require(value == "https://youtu.be/" + id
            || value == "https://www.youtube.com/watch?v=" + id || value == "https://youtube.com/watch?v=" + id
            || value == "https://www.youtube.com/embed/" + id || value == "https://youtube.com/embed/" + id
            || value == "https://www.youtube-nocookie.com/embed/" + id, "youtube.url", path);
        return id ?? throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "youtube.id", path));
    }

    private static bool UnsafeCharacters(string value) => value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '\\' or '<' or '>' or '"' or '\'');

    private static bool ValidEscapes(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length || !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2])) return false;
            var decoded = Convert.ToByte(value.Substring(i + 1, 2), 16);
            if (decoded <= 32 || decoded == 127 || decoded is 92 or 37) return false;
            // Encoded separators cannot turn a local reference into a network-path URL.
            if (value.StartsWith('/') && decoded is 47 or 58) return false;
            i += 2;
        }
        return true;
    }
}
