using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Rendering;
using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Services.Documents;

/// <summary>Renders an already visibility-filtered snapshot without a second read of private storage.</summary>
public sealed class PublicPostContentService(ContentDocumentValidator validator, DocumentTextProducer textProducer,
    DocumentWebRenderer documents, ISafeMarkdownRenderer markdown, TimeProvider clock)
{
    public PublicPostContent? Render(Post post)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (!PostPublication.IsVisibleAt(post, clock.GetUtcNow().UtcDateTime)) return null;
        var read = StoredDocumentReader.Read(new StoredPostDocument(post.EditVersion, post.DocumentVersion,
            post.DocumentJson, post.DocumentPlainText, post.DocumentWordCount, post.DocumentReadingMinutes), validator, textProducer);
        if (read.Status == PostDocumentReadStatus.Legacy) return new(markdown.RenderDocument(post.Content));
        return read.Document is { } document && read.Status == PostDocumentReadStatus.Ready
            ? new PublicPostContent(documents.Render(document)) : null;
    }
}

/// <summary>Only trusted renderer capabilities reach Razor; reading/navigation follow the same output.</summary>
public sealed class PublicPostContent
{
    internal PublicPostContent(RenderedMarkdown markdown)
    {
        Markdown = markdown; Headings = markdown.Headings; ReadingMinutes = markdown.ReadingMinutes;
    }
    internal PublicPostContent(RenderedContentDocument document)
    {
        Document = document;
        Headings = Array.AsReadOnly(document.Reading.Headings.Select(h => new MarkdownHeading(h.Id, h.Title, h.Level)).ToArray());
        ReadingMinutes = document.Reading.ReadingMinutes;
    }
    public RenderedMarkdown? Markdown { get; }
    public RenderedContentDocument? Document { get; }
    public IReadOnlyList<MarkdownHeading> Headings { get; }
    public int ReadingMinutes { get; }
}
