using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Documents;

/// <summary>Owns atomic editor metadata and document writes; reads omit both body formats.</summary>
public interface IEditorPostRepository
{
    Task<Post?> FindMetadataAsync(int id, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(Post post, CancellationToken cancellationToken);
    Task<bool> TryUpdateAsync(Post post, long expectedVersion, CancellationToken cancellationToken);
}
