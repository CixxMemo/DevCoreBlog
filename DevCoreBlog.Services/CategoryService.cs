// =============================================================================
// CategoryService.cs — Category Business Logic Layer
// =============================================================================
// This class implements ICategoryService and contains all business logic for
// blog categories. It sits between the Controller and the Repository layer.
//
// What is the Service Layer?
//   - The Service layer contains business logic (rules, validations, transformations).
//   - It coordinates between the Controller and the Repository.
//   - Example flow:
//     1. Controller receives a request (e.g., "Delete a category")
//     2. Controller calls CategoryService.DeleteCategoryAsync(id)
//     3. CategoryService checks business rules (does category have posts?)
//     4. If allowed, CategoryService calls CategoryRepository.DeleteAsync(category)
//     5. CategoryService calls CategoryRepository.SaveChangesAsync() to commit
//     6. Controller receives the result and returns a view/redirect
//
// Why not put business logic in the Controller?
//   - Controllers should only handle HTTP concerns (requests, responses, routing).
//   - Business logic should be in Services for:
//     1. Reusability — Multiple controllers can use the same service
//     2. Testability — Services can be unit tested without HTTP context
//     3. Separation of Concerns — Each layer has one responsibility
//
// Business Rules in CategoryService:
//   - Slug is auto-generated from Name (using SlugGenerator)
//   - Existing slugs stay stable when a name changes
//   - Categories with posts cannot be deleted (relationship protection)
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services;

// Service class for Category-related business logic
// Implements ICategoryService interface
public class CategoryService : ICategoryService
{
    private const int MaximumSlugAttempts = 100;
    // Private readonly field to hold the injected CategoryRepository
    private readonly CategoryRepository _categoryRepository;
    private readonly PublicListCacheInvalidator _publicListCache;

    // Constructor receives CategoryRepository via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public CategoryService(
        CategoryRepository categoryRepository,
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
                existingCategory, saved => saved.EditVersion,
                expectedEditVersion, cancellationToken))
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
