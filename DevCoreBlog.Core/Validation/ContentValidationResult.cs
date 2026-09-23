namespace DevCoreBlog.Core.Validation;

/// <summary>
/// Carries expected content-validation failures without using exceptions for user input.
/// </summary>
public sealed class ContentValidationResult
{
    private ContentValidationResult(IReadOnlyList<ContentValidationError> errors)
    {
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<ContentValidationError> Errors { get; }

    public static ContentValidationResult Success() =>
        new(Array.Empty<ContentValidationError>());

    public static ContentValidationResult FromErrors(
        IEnumerable<ContentValidationError> errors) =>
        new(errors.ToArray());
}
