using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Core.Newsletters;

/// <summary>Private, published newsletter projections; callers must first validate a current reader session.</summary>
public interface INewsletterArchive
{
    Task<NewsletterArchivePage> ReadPageAsync(int page, DateTime utcNow, CancellationToken cancellationToken);
    Task<NewsletterArchiveDocument?> FindAsync(string slug, DateTime utcNow, CancellationToken cancellationToken);
}

public sealed record NewsletterArchiveItem(string Slug, string Title, string Summary, DateTime PublishedUtc);
public sealed record NewsletterArchivePage(IReadOnlyList<NewsletterArchiveItem> Items, int TotalCount)
{
    public const int PageSize = 9;
    public const int MaximumPage = 1000;
}
public sealed record NewsletterArchiveDocument(string Slug, string Title, DateTime PublishedUtc, StoredPostDocument Content);

/// <summary>Storage failures never become an empty archive or a successful missing document.</summary>
public sealed class NewsletterArchiveStorageException : Exception { }
