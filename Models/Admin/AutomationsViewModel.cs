using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Models.Admin;

/// <summary>Provides real categories and validated integration settings to the documentation view.</summary>
public sealed record AutomationsViewModel(
    IReadOnlyList<Category> Categories, string SiteUrl, IReadOnlyList<string> CorsOrigins);
