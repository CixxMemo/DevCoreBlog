using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Request-local email presentation; authored values are encoded, styles are closed constants.</summary>
internal sealed class EmailHtmlWriter(EmailRenderContext context, DocumentReading reading)
{
    private readonly StringBuilder html = new();
    private int headingIndex;

    internal string Render(DocumentNode root)
    {
        html.Append("<div style=\"").Append(EmailDocumentStyles.Body).Append("\">");
        Write(root);
        if (context.Unsubscribe is { } exit)
        {
            html.Append("<p><a href=\""); Encode(exit.AbsoluteUri);
            html.Append("\" rel=\"noopener noreferrer\">Abonelikten çık</a></p>");
        }
        html.Append("</div>");
        return html.ToString();
    }

    private void Write(DocumentNode node)
    {
        switch (node.Kind)
        {
            case DocumentNodeKind.Doc: Children(node); break;
            case DocumentNodeKind.Text: Text(node); break;
            case DocumentNodeKind.HardBreak: html.Append("<br>"); break;
            case DocumentNodeKind.Paragraph: Aligned("p", Attributes<ParagraphAttributes>(node).Alignment, node); break;
            case DocumentNodeKind.Heading:
                var heading = reading.Headings[headingIndex++];
                Aligned("h" + Number(heading.Level), Attributes<HeadingAttributes>(node).Alignment, node, heading.Id); break;
            case DocumentNodeKind.Blockquote: Element("blockquote", node); break;
            case DocumentNodeKind.BulletList: Element("ul", node); break;
            case DocumentNodeKind.OrderedList:
                html.Append("<ol start=\"").Append(Number(Attributes<OrderedListAttributes>(node).Start)).Append("\">");
                Children(node); html.Append("</ol>"); break;
            case DocumentNodeKind.ListItem: Element("li", node); break;
            case DocumentNodeKind.CodeBlock:
                html.Append("<pre style=\"").Append(EmailDocumentStyles.Code).Append("\"><code>");
                Children(node); html.Append("</code></pre>"); break;
            case DocumentNodeKind.Image: Image(Attributes<ImageAttributes>(node)); break;
            case DocumentNodeKind.YouTube: Video(Attributes<YouTubeAttributes>(node)); break;
            case DocumentNodeKind.Table:
                html.Append("<table style=\"").Append(EmailDocumentStyles.Table).Append("\"><tbody>");
                Children(node); html.Append("</tbody></table>"); break;
            case DocumentNodeKind.TableRow: Element("tr", node); break;
            case DocumentNodeKind.TableCell: Aligned("td", Attributes<TableCellAttributes>(node).Alignment, node); break;
            case DocumentNodeKind.TableHeader: Aligned("th", Attributes<TableCellAttributes>(node).Alignment, node); break;
            default: throw new InvalidOperationException("Unsupported validated email node.");
        }
    }

    private void Children(DocumentNode node) { foreach (var child in node.Children) Write(child); }
    private void Element(string tag, DocumentNode node)
    {
        html.Append('<').Append(tag).Append('>'); Children(node); html.Append("</").Append(tag).Append('>');
    }

    private void Aligned(string tag, DocumentAlignment alignment, DocumentNode node, string? id = null)
    {
        html.Append('<').Append(tag);
        if (id is not null) { html.Append(" id=\""); Encode(id); html.Append('"'); }
        html.Append(" style=\"").Append(EmailDocumentStyles.Alignment(alignment, tag is "td" or "th"));
        html.Append("\">"); Children(node); html.Append("</").Append(tag).Append('>');
    }

    private void Text(DocumentNode node)
    {
        foreach (var mark in node.Marks)
        {
            html.Append('<').Append(Tag(mark.Kind));
            if (mark.Kind == DocumentMarkKind.Link)
            {
                var link = mark.Link ?? throw new InvalidOperationException("Validated link is missing.");
                html.Append(" href=\""); Encode(context.Resolve(link.Href)); html.Append('"');
                Title(link.Title);
                html.Append(link.NoFollow ? " rel=\"noopener noreferrer nofollow\"" : " rel=\"noopener noreferrer\"");
            }
            html.Append('>');
        }
        Encode(node.Text ?? throw new InvalidOperationException("Validated text is missing."));
        for (var i = node.Marks.Length - 1; i >= 0; i--) html.Append("</").Append(Tag(node.Marks[i].Kind)).Append('>');
    }

    private void Image(ImageAttributes image)
    {
        html.Append("<img src=\""); Encode(image.Source); html.Append("\" alt=\""); Encode(image.Alt ?? "");
        html.Append('"'); Title(image.Title);
        html.Append(" style=\"").Append(EmailDocumentStyles.Image).Append("\">");
    }

    private void Video(YouTubeAttributes video)
    {
        html.Append("<p><a href=\"https://www.youtube.com/watch?v="); Encode(video.VideoId);
        html.Append("\" rel=\"noopener noreferrer\"><img src=\"https://i.ytimg.com/vi/"); Encode(video.VideoId);
        html.Append("/hqdefault.jpg\" alt=\"YouTube videosunu izle\" style=\"").Append(EmailDocumentStyles.Image).Append("\">YouTube videosunu izle</a></p>");
    }

    private void Title(string? title)
    {
        if (title is null) return;
        html.Append(" title=\""); Encode(title); html.Append('"');
    }
    private void Encode(string value) => html.Append(HtmlEncoder.Default.Encode(value));
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static T Attributes<T>(DocumentNode node) where T : DocumentAttributes =>
        node.Attributes as T ?? throw new InvalidOperationException("Validated attributes are missing.");
    private static string Tag(DocumentMarkKind kind) => kind switch
    {
        DocumentMarkKind.Bold => "strong", DocumentMarkKind.Italic => "em", DocumentMarkKind.Underline => "u",
        DocumentMarkKind.Code => "code", DocumentMarkKind.Link => "a",
        _ => throw new InvalidOperationException("Unsupported validated email mark.")
    };
}
