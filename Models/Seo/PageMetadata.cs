namespace DevCoreBlog.Models.Seo;

/// <summary>Immutable head data; only serialized JSON crosses the script output boundary.</summary>
public sealed record PageMetadata(string Title, string Description, string CanonicalUrl,
    string Type = "website", string? ImageUrl = null, string? ArticleJson = null, bool NoIndex = false);
