using System.Collections.Immutable;

namespace DevCoreBlog.Services.Documents;

/// <summary>Plain data derived from one validated snapshot; text still needs output encoding.</summary>
public sealed record DocumentReading(string PlainText, int WordCount, int ReadingMinutes,
    ImmutableArray<DocumentHeading> Headings);

/// <summary>Document-order headings with generated identifiers, never user-supplied attributes.</summary>
public sealed record DocumentHeading(string Id, string Title, int Level);
