using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DevCoreBlog.Services.Rendering;

/// <summary>Counts displayed text and code, excluding link destinations and image/video metadata.</summary>
internal static class VisibleMarkdownText
{
    public static int ReadingMinutes(MarkdownDocument document)
    {
        var text = new StringBuilder();
        foreach (var block in document.Descendants<LeafBlock>())
        {
            text.Append(block is CodeBlock ? block.Lines.ToString() : FromInline(block.Inline));
            text.Append(' ');
        }
        var words = Regex.Matches(text.ToString(), @"[\p{L}\p{N}_]+", RegexOptions.None,
            TimeSpan.FromSeconds(1)).Count;
        return Math.Max(1, (int)Math.Ceiling(words / 200.0));
    }

    public static string FromInline(ContainerInline? container)
    {
        var text = new StringBuilder();
        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LinkInline link when link.IsImage:
                    break;
                case LinkInline link when link.FirstChild is LiteralInline label && label.NextSibling is null
                    && label.Content.ToString().Equals("video", StringComparison.OrdinalIgnoreCase)
                    && YouTubeVideoUrl.TryGetVideoId(link.Url, out _):
                    break;
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case HtmlEntityInline entity: text.Append(entity.Transcoded); break;
                case AutolinkInline auto: text.Append(auto.Url); break;
                case LineBreakInline: text.Append(' '); break;
                case ContainerInline nested: text.Append(FromInline(nested)); break;
            }
        }
        return text.ToString();
    }
}
