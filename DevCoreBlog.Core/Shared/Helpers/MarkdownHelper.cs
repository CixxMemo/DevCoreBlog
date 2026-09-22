using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace DevCoreBlog.Core.Shared.Helpers;

/// <summary>
/// Converts the blog's supported Markdown subset into HTML at the public render boundary.
/// </summary>
public static class MarkdownHelper
{
    private static readonly MarkdownPipeline SafePipeline = CreatePipeline();

    /// <summary>
    /// Renders Markdown with raw HTML disabled and all link targets validated.
    /// </summary>
    public static string ToSafeHtml(string? markdown)
    {
        return string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : Markdown.ToHtml(markdown, SafePipeline);
    }

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UsePipeTables()
            .UseTaskLists();

        builder.Extensions.Add(new SafeLinkRenderingExtension());
        return builder.Build();
    }
}

/// <summary>
/// Replaces Markdig's default link renderer with the blog's URL and video policy.
/// </summary>
internal sealed class SafeLinkRenderingExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            htmlRenderer.ObjectRenderers.ReplaceOrAdd<LinkInlineRenderer>(
                new SafeLinkInlineRenderer());
        }
    }
}

/// <summary>
/// Renders validated links and creates video embeds only from approved YouTube URLs.
/// </summary>
internal sealed class SafeLinkInlineRenderer : LinkInlineRenderer
{
    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;

        if (IsVideoLink(link))
        {
            if (YouTubeVideoUrl.TryGetVideoId(url, out var videoId))
            {
                WriteVideo(renderer, videoId);
            }
            else
            {
                renderer.WriteChildren(link);
            }

            return;
        }

        if (!MarkdownUrlPolicy.IsSafe(url))
        {
            renderer.WriteChildren(link);
            return;
        }

        // Render exactly the value that passed validation, including reference links.
        link.Url = url!;
        link.GetDynamicUrl = null;
        base.Write(renderer, link);
    }

    private static bool IsVideoLink(LinkInline link)
    {
        return !link.IsImage
            && link.FirstChild is LiteralInline label
            && label.NextSibling is null
            && string.Equals(label.Content.ToString(), "video", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteVideo(HtmlRenderer renderer, string videoId)
    {
        renderer.Write("<span class=\"my-6 block aspect-video w-full overflow-hidden border-2 border-black bg-black\">");
        renderer.Write("<iframe class=\"h-full w-full\" src=\"https://www.youtube-nocookie.com/embed/");
        renderer.WriteEscapeUrl(videoId);
        renderer.Write("\" title=\"YouTube video player\" loading=\"lazy\" referrerpolicy=\"strict-origin-when-cross-origin\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe>");
        renderer.Write("</span>");
    }
}

/// <summary>
/// Allows HTTPS and local relative targets while rejecting executable or ambiguous schemes.
/// </summary>
internal static class MarkdownUrlPolicy
{
    private const int MaximumUrlLength = 2_048;
    private const int MaximumDecodingPasses = 4;

    public static bool IsSafe(string? url)
    {
        if (!TryNormalizeForInspection(url, out var normalized))
        {
            return false;
        }

        if (normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Contains('\\'))
        {
            return false;
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.StartsWith("#", StringComparison.Ordinal)
            || normalized.StartsWith("?", StringComparison.Ordinal))
        {
            return true;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            return string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(absoluteUri.Host);
        }

        var colonIndex = normalized.IndexOf(':');
        var pathDelimiterIndex = normalized.IndexOfAny(['/', '?', '#']);
        if (colonIndex >= 0 && (pathDelimiterIndex < 0 || colonIndex < pathDelimiterIndex))
        {
            return false;
        }

        return Uri.TryCreate(normalized, UriKind.Relative, out _);
    }

    private static bool TryNormalizeForInspection(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaximumUrlLength)
        {
            return false;
        }

        var decoded = WebUtility.HtmlDecode(url).Trim();
        try
        {
            for (var pass = 0; pass < MaximumDecodingPasses; pass++)
            {
                var next = WebUtility.HtmlDecode(Uri.UnescapeDataString(decoded));
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        var compact = new StringBuilder(decoded.Length);
        foreach (var character in decoded)
        {
            if (!char.IsControl(character) && !char.IsWhiteSpace(character))
            {
                compact.Append(character);
            }
        }

        normalized = compact.ToString();
        return normalized.Length > 0;
    }
}

/// <summary>
/// Extracts a strict 11-character video ID from approved HTTPS YouTube URL shapes.
/// </summary>
internal static class YouTubeVideoUrl
{
    private static readonly HashSet<string> FullYouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com"
    };

    public static bool TryGetVideoId(string? url, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(WebUtility.HtmlDecode(url.Trim()), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string? candidate;
        try
        {
            candidate = null;
            if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                var pathSegments = GetPathSegments(uri);
                if (pathSegments.Length == 1)
                {
                    candidate = pathSegments[0];
                }
            }
            else if (FullYouTubeHosts.Contains(uri.Host))
            {
                var pathSegments = GetPathSegments(uri);
                if (pathSegments.Length == 1
                    && string.Equals(pathSegments[0], "watch", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = GetQueryValue(uri.Query, "v");
                }
                else if (pathSegments.Length == 2
                         && string.Equals(pathSegments[0], "embed", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = pathSegments[1];
                }
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (!IsValidVideoId(candidate))
        {
            return false;
        }

        videoId = candidate!;
        return true;
    }

    private static string[] GetPathSegments(Uri uri)
    {
        return uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
    }

    private static string? GetQueryValue(string query, string expectedName)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return parts.Length == 2
                    ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                    : string.Empty;
            }
        }

        return null;
    }

    private static bool IsValidVideoId(string? videoId)
    {
        return videoId is { Length: 11 }
            && videoId.All(character =>
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_' or '-');
    }
}
