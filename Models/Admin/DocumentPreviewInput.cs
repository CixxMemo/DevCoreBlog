using System.ComponentModel.DataAnnotations;

namespace DevCoreBlog.Models.Admin;

/// <summary>Only the unsaved document and a display preference cross the preview boundary.</summary>
public sealed class DocumentPreviewInput
{
    [Required(ErrorMessage = "Önizleme için içerik belgesini girin.")]
    public string DocumentJson { get; set; } = "";
    // Presentation only; authorization and document validation never depend on this flag.
    public bool FromEditor { get; set; }
}
