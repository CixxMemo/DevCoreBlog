using System.Linq.Expressions;
using DevCoreBlog.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Runs public post queries through one SQL-translatable visibility rule.</summary>
public class PostRepository : GenericRepository<Post>
{
    public PostRepository(ApplicationDbContext context) : base(context) { }

    // The captured UTC instant is a query parameter; all public paths use this predicate.
    private static Expression<Func<Post, bool>> VisibleAt(DateTime utcNow) =>
        post => post.IsActive && post.IsPublished && post.PublishDate <= utcNow &&
                post.Category.IsActive;

    private IQueryable<Post> PublicPosts(DateTime utcNow) =>
        _context.Posts.AsNoTracking().Where(VisibleAt(utcNow));

    // The next eligible publication bounds the lifetime of cached public lists.
    public Task<DateTime?> GetNextScheduledPublicationAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        _context.Posts.AsNoTracking()
            .Where(post => post.IsActive && post.IsPublished &&
                post.Category.IsActive && post.PublishDate > utcNow)
            .MinAsync(post => (DateTime?)post.PublishDate, cancellationToken);

    public async Task<IEnumerable<Post>> GetPublishedPostsAsync(DateTime utcNow) =>
        await PublicPosts(utcNow)
            .Include(post => post.Category)
            .OrderByDescending(post => post.CreatedDate)
            .ToListAsync();

    public Task<Post?> GetPostBySlugAsync(string slug, DateTime utcNow) =>
        PublicPosts(utcNow)
            .Include(post => post.Category)
            .FirstOrDefaultAsync(post => post.Slug == slug);

    public async Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(
        string categorySlug, DateTime utcNow) =>
        await PublicPosts(utcNow)
            .Include(post => post.Category)
            .Where(post => post.Category.Slug == categorySlug)
            .OrderByDescending(post => post.CreatedDate)
            .ToListAsync();

    public async Task<IEnumerable<Post>> GetRelatedPostsAsync(
        int currentPostId, int categoryId, DateTime utcNow) =>
        await PublicPosts(utcNow)
            .Include(post => post.Category)
            .Where(post => post.CategoryId == categoryId && post.Id != currentPostId)
            .OrderByDescending(post => post.CreatedDate)
            .Take(3)
            .ToListAsync();

    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPublishedPostsPagedAsync(
        int page, int pageSize, DateTime utcNow)
    {
        var query = PublicPosts(utcNow);
        var totalCount = await query.CountAsync();
        var posts = await query
            .Include(post => post.Category)
            .OrderByDescending(post => post.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (posts, totalCount);
    }

    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPostsByCategorySlugPagedAsync(
        string categorySlug, int page, int pageSize, DateTime utcNow)
    {
        var query = PublicPosts(utcNow)
            .Where(post => post.Category.Slug == categorySlug);
        var totalCount = await query.CountAsync();
        var posts = await query
            .Include(post => post.Category)
            .OrderByDescending(post => post.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (posts, totalCount);
    }

    public async Task<IEnumerable<Post>> SearchPostsAsync(string query, DateTime utcNow)
    {
        var lowerQuery = query.ToLower();
        return await PublicPosts(utcNow)
            .Include(post => post.Category)
            .Where(post => post.Title.ToLower().Contains(lowerQuery) ||
                           post.Content.ToLower().Contains(lowerQuery))
            .OrderByDescending(post => post.Title.ToLower() == lowerQuery)
            .ThenByDescending(post => post.Title.ToLower().Contains(lowerQuery))
            .ThenByDescending(post => post.CreatedDate)
            .ToListAsync();
    }

    // Limit and sort in PostgreSQL so the portfolio feed never loads admin rows.
    public async Task<IEnumerable<Post>> GetLatestPublicPostsAsync(DateTime utcNow) =>
        await PublicPosts(utcNow)
            .Include(post => post.Category)
            .OrderByDescending(post => post.PublishDate)
            .Take(3)
            .ToListAsync();

    // Administrative queries intentionally include drafts, future and inactive rows.
    public async Task<IEnumerable<Post>> GetAllPostsWithCategoryAsync() =>
        await _context.Posts
            .Include(post => post.Category)
            .OrderByDescending(post => post.CreatedDate)
            .ToListAsync();
}
