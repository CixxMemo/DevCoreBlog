using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;

namespace DevCoreBlog.Models.Public;

/// <summary>Only reader filters and bounded pagination can be bound from a GET request.</summary>
public sealed class PublicListInput : IValidatableObject
{
    [StringLength(PublicListBounds.MaximumTermLength)]
    public string? Query { get; set; }
    [StringLength(PublicListBounds.MaximumCategoryLength)]
    public string? Category { get; set; }
    [Range(1, PublicListBounds.MaximumPage)]
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 9;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PublicListBounds.IsValidPageSize(PageSize))
            yield return new ValidationResult("Page size must be 9, 18, or 27.", [nameof(PageSize)]);
    }
}
