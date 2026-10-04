namespace DevCoreBlog.Models.Admin;

/// <summary>Contains validated presentation data without a persisted publication decision.</summary>
public sealed record PostPreviewModel(string Title, string Content, string CategoryName, string ThumbnailUrl, string? ThumbnailAlt);
