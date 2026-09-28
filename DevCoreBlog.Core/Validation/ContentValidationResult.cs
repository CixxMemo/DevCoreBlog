namespace DevCoreBlog.Core.Validation;

/// <summary>
/// Carries expected content-validation failures without using exceptions for user input.
/// </summary>
public sealed class ContentValidationResult
{
    private ContentValidationResult(IReadOnlyList<ContentValidationError> errors, bool isConflict = false)
    {
        Errors = errors;
        IsConflict = isConflict;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<ContentValidationError> Errors { get; }

    public bool IsConflict { get; }

    public static ContentValidationResult Success() =>
        new(Array.Empty<ContentValidationError>());

    public static ContentValidationResult FromErrors(
        IEnumerable<ContentValidationError> errors) =>
        new(errors.ToArray());

    public static ContentValidationResult Conflict(string message) =>
        new([new(string.Empty, message)], isConflict: true);
}
