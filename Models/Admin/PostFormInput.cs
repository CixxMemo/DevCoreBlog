using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Models.Admin;

/// <summary>
/// Contains only editable post fields plus server-owned values needed to redraw the form.
/// </summary>
public sealed class PostFormInput
{
    [BindNever]
    public string? ReturnUrl { get; set; }

    public int Id { get; set; }

    public long EditVersion { get; set; }

    [Required(ErrorMessage = "Başlık gereklidir.")]
    [StringLength(
        PostContentRules.MaximumTitleLength,
        ErrorMessage = "Başlık en fazla {1} karakter olabilir.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "İçerik gereklidir.")]
    [StringLength(
        PostContentRules.MaximumContentLength,
        ErrorMessage = "İçerik en fazla {1} karakter olabilir.")]
    public string Content { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Etkin bir kategori seçin.")]
    public int CategoryId { get; set; }

    [StringLength(
        PostContentRules.MaximumSummaryLength,
        ErrorMessage = "Özet en fazla {1} karakter olabilir.")]
    public string? Summary { get; set; }

    [StringLength(
        PostContentRules.MaximumExcerptLength,
        ErrorMessage = "Kısa açıklama en fazla {1} karakter olabilir.")]
    public string? Excerpt { get; set; }

    [EnumDataType(typeof(PostSaveAction), ErrorMessage = "Geçerli bir kayıt eylemi seçin.")]
    public PostSaveAction SaveAction { get; set; } = PostSaveAction.SaveDraft;

    [BindNever]
    public PostPublicationState PublicationState { get; set; } = PostPublicationState.Draft;

    [BindNever]
    public bool IsPublished { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime PublishDate { get; set; }

    [BindNever]
    public string SiteTimeZoneId { get; set; } = string.Empty;

    [BindNever]
    public string Slug { get; set; } = string.Empty;

    [StringLength(PostContentRules.MaximumThumbnailAltLength,
        ErrorMessage = "Kapak açıklaması en fazla {1} karakter olabilir.")]
    public string? ThumbnailAlt { get; set; }

    [BindNever]
    public string ThumbnailUrl { get; set; } = string.Empty;
}
