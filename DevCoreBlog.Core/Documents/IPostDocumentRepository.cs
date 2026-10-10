namespace DevCoreBlog.Core.Documents;

/// <summary>Reads a narrow snapshot and atomically replaces document facts using an expected revision.</summary>
public interface IPostDocumentRepository
{
    Task<StoredPostDocument?> FindAsync(int postId, CancellationToken cancellationToken);
    Task<DocumentWriteOutcome> TrySaveAsync(int postId, long expectedEditVersion,
        PostDocumentWrite document, DateTime updatedUtc, CancellationToken cancellationToken);
}

/// <summary>Nullable facts preserve legacy rows without guessing their format.</summary>
public sealed record StoredPostDocument(long EditVersion, int? Version, string? Json,
    string? PlainText, int? WordCount, int? ReadingMinutes);

/// <summary>Server-produced persistence facts; no publication, counter, media or slug setters.</summary>
public sealed record PostDocumentWrite(int Version, string Json, string PlainText,
    int WordCount, int ReadingMinutes);

public enum DocumentWriteOutcome { Saved, Conflict, NotFound }
