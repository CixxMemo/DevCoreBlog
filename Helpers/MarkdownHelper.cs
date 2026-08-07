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
//   @Html.Raw(DevCoreBlog.Helpers.MarkdownHelper.ToHtml(Model.Content))
//
// Security Note:
//   - Markdig does NOT sanitize HTML by default. If user input is untrusted,
//     you should add an HTML sanitizer (e.g., Ganss.XSS) before rendering.
//   - In this blog, only the admin writes content, so it is trusted input.
// =============================================================================

using Markdig;

namespace DevCoreBlog.Helpers;

// Static helper class — no instantiation needed, call MarkdownHelper.ToHtml() directly
public static class MarkdownHelper
{
    // -----------------------------------------------------------------------
    // MARKDOWN PIPELINE (built once, reused forever)
    // -----------------------------------------------------------------------
    // The pipeline defines which Markdown extensions are active.
    // 'static readonly' means it is created once when the class is first accessed
    // and then shared across all requests (thread-safe in Markdig).
    //
    // UseAdvancedExtensions() is a shortcut that enables the most popular extensions:
    //   - GitHub Flavored Markdown (GFM): tables, strikethrough, task lists
    //   - Auto-links: URLs become clickable automatically
    //   - Footnotes, abbreviations, definition lists
    //   - Custom containers, emojis, math (if configured)
    //   - Code blocks with language class (for Prism.js syntax highlighting)
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // -----------------------------------------------------------------------
    // PUBLIC METHOD: Convert Markdown string → HTML string
    // -----------------------------------------------------------------------
    // Takes raw Markdown text (from the database) and returns rendered HTML.
    // The returned HTML is safe to render with @Html.Raw() in Razor views.
    //
    // Parameters:
    //   markdown — The raw Markdown text (e.g., "# Hello\n\n```csharp\nvar x = 1;\n```")
    //
    // Returns:
    //   A string of HTML (e.g., "<h1>Hello</h1>\n<pre><code class=\"language-csharp\">...")
    //
    // If the input is null or empty, returns an empty string (no exception).
    public static string ToHtml(string markdown)
    {
        // Guard clause: if input is null or whitespace, return empty HTML
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // Markdig.Markdown.ToHtml() parses the Markdown and outputs HTML.
        // The pipeline adds language classes to code blocks (e.g., "language-csharp"),
        // which Prism.js uses to apply syntax highlighting colors.
        return Markdig.Markdown.ToHtml(markdown, Pipeline);
    }
}
