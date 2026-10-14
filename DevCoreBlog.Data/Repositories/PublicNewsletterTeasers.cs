using System.Data.Common;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Published subscriber newsletters expose title/summary only, never canonical body fields.</summary>
public sealed class PublicNewsletterTeasers(ApplicationDbContext context) : IPublicNewsletterTeasers
{
    public async Task<PublicNewsletterTeaserPage> ReadPageAsync(int page, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (page is < 1 or > PublicNewsletterTeaserPage.MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        try
        {
            var query = context.Posts.AsNoTracking()
                .Where(PostPublication.InStateAt(PostPublicationState.Published, utcNow))
                .Where(post => post.ContentKind == PostContentKind.Newsletter && post.AccessScope == PostAccessScope.Subscribers);
            var count = await query.TagWith("F32 public newsletter teaser count").CountAsync(cancellationToken);
            var items = await query.OrderByDescending(post => post.PublishDate).ThenBy(post => post.Id)
                .Skip((page - 1) * PublicNewsletterTeaserPage.PageSize).Take(PublicNewsletterTeaserPage.PageSize)
                .Select(post => new PublicNewsletterTeaser(post.Title, post.Summary.Substring(0, PublicNewsletterTeaserPage.SummaryRunes)))
                .TagWith("F32 public newsletter teaser fields").ToArrayAsync(cancellationToken);
            return new(items, count);
        }
        catch (DbException) { throw new NewsletterTeaserStorageException(); }
    }
}
