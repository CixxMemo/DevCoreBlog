namespace DevCoreBlog.Services.Interfaces;

/// <summary>Reads only active category slugs, bounded by the sitemap protocol limit.</summary>
public interface ISitemapCategoryReader
{
    Task<IReadOnlyList<string>> GetSitemapCategorySlugsAsync(CancellationToken cancellationToken = default);
}
