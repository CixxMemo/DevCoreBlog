namespace DevCoreBlog.Core.Validation;

/// <summary>
/// Identifies a business-rule failure and the input field that caused it.
/// </summary>
public sealed record ContentValidationError(string Field, string Message);
