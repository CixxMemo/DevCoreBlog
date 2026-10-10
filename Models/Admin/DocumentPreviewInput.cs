using System.ComponentModel.DataAnnotations;

namespace DevCoreBlog.Models.Admin;

/// <summary>Only the unsaved document crosses the preview form boundary.</summary>
public sealed class DocumentPreviewInput
{
    [Required(ErrorMessage = "Önizleme için içerik belgesini girin.")]
    public string DocumentJson { get; set; } = "";
}
