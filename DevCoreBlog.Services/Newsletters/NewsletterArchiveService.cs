using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Validates current reader access before every private query and revalidates canonical content before rendering.</summary>
public sealed class NewsletterArchiveService(INewsletterArchive archive, IReaderIdentityStore readers,
    ContentDocumentValidator validator, DocumentTextProducer text, DocumentWebRenderer renderer, TimeProvider clock)
{
    public async Task<ArchiveRead<NewsletterArchivePage>> ReadPageAsync(Guid session, int page, CancellationToken ct)
    {
        if (page is < 1 or > NewsletterArchivePage.MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (session == Guid.Empty || !await readers.IsValidAsync(session, now, deadline.Token)) return new(ArchiveReadStatus.Denied);
            return new(ArchiveReadStatus.Ready, await archive.ReadPageAsync(page, now, deadline.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(ArchiveReadStatus.Unavailable); }
    }

    public async Task<ArchiveRead<NewsletterArchiveDetail>> ReadAsync(Guid session, string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200) throw new ArgumentOutOfRangeException(nameof(slug));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (session == Guid.Empty || !await readers.IsValidAsync(session, now, deadline.Token)) return new(ArchiveReadStatus.Denied);
            var stored = await archive.FindAsync(slug, now, deadline.Token);
            if (stored is null) return new(ArchiveReadStatus.NotFound);
            var content = StoredDocumentReader.Read(stored.Content, validator, text);
            // A malformed or legacy newsletter must not fall back to unverified body fields.
            if (content.Status != PostDocumentReadStatus.Ready || content.Document is not { } document)
                return new(ArchiveReadStatus.Unavailable);
            return new(ArchiveReadStatus.Ready, new(stored.Title, stored.PublishedUtc, renderer.Render(document)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(ArchiveReadStatus.Unavailable); }
    }
}

public enum ArchiveReadStatus { Ready, Denied, NotFound, Unavailable }
public sealed record ArchiveRead<T>(ArchiveReadStatus Status, T? Value = null) where T : class;
public sealed record NewsletterArchiveDetail(string Title, DateTime PublishedUtc, RenderedContentDocument Content);
