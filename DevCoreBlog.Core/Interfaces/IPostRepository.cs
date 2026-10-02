using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Core.Interfaces;

/// <summary>
/// Supplies the post queries and writes used by the post use cases.
/// Public reads receive one captured UTC instant so visibility stays consistent.
/// </summary>
public interface IPostRepository
{
    Task<DateTime?> GetNextScheduledPublicationAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IEnumerable<Post>> GetPublishedPostsAsync(DateTime utcNow);
    // Fixed five-row public ranking; full content is never projected.
    Task<IReadOnlyList<TopReadPost>> GetMostReadPublicPostsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
    Task<Post?> GetPostBySlugAsync(string slug, DateTime utcNow);
    Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug, DateTime utcNow);
    Task<IEnumerable<Post>> SearchPostsAsync(string query, DateTime utcNow);
    Task<(IEnumerable<Post> Posts, int TotalCount)> GetPublishedPostsPagedAsync(
        int page, int pageSize, DateTime utcNow);
    Task<(IEnumerable<Post> Posts, int TotalCount)> GetPostsByCategorySlugPagedAsync(
        string categorySlug, int page, int pageSize, DateTime utcNow);
    Task<IEnumerable<Post>> GetRelatedPostsAsync(
        int currentPostId, int categoryId, DateTime utcNow);
    Task<IReadOnlyList<PublicFeedPost>> GetLatestPublicPostsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
    Task<int?> IncrementVisibleViewCountAsync(
        int id, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IEnumerable<Post>> GetAllPostsWithCategoryAsync();
    Task<Post?> GetByIdAsync(int id);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task<bool> TryCreateWithSlugAsync(Post post, CancellationToken cancellationToken);
    Task<bool> TrySaveVersionedEditAsync(
        Post post, long expectedVersion, CancellationToken cancellationToken = default);
    Task DeleteAsync(Post post);
    Task SaveChangesAsync();
}
