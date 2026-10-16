using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Services.Publishing;
namespace DevCoreBlog.Models.Admin;

/// <summary>Only editorial preferences, a CAS version and configured sender confirmation cross the form boundary.</summary>
public sealed class PublicationSettingsInput
{
    [Required] public long? Version { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Cadence { get; set; } = string.Empty;
    public bool ApproveSender { get; set; }
    [StringLength(254)] public string? SenderEmail { get; set; }
    [StringLength(80)] public string? SenderName { get; set; }
    public override string ToString() => nameof(PublicationSettingsInput);
}
public sealed record PublicationSettingsModel(PublicationSettingsInput Input, PublicationSettingsPage Page, string? Error = null);
