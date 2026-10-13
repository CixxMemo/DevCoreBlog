using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Core.Newsletters;

/// <summary>Private bounded projection; canonical bodies and subscriber identities are excluded.</summary>
public interface INewsletterInventory
{
    Task<NewsletterPage> FindPageAsync(int page, CancellationToken cancellationToken);
}

public sealed record NewsletterSummary(int Id, string Title, string Summary, bool IsActive,
    bool CategoryActive, bool IsPublished, DateTime PublishDate, long EditVersion);
public sealed record NewsletterPage(IReadOnlyList<NewsletterSummary> Items, int TotalCount)
{
    public const int PageSize = 25;
    public const int MaximumPage = 1000;
}
public sealed record NewsletterListItem(NewsletterSummary Newsletter, PostPublicationState State);
public sealed record NewsletterListPage(IReadOnlyList<NewsletterListItem> Items, int TotalCount, int Page);
