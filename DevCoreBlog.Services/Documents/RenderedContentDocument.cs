namespace DevCoreBlog.Services.Documents;

/// <summary>Encoded web output and reading facts from one validated snapshot.</summary>
public sealed class RenderedContentDocument
{
    internal RenderedContentDocument(string html, DocumentReading reading)
    {
        Html = html;
        Reading = reading;
    }

    public string Html { get; }
    public DocumentReading Reading { get; }
}
