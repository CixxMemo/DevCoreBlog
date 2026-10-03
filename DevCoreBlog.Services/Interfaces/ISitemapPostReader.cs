using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Services.Interfaces;

/// <summary>Reads a bounded sitemap projection through the shared public visibility rule.</summary>
public interface ISitemapPostReader
{
    Task<IReadOnlyList<SitemapPost>> GetSitemapPostsAsync(CancellationToken cancellationToken = default);
}
