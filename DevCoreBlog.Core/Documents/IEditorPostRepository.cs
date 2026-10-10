using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Documents;

/// <summary>Owns atomic editor writes; storage failures throw EditorPostStorageException, never report a conflict or success.</summary>
public interface IEditorPostRepository
{
    Task<Post?> FindMetadataAsync(int id, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(Post post, CancellationToken cancellationToken);
    Task<bool> TryUpdateAsync(Post post, long expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Provider-neutral write failure; callers retain submitted content without exposing database diagnostics.</summary>
public sealed class EditorPostStorageException : Exception
{
    public EditorPostStorageException() : base("Editor post storage failed.") { }
}
