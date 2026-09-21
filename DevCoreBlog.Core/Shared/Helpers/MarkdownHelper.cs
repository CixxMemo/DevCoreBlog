// =============================================================================
// MarkdownHelper.cs — Markdown to HTML Converter Utility
// =============================================================================
// This static helper class converts Markdown text into safe HTML using Markdig.
//
// What is Markdig?
//   - Markdig is a fast, extensible Markdown parser for .NET.
//   - It supports CommonMark, GitHub Flavored Markdown (tables, task lists, etc.),
//     code blocks with language tags, auto-links, and more.
//
// Why a Helper class?
//   - The conversion logic is a utility — not business logic (Service) or data
//     access (Repository). Helpers are the right place for pure functions.
//   - A static helper can be called from any Controller or View without DI.
//
// How the Pipeline works:
//   - MarkdownPipelineBuilder configures which Markdown extensions are enabled.
//   - UseAdvancedExtensions() enables: tables, strikethrough, auto-links,
//     task lists, footnotes, abbreviation, definition lists, and more.
//   - The pipeline is built ONCE (static readonly) and reused for every call.
//   - This is important for performance — building a pipeline is expensive.
//
// Usage in Views:
//   @Html.Raw(DevCoreBlog.Shared.Helpers.MarkdownHelper.ToHtml(Model.Content))
//
// Security Note:
//   - Markdig does NOT sanitize HTML by default. If user input is untrusted,
//     you should add an HTML sanitizer (e.g., Ganss.XSS) before rendering.
//   - In this blog, only the admin writes content, so it is trusted input.
// =============================================================================

using System.Text.RegularExpressions;
using Markdig;

namespace DevCoreBlog.Core.Shared.Helpers;

// Static helper class — converts Markdown to HTML and renders responsive video embeds
public static class MarkdownHelper
{
    // The pipeline defines which Markdown extensions are active.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // Regex to match [video](https://www.youtube.com/watch?v=VIDEO_ID) or [video](https://youtu.be/VIDEO_ID)
    private static readonly Regex YouTubeVideoTagRegex = new(
        @"\[video\]\((https?:\/\/(?:www\.)?(?:youtube\.com\/(?:watch\?v=|embed\/)|youtu\.be\/)([a-zA-Z0-9_\-]+)[^\)]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // -----------------------------------------------------------------------
    // PUBLIC METHOD: Convert Markdown string → HTML string
    // -----------------------------------------------------------------------
    public static string ToHtml(string markdown)
    {
        // Guard clause: if input is null or whitespace, return empty HTML
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // Pre-process video embed tags: [video](youtube_url) -> responsive brutalist iframe container
        string processedMarkdown = YouTubeVideoTagRegex.Replace(markdown, match =>
        {
            var videoId = match.Groups[2].Value;
            return $"\n\n<div class=\"my-6 aspect-video w-full border-2 border-black bg-black overflow-hidden\"><iframe class=\"w-full h-full\" src=\"https://www.youtube.com/embed/{videoId}\" title=\"YouTube video player\" frameborder=\"0\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" allowfullscreen></iframe></div>\n\n";
        });

        // Markdig.Markdown.ToHtml() parses the Markdown and outputs HTML.
        return Markdig.Markdown.ToHtml(processedMarkdown, Pipeline);
    }
}

