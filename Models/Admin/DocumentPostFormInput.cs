using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Models.Admin;

/// <summary>HTTP allowlist for the JSON editor; display-only values cannot be posted.</summary>
public sealed class DocumentPostFormInput
{
    public int Id { get; set; }
    public long EditVersion { get; set; }
    [Required(ErrorMessage = "Başlık gereklidir.")]
    [StringLength(PostContentRules.MaximumTitleLength, ErrorMessage = "Başlık en çok 200 karakter olabilir.")]
    public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "İçerik belgesi gereklidir.")]
    public string DocumentJson { get; set; } = string.Empty;
    [Range(1, int.MaxValue, ErrorMessage = "Etkin bir kategori seçin.")]
    public int CategoryId { get; set; }
    [StringLength(PostContentRules.MaximumSummaryLength, ErrorMessage = "Özet en çok 500 karakter olabilir.")]
    public string? Summary { get; set; }
    [StringLength(PostContentRules.MaximumExcerptLength, ErrorMessage = "Kısa açıklama en çok 1000 karakter olabilir.")]
    public string? Excerpt { get; set; }
    [StringLength(PostContentRules.MaximumThumbnailAltLength, ErrorMessage = "Kapak açıklaması en çok 300 karakter olabilir.")]
    public string? ThumbnailAlt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime PublishDate { get; set; }
    [EnumDataType(typeof(PostSaveAction), ErrorMessage = "Geçerli bir kayıt eylemi seçin.")]
    public PostSaveAction SaveAction { get; set; } = PostSaveAction.SaveDraft;
    [BindNever] public string? ReturnUrl { get; set; }
    [BindNever] public string SiteTimeZoneId { get; set; } = string.Empty;
    [BindNever] public string? BlockingMessage { get; set; }
    [BindNever] public string Slug { get; set; } = string.Empty;
    [BindNever] public bool EditorEnabled { get; set; }
}
