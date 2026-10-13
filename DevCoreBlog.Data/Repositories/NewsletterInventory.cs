using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Admin-only inventory is paged in SQL and cannot retrieve newsletter bodies.</summary>
public sealed class NewsletterInventory(ApplicationDbContext context) : INewsletterInventory
{
    public async Task<NewsletterPage> FindPageAsync(int page, CancellationToken cancellationToken)
    {
        if (page is < 1 or > NewsletterPage.MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        try
        {
            var query = context.Posts.AsNoTracking().Where(post => post.ContentKind == PostContentKind.Newsletter
                && post.AccessScope == PostAccessScope.Subscribers);
            var count = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(post => post.CreatedDate).ThenBy(post => post.Id)
                .Skip((page - 1) * NewsletterPage.PageSize).Take(NewsletterPage.PageSize)
                .Select(post => new NewsletterSummary(post.Id, post.Title, post.Summary, post.IsActive,
                    post.Category.IsActive, post.IsPublished, post.PublishDate, post.EditVersion))
                .ToArrayAsync(cancellationToken);
            return new(items, count);
        }
        catch (DbException) { throw new EditorPostStorageException(); }
    }
}
