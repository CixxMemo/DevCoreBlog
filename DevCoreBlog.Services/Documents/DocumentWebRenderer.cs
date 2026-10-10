using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Maps the closed validated schema to semantic HTML without accepting raw markup.</summary>
public sealed class DocumentWebRenderer(DocumentTextProducer textProducer)
{
    public RenderedContentDocument Render(ValidatedContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var reading = textProducer.Produce(document);
        var writer = new WebWriter(reading);
        writer.Write(document.Document.Root);
        return new(writer.ToString(), reading);
    }

    /// <summary>All traversal state is request-local; every authored value is encoded.</summary>
    private sealed class WebWriter(DocumentReading reading)
    {
        private readonly StringBuilder html = new();
        private int headingIndex;

        internal void Write(DocumentNode node)
        {
            switch (node.Kind)
            {
                case DocumentNodeKind.Doc: Children(node); break;
                case DocumentNodeKind.Text: Text(node); break;
                case DocumentNodeKind.HardBreak: html.Append("<br>"); break;
                case DocumentNodeKind.Paragraph:
                    Aligned("p", Attributes<ParagraphAttributes>(node).Alignment, node); break;
                case DocumentNodeKind.Heading: Heading(node); break;
                case DocumentNodeKind.Blockquote: Element("blockquote", node); break;
                case DocumentNodeKind.BulletList: Element("ul", node); break;
                case DocumentNodeKind.OrderedList:
                    html.Append("<ol start=\"").Append(Number(Attributes<OrderedListAttributes>(node).Start)).Append("\">");
                    Children(node); html.Append("</ol>"); break;
                case DocumentNodeKind.ListItem: Element("li", node); break;
                case DocumentNodeKind.CodeBlock: Code(node); break;
                case DocumentNodeKind.Image: Image(Attributes<ImageAttributes>(node)); break;
                case DocumentNodeKind.YouTube: Video(Attributes<YouTubeAttributes>(node)); break;
                case DocumentNodeKind.Table:
                    html.Append("<div class=\"document-table-scroll\" tabindex=\"0\" role=\"region\" aria-label=\"Yazı tablosu\"><table><tbody>");
                    Children(node); html.Append("</tbody></table></div>"); break;
                case DocumentNodeKind.TableRow: Element("tr", node); break;
                case DocumentNodeKind.TableCell:
                    Aligned("td", Attributes<TableCellAttributes>(node).Alignment, node); break;
                case DocumentNodeKind.TableHeader:
                    Aligned("th", Attributes<TableCellAttributes>(node).Alignment, node); break;
                default: throw new InvalidOperationException("Unsupported validated document node.");
            }
        }

        private void Children(DocumentNode node)
        {
            foreach (var child in node.Children) Write(child);
        }

        private void Element(string tag, DocumentNode node)
        {
            html.Append('<').Append(tag).Append('>');
            Children(node);
            html.Append("</").Append(tag).Append('>');
        }

        private void Aligned(string tag, DocumentAlignment alignment, DocumentNode node)
        {
            html.Append('<').Append(tag).Append(" class=\"").Append(Alignment(alignment)).Append("\">");
            Children(node);
            html.Append("</").Append(tag).Append('>');
        }

        private void Heading(DocumentNode node)
        {
            var heading = reading.Headings[headingIndex++];
            var tag = "h" + Number(heading.Level);
            html.Append('<').Append(tag).Append(" id=\"");
            Encode(heading.Id);
            html.Append("\" class=\"").Append(Alignment(Attributes<HeadingAttributes>(node).Alignment)).Append("\">");
            Children(node);
            html.Append("</").Append(tag).Append('>');
        }

        private void Text(DocumentNode node)
        {
            foreach (var mark in node.Marks) OpenMark(mark);
            Encode(node.Text ?? throw new InvalidOperationException("Validated text is missing."));
            for (var i = node.Marks.Length - 1; i >= 0; i--)
                html.Append("</").Append(MarkTag(node.Marks[i].Kind)).Append('>');
        }

        private void OpenMark(DocumentMark mark)
        {
            var tag = MarkTag(mark.Kind);
            html.Append('<').Append(tag);
            if (mark.Kind == DocumentMarkKind.Link)
            {
                var link = mark.Link ?? throw new InvalidOperationException("Validated link is missing.");
                html.Append(" href=\""); Encode(link.Href); html.Append('"');
                OptionalTitle(link.Title);
                html.Append(link.Target == DocumentLinkTarget.NewWindow ? " target=\"_blank\"" : " target=\"_self\"");
                html.Append(link.NoFollow ? " rel=\"noopener noreferrer nofollow\"" : " rel=\"noopener noreferrer\"");
            }
            html.Append('>');
        }

        private void Code(DocumentNode node)
        {
            html.Append("<pre tabindex=\"0\" role=\"region\" aria-label=\"Kod bloğu\"><code class=\"")
                .Append(CodeClass(Attributes<CodeBlockAttributes>(node).Language)).Append("\">");
            Children(node); html.Append("</code></pre>");
        }

        private void Image(ImageAttributes image)
        {
            html.Append("<img src=\""); Encode(image.Source);
            html.Append("\" alt=\""); Encode(image.Alt ?? ""); html.Append('"');
            OptionalTitle(image.Title);
            html.Append(" loading=\"lazy\" decoding=\"async\">");
        }

        private void Video(YouTubeAttributes video)
        {
            html.Append("<div class=\"document-video\"><iframe src=\"https://www.youtube-nocookie.com/embed/");
            Encode(video.VideoId);
            html.Append("\" title=\"YouTube videosu\" loading=\"lazy\" referrerpolicy=\"no-referrer\" allow=\"fullscreen\" allowfullscreen></iframe></div>");
        }

        private void OptionalTitle(string? title)
        {
            if (title is null) return;
            html.Append(" title=\""); Encode(title); html.Append('"');
        }

        private void Encode(string text) => html.Append(HtmlEncoder.Default.Encode(text));
        private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
        private static T Attributes<T>(DocumentNode node) where T : DocumentAttributes =>
            node.Attributes as T ?? throw new InvalidOperationException("Validated attributes are missing.");

        private static string Alignment(DocumentAlignment alignment) => alignment switch
        {
            DocumentAlignment.Left => "document-align-left",
            DocumentAlignment.Center => "document-align-center",
            DocumentAlignment.Right => "document-align-right",
            _ => throw new InvalidOperationException("Unsupported validated alignment.")
        };

        private static string MarkTag(DocumentMarkKind kind) => kind switch
        {
            DocumentMarkKind.Bold => "strong", DocumentMarkKind.Italic => "em",
            DocumentMarkKind.Underline => "u", DocumentMarkKind.Code => "code",
            DocumentMarkKind.Link => "a",
            _ => throw new InvalidOperationException("Unsupported validated mark.")
        };

        // Explicit constants prevent language strings becoming arbitrary CSS classes.
        private static string CodeClass(string? language) => language switch
        {
            null or "plaintext" => "language-plaintext", "csharp" => "language-csharp",
            "javascript" => "language-javascript", "typescript" => "language-typescript",
            "json" => "language-json", "python" => "language-python", "bash" => "language-bash",
            "sql" => "language-sql", "html" => "language-html", "css" => "language-css",
            "markdown" => "language-markdown",
            _ => throw new InvalidOperationException("Unsupported validated code language.")
        };

        public override string ToString() => html.ToString();
    }
}
