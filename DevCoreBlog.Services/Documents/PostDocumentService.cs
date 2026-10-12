using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services.Documents;

/// <summary>Validates once, derives from the same immutable tree and commits only server-produced facts.</summary>
public sealed class PostDocumentService(IPostDocumentRepository repository, ContentDocumentValidator validator,
    DocumentTextProducer textProducer, TimeProvider timeProvider, PublicListCacheInvalidator publicListCache)
{
    public async Task<PostDocumentSaveResult> SaveAsync(int postId, long expectedEditVersion,
        string documentJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (postId <= 0 || expectedEditVersion <= 0 || expectedEditVersion == long.MaxValue)
            return new(PostDocumentSaveStatus.InvalidInput);
        var validation = validator.Validate(documentJson);
        if (validation.Document is not { } document)
            return new(PostDocumentSaveStatus.InvalidDocument, Error: validation.Error);
        var reading = textProducer.Produce(document);
        // Keep the validated Tiptap envelope verbatim; the Core tree is not its wire serializer.
        var write = new PostDocumentWrite(document.Document.Version, documentJson,
            reading.PlainText, reading.WordCount, reading.ReadingMinutes);
        var outcome = await repository.TrySaveAsync(postId, expectedEditVersion, write,
            timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (outcome == DocumentWriteOutcome.NotFound) return new(PostDocumentSaveStatus.NotFound);
        if (outcome == DocumentWriteOutcome.Conflict) return new(PostDocumentSaveStatus.Conflict);
        await publicListCache.InvalidateAsync(cancellationToken);
        return new(PostDocumentSaveStatus.Saved, expectedEditVersion + 1);
    }

    /// <summary>Storage is not a trust capability: reread validates the envelope and every derived fact.</summary>
    public async Task<PostDocumentReadResult> ReadAsync(int postId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(postId);
        var stored = await repository.FindAsync(postId, cancellationToken);
        if (stored is null) return new(PostDocumentReadStatus.NotFound);
        return StoredDocumentReader.Read(stored, validator, textProducer);
    }
}

public enum PostDocumentSaveStatus { Saved, InvalidInput, InvalidDocument, NotFound, Conflict }
/// <summary>Expected save outcomes expose a new revision only after a committed write.</summary>
public sealed record PostDocumentSaveResult(PostDocumentSaveStatus Status, long? EditVersion = null,
    DocumentValidationError? Error = null);

public enum PostDocumentReadStatus { Ready, Legacy, NotFound, Inconsistent }
/// <summary>Only a revalidated, consistent stored snapshot grants document and reading values.</summary>
public sealed record PostDocumentReadResult(PostDocumentReadStatus Status, long? EditVersion = null,
    ValidatedContentDocument? Document = null, DocumentReading? Reading = null, string? Json = null);
