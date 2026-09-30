namespace DevCoreBlog.Configuration;

/// <summary>Validates origins without credentials, paths, queries or wildcard hosts.</summary>
internal static class HttpOrigin
{
    public static bool TryParse(string value, out Uri? origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) ||
            Uri.CheckHostName(parsed.Host) == UriHostNameType.Unknown ||
            parsed.UserInfo.Length != 0 || parsed.AbsolutePath != "/" ||
            parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
        {
            return false;
        }
        origin = parsed;
        return true;
    }
}
