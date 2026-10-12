namespace DevCoreBlog.Services.Documents;

/// <summary>Validated delivery context supplied by the caller, never inferred from request Host or body text.</summary>
public sealed class EmailRenderContext
{
    private EmailRenderContext(Uri webVersion, Uri? unsubscribe)
    {
        WebVersion = webVersion;
        Unsubscribe = unsubscribe;
    }

    public Uri WebVersion { get; }
    public Uri? Unsubscribe { get; }

    /// <summary>Accepts bounded HTTPS URLs; an optional exit link must share the configured web origin.</summary>
    public static EmailRenderContext Create(string webVersionUrl, string? unsubscribeUrl = null)
    {
        var web = ReadUrl(webVersionUrl);
        var exit = unsubscribeUrl is null ? null : ReadUrl(unsubscribeUrl);
        if (exit is not null && (exit.Scheme != web.Scheme || exit.Host != web.Host || exit.Port != web.Port))
            throw new ArgumentException("Email exit URL must share the web origin.");
        return new(web, exit);
    }

    internal string Resolve(string reference) => new Uri(WebVersion, reference).AbsoluteUri;

    private static Uri ReadUrl(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            DocumentJsonInput.StrictUtf8.GetByteCount(value);
            DocumentUrlPolicy.Read(value, "$", true);
            return new Uri(value, UriKind.Absolute);
        }
        catch (Exception error) when (error is InvalidDocumentException or System.Text.EncoderFallbackException)
        {
            // Never echo a recipient-specific exit token or the rejected input.
            throw new ArgumentException("Invalid email context URL.");
        }
    }
}
