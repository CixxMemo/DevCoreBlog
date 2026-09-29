using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Interfaces;

/// <summary>Supplies only the category operations consumed by category use cases.</summary>
public interface ICategoryRepository
{
    Task<IEnumerable<Category>> GetAllAsync();
    Task<IEnumerable<Category>> GetActiveCategoriesAsync();
    Task<Category?> GetActiveCategoryBySlugAsync(string slug);
    Task<IEnumerable<Category>> GetAllCategoriesWithPostsAsync();
    Task<Category?> GetCategoryBySlugAsync(string slug);
    Task<Category?> GetByIdAsync(int id);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task<bool> TryCreateWithSlugAsync(Category category, CancellationToken cancellationToken);
    Task<bool> TrySaveVersionedEditAsync(
        Category category, long expectedVersion, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteEmptyCategoryAsync(
        Category category, CancellationToken cancellationToken = default);
    Task<bool> CategoryHasPostsAsync(int categoryId);
}
