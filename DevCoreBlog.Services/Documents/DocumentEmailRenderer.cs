using System.Text;

namespace DevCoreBlog.Services.Documents;

/// <summary>Derives independent email HTML/text from one trusted snapshot without I/O or publication side effects.</summary>
public sealed class DocumentEmailRenderer(DocumentTextProducer textProducer)
{
    public const int HtmlUtf8Bytes = 8 * 1024 * 1024;
    public const int PlainTextUtf8Bytes = 4 * 1024 * 1024;

    public RenderedEmailDocument Render(ValidatedContentDocument document, EmailRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        var reading = textProducer.Produce(document);
        var html = new EmailHtmlWriter(context, reading).Render(document.Document.Root);
        var text = new EmailTextWriter(context).Render(document.Document.Root);
        if (Encoding.UTF8.GetByteCount(html) > HtmlUtf8Bytes || Encoding.UTF8.GetByteCount(text) > PlainTextUtf8Bytes)
            throw new InvalidOperationException("Email output exceeds its resource budget.");
        return new(html, text, reading);
    }
}
