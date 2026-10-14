using System.Data.Common;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Both private queries share publication/scope filtering; list SQL never retrieves a document body.</summary>
public sealed class NewsletterArchive(ApplicationDbContext db) : INewsletterArchive
{
    private IQueryable<Post> Published(DateTime utcNow) => db.Posts.AsNoTracking()
        .Where(PostPublication.InStateAt(PostPublicationState.Published, utcNow))
        .Where(post => post.ContentKind == PostContentKind.Newsletter && post.AccessScope == PostAccessScope.Subscribers);

    public async Task<NewsletterArchivePage> ReadPageAsync(int page, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (page is < 1 or > NewsletterArchivePage.MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        try
        {
            var query = Published(utcNow);
            var count = await query.TagWith("F31 archive count").CountAsync(cancellationToken);
            var items = await query.OrderByDescending(post => post.PublishDate).ThenBy(post => post.Id)
                .Skip((page - 1) * NewsletterArchivePage.PageSize).Take(NewsletterArchivePage.PageSize)
                .Select(post => new NewsletterArchiveItem(post.Slug, post.Title, post.Summary.Substring(0, 320), post.PublishDate))
                .TagWith("F31 archive list").ToArrayAsync(cancellationToken);
            return new(items, count);
        }
        catch (DbException) { throw new NewsletterArchiveStorageException(); }
    }

    public async Task<NewsletterArchiveDocument?> FindAsync(string slug, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200) throw new ArgumentOutOfRangeException(nameof(slug));
        try
        {
            return await Published(utcNow).Where(post => post.Slug == slug)
                .Select(post => new NewsletterArchiveDocument(post.Slug, post.Title, post.PublishDate,
                    new StoredPostDocument(post.EditVersion, post.DocumentVersion, post.DocumentJson,
                        post.DocumentPlainText, post.DocumentWordCount, post.DocumentReadingMinutes)))
                .TagWith("F31 archive document").SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException) { throw new NewsletterArchiveStorageException(); }
    }
}
