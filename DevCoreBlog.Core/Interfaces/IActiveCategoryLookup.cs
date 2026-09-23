namespace DevCoreBlog.Core.Interfaces;

/// <summary>
/// Exposes the single category fact required when validating a post assignment.
/// </summary>
public interface IActiveCategoryLookup
{
    Task<bool> IsActiveCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default);
}
