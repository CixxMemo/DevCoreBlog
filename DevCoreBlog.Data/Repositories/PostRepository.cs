using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Core.Validation;
using System.Linq.Expressions;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Runs public post queries through one SQL-translatable visibility rule.</summary>
public class PostRepository : GenericRepository<Post>, IPostRepository
{
    private const string SlugIndex = "IX_Posts_Slug";

    public PostRepository(ApplicationDbContext context) : base(context) { }

    /// <summary>Persists one edit only when its loaded content version still matches.</summary>
    public Task<bool> TrySaveVersionedEditAsync(
        Post post, long expectedVersion, CancellationToken cancellationToken = default) =>
        base.TrySaveVersionedEditAsync(
            post, saved => saved.EditVersion, expectedVersion, cancellationToken);

    /// <summary>Checks a candidate before insert; the unique index remains the final arbiter.</summary>
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        _context.Posts.AsNoTracking()
            .AnyAsync(post => post.Slug == slug, cancellationToken);

    /// <summary>Retries may reuse the entity after this method detaches a collided insert.</summary>
    public async Task<bool> TryCreateWithSlugAsync(
        Post post, CancellationToken cancellationToken)
    {
        await _context.Posts.AddAsync(post, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: SlugIndex
            })
        {
            _context.Entry(post).State = EntityState.Detached;
            post.Id = 0;
            return false;
        }
    }

    // The captured UTC instant is a query parameter; all public paths use this predicate.
    private static Expression<Func<Post, bool>> VisibleAt(DateTime utcNow) =>
        PostPublication.VisibleAt(utcNow);

    private IQueryable<Post> PublicPosts(DateTime utcNow) =>
        _context.Posts.AsNoTracking().Where(VisibleAt(utcNow));

    /// <summary>
    /// Increments only the counter for a still-visible post; a detached scalar
    /// read returns the current value without saving a stale entity graph.
    /// </summary>
    public async Task<int?> IncrementVisibleViewCountAsync(
        int id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var changed = await _context.Posts
            .Where(VisibleAt(utcNow))
            .Where(post => post.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    post => post.ViewCount,
                    post => post.ViewCount + 1),
                cancellationToken);
        if (changed == 0)
        {
            return null;
        }

        return await PublicPosts(utcNow)
            .Where(post => post.Id == id)
            .Select(post => (int?)post.ViewCount)
            .SingleOrDefaultAsync(cancellationToken);
    }

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

    // Visibility precedes ranking; SQL reads only the five displayed fields.
    public async Task<IReadOnlyList<TopReadPost>> GetMostReadPublicPostsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        await PublicPosts(utcNow)
            .OrderByDescending(post => post.ViewCount)
            .ThenByDescending(post => post.PublishDate)
            .ThenBy(post => post.Id)
            .Take(5)
            .Select(post => new TopReadPost(
                post.Id, post.Title, post.Slug, post.Category.Name, post.ViewCount))
            .ToListAsync(cancellationToken);

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
            .OrderByDescending(post => post.PublishDate)
            .ThenBy(post => post.Id)
            .Take(3)
            .ToListAsync();

    public Task<PublicPostPage> GetPublishedPostsPagedAsync(int page, int pageSize, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize);
        return ReadPageAsync(PublicPosts(utcNow).OrderByDescending(post => post.PublishDate)
            .ThenBy(post => post.Id), page, pageSize, cancellationToken);
    }

    public Task<PublicPostPage> GetPostsByCategorySlugPagedAsync(string categorySlug, int page, int pageSize,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize, category: categorySlug);
        return ReadPageAsync(PublicPosts(utcNow).Where(post => post.Category.Slug == categorySlug)
            .OrderByDescending(post => post.PublishDate).ThenBy(post => post.Id), page, pageSize, cancellationToken);
    }

    // Literal substring matching uses the database locale, with LIKE metacharacters escaped.
    public Task<PublicPostPage> SearchPostsPagedAsync(string query, string? categorySlug, int page, int pageSize,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize, query, categorySlug);
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(new PublicPostPage([], 0));
        var literal = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var pattern = $"%{literal}%";
        var posts = PublicPosts(utcNow);
        if (!string.IsNullOrEmpty(categorySlug)) posts = posts.Where(post => post.Category.Slug == categorySlug);
        return ReadPageAsync(posts.Where(post => EF.Functions.ILike(post.Title, pattern, "\\") ||
                EF.Functions.ILike(post.Content, pattern, "\\"))
            .OrderByDescending(post => EF.Functions.ILike(post.Title, literal, "\\"))
            .ThenByDescending(post => EF.Functions.ILike(post.Title, pattern, "\\"))
            .ThenByDescending(post => post.PublishDate).ThenBy(post => post.Id), page, pageSize, cancellationToken);
    }

    // Count and limited card projection share the already-filtered SQL query.
    private static async Task<PublicPostPage> ReadPageAsync(IQueryable<Post> query, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var count = await query.CountAsync(cancellationToken);
        if (page > 1 && (page - 1) * pageSize >= count) return new PublicPostPage([], count);
        var posts = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(post => new PublicPostSummary(post.Id, post.Title, post.Slug, post.Summary,
                post.ThumbnailUrl, post.Category.Name, post.PublishDate, post.ViewCount))
            .ToListAsync(cancellationToken);
        return new PublicPostPage(posts.AsReadOnly(), count);
    }

    // Project before materialization: full content and category graphs never leave PostgreSQL.
    public async Task<IReadOnlyList<PublicFeedPost>> GetLatestPublicPostsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        await PublicPosts(utcNow)
            .OrderByDescending(post => post.PublishDate)
            .ThenBy(post => post.Id)
            .Take(3)
            .Select(post => new PublicFeedPost(
                post.Id, post.Title, post.Slug, post.Summary, post.Excerpt,
                post.ThumbnailUrl, post.PublishDate, post.Category.Name))
            .ToListAsync(cancellationToken);

    // Administrative queries intentionally include drafts, future and inactive rows.
    public async Task<IEnumerable<Post>> GetAllPostsWithCategoryAsync() =>
        await _context.Posts
            .Include(post => post.Category)
            .OrderByDescending(post => post.CreatedDate)
            .ToListAsync();
}
