using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Models.Admin;

/// <summary>
/// Contains only editable post fields plus server-owned values needed to redraw the form.
/// </summary>
public sealed class PostFormInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(
        PostContentRules.MaximumTitleLength,
        ErrorMessage = "Title cannot exceed {1} characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Content is required.")]
    [StringLength(
        PostContentRules.MaximumContentLength,
        ErrorMessage = "Content cannot exceed {1} characters.")]
    public string Content { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Select an active category.")]
    public int CategoryId { get; set; }

    [StringLength(
        PostContentRules.MaximumSummaryLength,
        ErrorMessage = "Summary cannot exceed {1} characters.")]
    public string? Summary { get; set; }

    [StringLength(
        PostContentRules.MaximumExcerptLength,
        ErrorMessage = "Excerpt cannot exceed {1} characters.")]
    public string? Excerpt { get; set; }

    public bool IsPublished { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime PublishDate { get; set; } = DateTime.Now;

    [BindNever]
    public string Slug { get; set; } = string.Empty;

    [BindNever]
    public string ThumbnailUrl { get; set; } = string.Empty;
}
