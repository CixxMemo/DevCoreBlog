namespace DevCoreBlog.Configuration;

/// <summary>Supplies a trusted public origin independently of incoming Host headers.</summary>
public sealed class SiteUrlOptions
{
    public string Origin { get; }

    public SiteUrlOptions(string? value, bool isDevelopment)
    {
        var candidate = value;
        if (candidate is null && isDevelopment)
        {
            candidate = "http://localhost:5000";
        }
        if (candidate is null || !HttpOrigin.TryParse(candidate, out var parsed) || parsed is null ||
            (parsed.Scheme != Uri.UriSchemeHttps && (!isDevelopment || !parsed.IsLoopback)))
        {
            throw new InvalidOperationException(
                "SITE_URL must be an absolute HTTPS origin. Development also allows a loopback HTTP origin.");
        }
        Origin = parsed.GetLeftPart(UriPartial.Authority);
    }
}
