using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>One INSERT or conditional UPDATE commits metadata and all document facts together.</summary>
public sealed class EditorPostRepository(ApplicationDbContext context, IPostRepository posts) : IEditorPostRepository
{
    public Task<Post?> FindMetadataAsync(int id, CancellationToken cancellationToken) =>
        context.Posts.AsNoTracking().Where(p => p.Id == id).Select(p => new Post {
            Id = p.Id, Title = p.Title, Slug = p.Slug, Summary = p.Summary, Excerpt = p.Excerpt,
            CategoryId = p.CategoryId, IsActive = p.IsActive, IsPublished = p.IsPublished,
            PublishDate = p.PublishDate, EditVersion = p.EditVersion, ContentKind = p.ContentKind,
            AccessScope = p.AccessScope, DocumentVersion = p.DocumentVersion,
            ThumbnailUrl = p.ThumbnailUrl, ThumbnailPublicId = p.ThumbnailPublicId,
            ThumbnailWidth = p.ThumbnailWidth, ThumbnailHeight = p.ThumbnailHeight, ThumbnailAlt = p.ThumbnailAlt
        }).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> TryCreateAsync(Post post, CancellationToken cancellationToken) =>
        posts.TryCreateWithSlugAsync(post, cancellationToken);

    public async Task<bool> TryUpdateAsync(Post post, long expectedVersion, CancellationToken cancellationToken)
    {
        if (post.Id <= 0 || expectedVersion <= 0 || expectedVersion == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        // Never overwrite legacy content, ownership, slug, creation time or the independently updated counter.
        var affected = await context.Posts.Where(p => p.Id == post.Id && p.EditVersion == expectedVersion && p.DocumentVersion == 1)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Title, post.Title).SetProperty(p => p.Summary, post.Summary)
                .SetProperty(p => p.Excerpt, post.Excerpt).SetProperty(p => p.CategoryId, post.CategoryId)
                .SetProperty(p => p.IsActive, post.IsActive).SetProperty(p => p.IsPublished, post.IsPublished)
                .SetProperty(p => p.PublishDate, post.PublishDate).SetProperty(p => p.ThumbnailAlt, post.ThumbnailAlt)
                .SetProperty(p => p.DocumentVersion, post.DocumentVersion).SetProperty(p => p.DocumentJson, post.DocumentJson)
                .SetProperty(p => p.DocumentPlainText, post.DocumentPlainText).SetProperty(p => p.DocumentWordCount, post.DocumentWordCount)
                .SetProperty(p => p.DocumentReadingMinutes, post.DocumentReadingMinutes)
                .SetProperty(p => p.UpdatedDate, post.UpdatedDate).SetProperty(p => p.EditVersion, expectedVersion + 1), cancellationToken);
        return affected == 1;
    }
}
