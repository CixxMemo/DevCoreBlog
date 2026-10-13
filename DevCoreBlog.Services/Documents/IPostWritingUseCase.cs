namespace DevCoreBlog.Services.Documents;

/// <summary>The form consumes only document read/save operations, independent of its inventory.</summary>
public interface IPostWritingUseCase
{
    Task<EditorPostRead?> ReadAsync(int id, CancellationToken cancellationToken);
    Task<EditorPostSave> SaveAsync(EditorPostInput input, CancellationToken cancellationToken);
}

/// <summary>Trusted use-case scope, never bound from a form or inferred from document text.</summary>
public enum WritingPurpose { Article, Newsletter }
