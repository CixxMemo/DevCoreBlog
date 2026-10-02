namespace DevCoreBlog.Services.Rendering;

/// <summary>Trusted renderer output and presentation facts from the same Markdown snapshot.</summary>
public sealed record RenderedMarkdown(string Html, IReadOnlyList<MarkdownHeading> Headings, int ReadingMinutes);

/// <summary>Generated target identifiers never contain user-supplied attributes.</summary>
public sealed record MarkdownHeading(string Id, string Title, int Level);
