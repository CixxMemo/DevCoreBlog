using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Services.Interfaces;

/// <summary>Reads at most three currently visible posts without exposing entity graphs.</summary>
public interface IPublicFeedService
{
    Task<IReadOnlyList<PublicFeedPost>> GetLatestPublicPostsAsync(
        CancellationToken cancellationToken = default);
}
