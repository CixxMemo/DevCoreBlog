using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Services.Interfaces;

/// <summary>Reads at most twenty currently visible articles for RSS without entity graphs.</summary>
public interface IRssPostReader
{
    Task<IReadOnlyList<RssPost>> GetRssPostsAsync(CancellationToken cancellationToken = default);
}
