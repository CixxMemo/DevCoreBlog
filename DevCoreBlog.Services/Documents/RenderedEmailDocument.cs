namespace DevCoreBlog.Services.Documents;

/// <summary>Email-safe body fragment and text alternative; no transport or recipient state.</summary>
public sealed class RenderedEmailDocument
{
    internal RenderedEmailDocument(string html, string plainText, DocumentReading reading)
    {
        Html = html;
        PlainText = plainText;
        Reading = reading;
    }

    public string Html { get; }
    public string PlainText { get; }
    public DocumentReading Reading { get; }
}
