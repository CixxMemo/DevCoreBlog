namespace DevCoreBlog.Core.ReadModels;

/// <summary>Only the canonical slug and real editorial dates needed by sitemap output.</summary>
public sealed record SitemapPost(string Slug, DateTime PublishDate, DateTime? UpdatedDate)
{
    // A draft edit before publication must not backdate the published page.
    public DateTime LastModifiedUtc => UpdatedDate is { } updated && updated > PublishDate
        ? updated : PublishDate;
}
