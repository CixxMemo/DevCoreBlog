namespace DevCoreBlog.Services.Interfaces;

/// <summary>Renders the supported Markdown subset at the trusted HTML boundary.</summary>
public interface ISafeMarkdownRenderer
{
    string ToSafeHtml(string? markdown);
}
