using System.Data;
using DevCoreBlog.Data;
using DevCoreBlog.Services.Documents;
using Microsoft.EntityFrameworkCore;

/// <summary>Owns one bounded read-only snapshot, including hidden posts and legacy reference protection.</summary>
internal static class InventoryScanner
{
    public static async Task<object> ScanAsync(string connection, ReferenceMatch[] results, CancellationToken cancellation)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection, provider => provider.CommandTimeout(10)).Options;
        await using var db = new ApplicationDbContext(options);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellation);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellation);
        var startedAt = DateTimeOffset.UtcNow;
        var scanned = 0;
        long characters = 0;
        var validator = new ContentDocumentValidator();
        var producer = new DocumentTextProducer();
        var documentCounts = new Dictionary<string, int>();
        var uncertainIds = new List<int>();
        var uncertainPosts = 0;
        // Stream a narrow projection of every post, including drafts/inactive posts, without tracking.
        await foreach (var post in db.Posts.AsNoTracking().OrderBy(post => post.Id)
            .Take(10_001).Select(post => new { post.Id, post.ThumbnailPublicId, ThumbnailUrl = post.ThumbnailUrl.Substring(0, 1_048_577),
                Content = post.Content.Substring(0, 1_048_577), Summary = post.Summary.Substring(0, 1_048_577), Excerpt = post.Excerpt.Substring(0, 1_048_577),
                post.DocumentVersion, DocumentJson = post.DocumentJson == null ? null : post.DocumentJson.Substring(0, 1_048_577),
                DocumentPlainText = post.DocumentPlainText == null ? null : post.DocumentPlainText.Substring(0, 1_048_577),
                post.DocumentWordCount, post.DocumentReadingMinutes }).AsAsyncEnumerable().WithCancellation(cancellation))
        {
            if (++scanned > 10_000) throw new InvalidOperationException();
            characters += post.Content.Length + (post.DocumentJson?.Length ?? 0) +
                (post.DocumentPlainText?.Length ?? 0) + post.Summary.Length + post.Excerpt.Length + post.ThumbnailUrl.Length;
            if (characters > 32 * 1024 * 1024 ||
                new[] { post.Content, post.Summary, post.Excerpt, post.ThumbnailUrl }.Any(text => text.Length > 1_048_576))
                throw new InvalidOperationException();
            cancellation.ThrowIfCancellationRequested();
            var document = DocumentReferences.Read(post.DocumentVersion, post.DocumentJson, post.DocumentPlainText,
                post.DocumentWordCount, post.DocumentReadingMinutes, validator, producer);
            documentCounts[document.Status] = documentCounts.GetValueOrDefault(document.Status) + 1;
            if (document.Status is not "Legacy" and not "Validated")
            {
                uncertainPosts++;
                if (uncertainIds.Count < 20) uncertainIds.Add(post.Id);
            }
            var texts = new[] { post.ThumbnailUrl, post.Content, post.Summary, post.Excerpt }
                .Select(ReferenceMatch.Normalize).ToArray();
            foreach (var match in results)
            {
                cancellation.ThrowIfCancellationRequested();
                match.Observe(post.Id, post.ThumbnailPublicId, texts, document.Sources);
            }
        }
        await snapshot.CommitAsync(cancellation);
        return new
        {
            dryRun = true, completeReferenceCoverage = uncertainPosts == 0,
            documentCounts, uncertainPosts, uncertainSamplePostIds = uncertainIds,
            uncertainSamplesTruncated = uncertainPosts > uncertainIds.Count, snapshotStartedAt = startedAt, scannedPosts = scanned,
            warning = "No deletion permission. Explicit URL aliases are required for transformed/legacy URLs. Unsaved drafts, external sites and uploads after this snapshot are not covered.",
            assets = results.Select(match => match.Report(uncertainPosts))
        };
    }
}
