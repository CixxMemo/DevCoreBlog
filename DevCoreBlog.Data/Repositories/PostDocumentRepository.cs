using DevCoreBlog.Core.Documents;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

/// <summary>One conditional SQL statement owns the document transaction and leaves other fields untouched.</summary>
public sealed class PostDocumentRepository(ApplicationDbContext context) : IPostDocumentRepository
{
    public Task<StoredPostDocument?> FindAsync(int postId, CancellationToken cancellationToken) =>
        context.Posts.AsNoTracking().Where(post => post.Id == postId)
            .Select(post => new StoredPostDocument(post.EditVersion, post.DocumentVersion,
                post.DocumentJson, post.DocumentPlainText, post.DocumentWordCount, post.DocumentReadingMinutes))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<DocumentWriteOutcome> TrySaveAsync(int postId, long expectedEditVersion,
        PostDocumentWrite document, DateTime updatedUtc, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(postId);
        if (expectedEditVersion <= 0 || expectedEditVersion == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedEditVersion));
        ArgumentNullException.ThrowIfNull(document);
        if (updatedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(updatedUtc));

        // ExecuteUpdate does not apply EF concurrency automatically: the predicate is mandatory.
        // A single PostgreSQL UPDATE is atomic, including constraint failure and revision advancement.
        var affected = await context.Posts.Where(post => post.Id == postId && post.EditVersion == expectedEditVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(post => post.DocumentVersion, document.Version)
                .SetProperty(post => post.DocumentJson, document.Json)
                .SetProperty(post => post.DocumentPlainText, document.PlainText)
                .SetProperty(post => post.DocumentWordCount, document.WordCount)
                .SetProperty(post => post.DocumentReadingMinutes, document.ReadingMinutes)
                .SetProperty(post => post.UpdatedDate, updatedUtc)
                .SetProperty(post => post.EditVersion, expectedEditVersion + 1), cancellationToken);
        if (affected == 1) return DocumentWriteOutcome.Saved;
        return await context.Posts.AnyAsync(post => post.Id == postId, cancellationToken)
            ? DocumentWriteOutcome.Conflict : DocumentWriteOutcome.NotFound;
    }
}
