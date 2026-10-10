using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>A trust capability created only after the entire input passes validation.</summary>
public sealed class ValidatedContentDocument
{
    internal ValidatedContentDocument(ContentDocument document) => Document = document;
    public ContentDocument Document { get; }
}

public enum DocumentFailureKind { Schema, Limit }

/// <summary>Safe diagnostics contain schema paths and codes, never input values.</summary>
public sealed record DocumentValidationError(DocumentFailureKind Kind, string Code, string Path);

/// <summary>Failure never exposes a partially validated tree.</summary>
public sealed class DocumentValidationResult
{
    internal DocumentValidationResult(ValidatedContentDocument? document, DocumentValidationError? error)
    {
        Document = document;
        Error = error;
    }

    public bool IsValid => Document is not null;
    public ValidatedContentDocument? Document { get; }
    public DocumentValidationError? Error { get; }
}

/// <summary>Private control flow for a bounded traversal; exceptions never escape the boundary.</summary>
internal sealed class InvalidDocumentException(DocumentValidationError error) : Exception
{
    internal DocumentValidationError Error { get; } = error;
}

internal static class DocumentGuard
{
    internal static void Require(bool condition, string code, string path)
    {
        if (!condition) throw new InvalidDocumentException(new(DocumentFailureKind.Schema, code, path));
    }

    internal static void Limit(bool condition, string code, string path)
    {
        if (!condition) throw new InvalidDocumentException(new(DocumentFailureKind.Limit, code, path));
    }
}
