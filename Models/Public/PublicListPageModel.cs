using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Core.Validation;

namespace DevCoreBlog.Models.Public;

/// <summary>One reader result page with its explicit filter context and active category choices.</summary>
public sealed record PublicListPageModel(PublicPostPage Result, PublicPagingModel Paging,
    string? CategoryName, IReadOnlyList<Category> Categories);

/// <summary>Native navigation preserves only validated filters.</summary>
public sealed record PublicPagingModel(string Action, string? Slug, string? Query, string? Category,
    int Page, int PageSize, int TotalCount)
{
    public int ActualTotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
    public int TotalPages => Math.Min(PublicListBounds.MaximumPage, ActualTotalPages);
}
