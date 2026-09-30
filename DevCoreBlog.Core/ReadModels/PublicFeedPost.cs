namespace DevCoreBlog.Core.ReadModels;

/// <summary>Contains only the persisted fields needed by the public portfolio feed.</summary>
public sealed record PublicFeedPost(
    int Id, string Title, string Slug, string Summary, string Excerpt,
    string CoverImageUrl, DateTime PublishDate, string CategoryName);
