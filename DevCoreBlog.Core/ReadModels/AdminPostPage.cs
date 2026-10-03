using DevCoreBlog.Core.Publishing;
namespace DevCoreBlog.Core.ReadModels;

/// <summary>Bounded inventory filters; no provider or HTTP types cross this boundary.</summary>
public sealed record AdminPostQuery(string? Query = null, int? CategoryId = null,
    PostPublicationState? Status = null, int Page = 1, int PageSize = 25)
{
    public const int MaximumPage = 1000;
    public const int MaximumQueryLength = 100;
    public static IReadOnlyList<int> PageSizes { get; } = Array.AsReadOnly(new[] { 10, 25, 50 });
    public void Validate()
    {
        if (Page is < 1 or > MaximumPage) throw new ArgumentOutOfRangeException(nameof(Page));
        if (!PageSizes.Contains(PageSize)) throw new ArgumentOutOfRangeException(nameof(PageSize));
        if (Query?.Length > MaximumQueryLength) throw new ArgumentException("Search term is too long.", nameof(Query));
        if (CategoryId is <= 0) throw new ArgumentOutOfRangeException(nameof(CategoryId));
        if (Status is { } state && !Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(Status));
    }
}

/// <summary>Only columns displayed in the inventory leave persistence.</summary>
public sealed record AdminPostSummary(int Id, string Title, string Slug, int CategoryId, string CategoryName,
    bool IsActive, bool CategoryIsActive, bool IsPublished, DateTime PublishDate, DateTime CreatedDate, int ViewCount);
public sealed record AdminPostPage(IReadOnlyList<AdminPostSummary> Posts, int TotalCount);
public sealed record AdminPostListItem(AdminPostSummary Post, PostPublicationState State);
public sealed record AdminPostListPage(IReadOnlyList<AdminPostListItem> Posts, int TotalCount);
