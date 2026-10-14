namespace DevCoreBlog.Core.Newsletters;

/// <summary>Only owner-approved public teaser fields can cross this persistence boundary.</summary>
public interface IPublicNewsletterTeasers
{
    Task<PublicNewsletterTeaserPage> ReadPageAsync(int page, DateTime utcNow, CancellationToken cancellationToken);
}

public sealed record PublicNewsletterTeaser(string Title, string Summary);
public sealed record PublicNewsletterTeaserPage(IReadOnlyList<PublicNewsletterTeaser> Items, int TotalCount)
{
    public const int PageSize = 9;
    public const int MaximumPage = 1000;
    public const int SummaryRunes = 320;
}

/// <summary>Storage failure never becomes an empty successful public list.</summary>
public sealed class NewsletterTeaserStorageException : Exception { }
