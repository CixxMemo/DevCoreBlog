using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Publishing;

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
    [StringLength(10, ErrorMessage = "İçerik türü geçersiz.")]
    public string? Kind { get; set; }
    public PostContentKind? ContentKind => PublicContentFilter.TryParse(Kind, out var kind) ? kind : null;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PublicContentFilter.TryParse(Kind, out _))
            yield return new ValidationResult("Geçerli bir içerik türü seçin.", [nameof(Kind)]);
        if (!PublicListBounds.IsValidPageSize(PageSize))
            yield return new ValidationResult("Page size must be 9, 18, or 27.", [nameof(PageSize)]);
    }
}
