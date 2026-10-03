using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Models.Admin;

/// <summary>Only bounded inventory filters bind from query strings.</summary>
public sealed class AdminPostListInput : IValidatableObject
{
    [StringLength(AdminPostQuery.MaximumQueryLength)]
    public string? Query { get; set; }
    [Range(1, int.MaxValue)]
    public int? CategoryId { get; set; }
    [EnumDataType(typeof(PostPublicationState))]
    public PostPublicationState? Status { get; set; }
    [Range(1, AdminPostQuery.MaximumPage)]
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!AdminPostQuery.PageSizes.Contains(PageSize))
            yield return new ValidationResult("Page size must be 10, 25, or 50.", [nameof(PageSize)]);
    }
    public AdminPostQuery ToQuery() => new(Query?.Trim(), CategoryId, Status, Page, PageSize);
    public object RouteValues(int? page = null) => new { query = Query?.Trim(), categoryId = CategoryId,
        status = Status, page = page ?? Page, pageSize = PageSize };
}

/// <summary>One page with explicit filter/count context; no tracked entities are rendered.</summary>
public sealed record AdminPostIndexModel(AdminPostListPage Result, AdminPostListInput Input, IReadOnlyList<Category> Categories)
{
    public int TotalPages => Math.Min(AdminPostQuery.MaximumPage, Math.Max(1, (int)Math.Ceiling((double)Result.TotalCount / Input.PageSize)));
}
