using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Services.Publishing;
namespace DevCoreBlog.Models.Admin;

/// <summary>Only editorial preferences, a CAS version and configured sender confirmation cross the form boundary.</summary>
public sealed class PublicationSettingsInput : IValidatableObject
{
    [Required(ErrorMessage = "Kayıt sürümü eksik. Güncel ayarları yeniden açın.")] public long? Version { get; set; }
    [Required(ErrorMessage = "Yayın adı gereklidir."), StringLength(80, ErrorMessage = "Yayın adı en fazla 80 karakter olabilir.")] public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Hedef ritim bilgisi gereklidir."), StringLength(200, ErrorMessage = "Hedef ritim bilgisi en fazla 200 karakter olabilir.")] public string Cadence { get; set; } = string.Empty;
    public bool ApproveSender { get; set; }
    [StringLength(254)] public string? SenderEmail { get; set; }
    [StringLength(80)] public string? SenderName { get; set; }
    /// <summary>Attach the existing domain text rule to its editable field without duplicating that rule.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DevCoreBlog.Core.Publishing.PublicationSettings.ValidText(Name, 80))
            yield return new("Yayın adı en fazla 80 karakterlik düz metin olmalı; başında/sonunda boşluk, HTML veya kontrol karakteri kullanmayın.", [nameof(Name)]);
        if (!DevCoreBlog.Core.Publishing.PublicationSettings.ValidText(Cadence, 200))
            yield return new("Hedef ritim bilgisi en fazla 200 karakterlik düz metin olmalı; başında/sonunda boşluk, HTML veya kontrol karakteri kullanmayın.", [nameof(Cadence)]);
    }
    public override string ToString() => nameof(PublicationSettingsInput);
}
public sealed record PublicationSettingsModel(PublicationSettingsInput Input, PublicationSettingsPage Page, string? Error = null);
