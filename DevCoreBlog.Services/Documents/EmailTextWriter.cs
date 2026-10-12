using System.Globalization;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Plain-text alternative preserves authored text and adds usable media/link destinations without HTML parsing.</summary>
internal sealed class EmailTextWriter(EmailRenderContext context)
{
    internal string Render(DocumentNode root)
    {
        var body = Write(root);
        return context.Unsubscribe is { } exit ? body + "\n\nAbonelikten çık: " + exit.AbsoluteUri : body;
    }

    private string Write(DocumentNode node) => node.Kind switch
    {
        DocumentNodeKind.Doc or DocumentNodeKind.Blockquote or DocumentNodeKind.ListItem => Join(node, "\n\n"),
        DocumentNodeKind.Paragraph or DocumentNodeKind.Heading or DocumentNodeKind.CodeBlock => Join(node, ""),
        DocumentNodeKind.Text => Text(node),
        DocumentNodeKind.HardBreak => "\n",
        DocumentNodeKind.BulletList => List(node, null),
        DocumentNodeKind.OrderedList => List(node, Attributes<OrderedListAttributes>(node).Start),
        DocumentNodeKind.Image => Image(Attributes<ImageAttributes>(node)),
        DocumentNodeKind.YouTube => "YouTube videosunu izle: https://www.youtube.com/watch?v=" + Attributes<YouTubeAttributes>(node).VideoId,
        DocumentNodeKind.Table => Join(node, "\n"),
        DocumentNodeKind.TableRow => Join(node, " | "),
        DocumentNodeKind.TableCell or DocumentNodeKind.TableHeader => Join(node, "\n"),
        _ => throw new InvalidOperationException("Unsupported validated email node.")
    };

    private string Join(DocumentNode node, string separator) => string.Join(separator, node.Children.Select(Write));
    private string List(DocumentNode node, int? start) => string.Join('\n', node.Children.Select((child, index) =>
        (start is { } number ? (number + index).ToString(CultureInfo.InvariantCulture) + ". " : "- ")
        + Write(child).Replace("\n", "\n  ", StringComparison.Ordinal)));

    private string Text(DocumentNode node)
    {
        var text = node.Text ?? throw new InvalidOperationException("Validated text is missing.");
        foreach (var mark in node.Marks)
            if (mark.Kind == DocumentMarkKind.Link)
                text += " (" + context.Resolve((mark.Link ?? throw new InvalidOperationException("Validated link is missing.")).Href) + ")";
        return text;
    }

    private static string Image(ImageAttributes image) =>
        (string.IsNullOrEmpty(image.Alt) ? "Görsel" : image.Alt) + ": " + image.Source;
    private static T Attributes<T>(DocumentNode node) where T : DocumentAttributes =>
        node.Attributes as T ?? throw new InvalidOperationException("Validated attributes are missing.");
}
