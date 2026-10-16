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

    public long EditVersion { get; set; }

    [Required(ErrorMessage = "Kategori adı gereklidir.")]
    [StringLength(
        CategoryContentRules.MaximumNameLength,
        ErrorMessage = "Kategori adı en fazla {1} karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [BindNever]
    public string Slug { get; set; } = string.Empty;
}
