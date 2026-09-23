using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Models.Admin;

/// <summary>
/// Restricts category forms to the fields an administrator may submit.
/// </summary>
public sealed class CategoryFormInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(
        CategoryContentRules.MaximumNameLength,
        ErrorMessage = "Category name cannot exceed {1} characters.")]
    public string Name { get; set; } = string.Empty;

    [BindNever]
    public string Slug { get; set; } = string.Empty;
}
