using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Executes bounded, no-tracking queries for the admin dashboard.</summary>
public sealed class AdminDashboardReadRepository : IAdminDashboardReadRepository
{
    private readonly ApplicationDbContext _context;

    public AdminDashboardReadRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Computes all active-post totals in one SQL query.</summary>
    public async Task<PostDashboardTotals> GetPostTotalsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.Posts.AsNoTracking()
            .Where(post => post.IsActive)
            .GroupBy(post => 1)
            .Select(group => new PostDashboardTotals(
                group.Count(),
                group.Count(post => post.IsPublished),
                group.Count(post => !post.IsPublished),
                group.Sum(post => (long)post.ViewCount)))
            .SingleOrDefaultAsync(cancellationToken)
        ?? new PostDashboardTotals(0, 0, 0, 0);

    public Task<int> GetActiveCategoryCountAsync(
        CancellationToken cancellationToken = default) =>
        _context.Categories.AsNoTracking()
            .CountAsync(category => category.IsActive, cancellationToken);

    /// <summary>Projects only the five ranking rows displayed by the view.</summary>
    public async Task<IReadOnlyList<TopReadPost>> GetTopReadPostsAsync(
        CancellationToken cancellationToken = default) =>
        await _context.Posts.AsNoTracking()
            .Where(post => post.IsActive)
            .OrderByDescending(post => post.ViewCount)
            .ThenBy(post => post.Id)
            .Take(5)
            .Select(post => new TopReadPost(
                post.Id,
                post.Title,
                post.Slug,
                post.Category.Name,
                post.ViewCount))
            .ToListAsync(cancellationToken);
}
