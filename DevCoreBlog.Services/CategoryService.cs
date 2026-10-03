using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services;

/// <summary>Coordinates category validation and safe writes through domain contracts.</summary>
public class CategoryService : ICategoryService, ISitemapCategoryReader
{
    private const int MaximumSlugAttempts = 100;
    // Category use cases depend on the domain persistence contract.
    private readonly ICategoryRepository _categoryRepository;
    private readonly PublicListCacheInvalidator _publicListCache;

    // Constructor receives the repository contract via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public CategoryService(
        ICategoryRepository categoryRepository,
        PublicListCacheInvalidator publicListCache)
    {
        // Store the injected repository for use in all service methods
        _categoryRepository = categoryRepository;
        _publicListCache = publicListCache;
    }

    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all categories (without posts)
    public async Task<IEnumerable<Category>> GetAllCategoriesAsync()
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.GetAllAsync();
    }

    public Task<IReadOnlyList<string>> GetSitemapCategorySlugsAsync(
        CancellationToken cancellationToken = default) =>
        _categoryRepository.GetSitemapCategorySlugsAsync(cancellationToken);

    public Task<IEnumerable<Category>> GetActiveCategoriesAsync() =>
        _categoryRepository.GetActiveCategoriesAsync();

    public Task<Category?> GetActiveCategoryBySlugAsync(string slug) =>
        _categoryRepository.GetActiveCategoryBySlugAsync(slug);

    // Get all categories with their posts (for admin listing with post counts)
    public async Task<IEnumerable<Category>> GetAllCategoriesWithPostsAsync()
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.GetAllCategoriesWithPostsAsync();
    }

    // Get a single category by its slug
    public async Task<Category?> GetCategoryBySlugAsync(string slug)
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.GetCategoryBySlugAsync(slug);
    }

    // Get a single category by its Id (for admin edit form)
    public async Task<Category?> GetCategoryByIdAsync(int id)
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.GetByIdAsync(id);
    }

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Create a new category (handles slug generation)
    // Business rule: Slug is auto-generated from Name (using SlugGenerator)
    public async Task<ContentValidationResult> CreateCategoryAsync(
        Category category,
        CancellationToken cancellationToken = default)
    {
        CategoryContentRules.Normalize(category);
        var validationResult = CategoryContentRules.Validate(category);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        var baseSlug = SlugGenerator.GenerateBase(category.Name, "category");
        for (var attempt = 0; attempt < MaximumSlugAttempts; attempt++)
        {
            category.Slug = SlugGenerator.Candidate(baseSlug, attempt);
            if (await _categoryRepository.SlugExistsAsync(category.Slug, cancellationToken))
            {
                continue;
            }

            if (await _categoryRepository.TryCreateWithSlugAsync(
                    category, cancellationToken))
            {
                await _publicListCache.InvalidateAsync(cancellationToken);
                return ContentValidationResult.Success();
            }
        }

        return ContentValidationResult.FromErrors(
        [
            new(nameof(Category.Name), "Could not reserve a unique address. Try another name.")
        ]);
    }

    // Update an existing category without replacing server-owned state.
    // The name can change without moving the category's public URL.
    public async Task<ContentValidationResult?> UpdateCategoryAsync(
        int id,
        string name,
        long expectedEditVersion,
        CancellationToken cancellationToken = default)
    {
        var existingCategory = await _categoryRepository.GetByIdAsync(id);
        if (existingCategory is null)
        {
            return null;
        }

        if (expectedEditVersion != existingCategory.EditVersion)
        {
            return ContentValidationResult.Conflict(
                "This category changed since you opened it. Your edits are still here. Reload the current category before trying again.");
        }

        // Validate a detached candidate before changing the tracked entity. This
        // keeps CreatedDate, IsActive and every other server-owned field intact.
        var editableCategory = new Category { Name = name };
        CategoryContentRules.Normalize(editableCategory);
        var validationResult = CategoryContentRules.Validate(editableCategory);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        existingCategory.Name = editableCategory.Name;
        existingCategory.EditVersion = checked(expectedEditVersion + 1);

        // GetByIdAsync returns a tracked row, so SaveChanges writes only changed
        // properties instead of marking every column as modified.
        if (!await _categoryRepository.TrySaveVersionedEditAsync(
                existingCategory, expectedEditVersion, cancellationToken))
        {
            return ContentValidationResult.Conflict(
                "This category changed since you opened it. Your edits are still here. Reload the current category before trying again.");
        }

        await _publicListCache.InvalidateAsync(cancellationToken);
        return ContentValidationResult.Success();
    }

    // Delete a category by its Id (returns false if category has posts)
    // Business rule: Categories with posts cannot be deleted.
    public async Task<bool> DeleteCategoryAsync(int id)
    {
        // BUSINESS RULE: Check if category has any posts (relationship protection).
        // If posts exist, deletion is blocked before reaching the database.
        var hasPosts = await CategoryHasPostsAsync(id);
        if (hasPosts)
        {
            // Return false to indicate deletion was blocked
            return false;
        }

        // Get the category by Id from repository
        var category = await _categoryRepository.GetByIdAsync(id);

        // If the category exists, the database remains the final authority. A
        // post may be inserted after the early check and before this delete.
        if (category != null)
        {
            var deleted = await _categoryRepository.TryDeleteEmptyCategoryAsync(category);
            if (deleted)
            {
                await _publicListCache.InvalidateAsync();
            }
            return deleted;
        }

        // Return true to indicate deletion was successful
        return true;
    }

    // Check if a category has any posts (for relationship protection)
    public async Task<bool> CategoryHasPostsAsync(int categoryId)
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.CategoryHasPostsAsync(categoryId);
    }
}
