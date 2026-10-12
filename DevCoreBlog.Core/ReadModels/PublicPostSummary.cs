using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.ReadModels;

/// <summary>Only fields used by public cards; article bodies never leave a list query.</summary>
public sealed record PublicPostSummary(int Id, string Title, string Slug, string Summary,
    string ThumbnailUrl, string CategoryName, DateTime PublishDate, int ViewCount,
    PostContentKind ContentKind = PostContentKind.Unclassified, int? ReadingMinutes = null);

/// <summary>A bounded page and the database count before pagination.</summary>
public sealed record PublicPostPage(IReadOnlyList<PublicPostSummary> Posts, int TotalCount);
