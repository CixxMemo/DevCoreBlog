using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Models.Public;

/// <summary>Typed presentation of one public page and its independent ranking.</summary>
public sealed record HomePageModel(
    IReadOnlyList<PublicPostSummary> Posts, IReadOnlyList<TopReadPost> MostRead,
    int CurrentPage, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Min(DevCoreBlog.Core.Validation.PublicListBounds.MaximumPage, Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)));
}
