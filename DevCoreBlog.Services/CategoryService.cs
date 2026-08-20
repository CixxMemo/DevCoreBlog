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
//   - Slug is regenerated on update (in case Name changed)
//   - Categories with posts cannot be deleted (cascade protection)
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services.Interfaces;

namespace DevCoreBlog.Services;

// Service class for Category-related business logic
// Implements ICategoryService interface
public class CategoryService : ICategoryService
{
    // Private readonly field to hold the injected CategoryRepository
    private readonly CategoryRepository _categoryRepository;

    // Constructor receives CategoryRepository via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public CategoryService(CategoryRepository categoryRepository)
    {
        // Store the injected repository for use in all service methods
        _categoryRepository = categoryRepository;
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
    public async Task CreateCategoryAsync(Category category)
    {
        // BUSINESS RULE: Auto-generate URL-friendly slug from the category name
        // Example: "C# Dersleri" → "c-sharp-dersleri"
        category.Slug = SlugGenerator.Generate(category.Name);

        // Add the category to the database via repository
        await _categoryRepository.AddAsync(category);

        // Commit the transaction to the database
        await _categoryRepository.SaveChangesAsync();
    }

    // Update an existing category (handles slug regeneration)
    // Business rule: Slug is regenerated in case the Name was changed
    public async Task UpdateCategoryAsync(Category category)
    {
        // BUSINESS RULE: Regenerate slug in case the name was changed
        // This ensures the slug always matches the current name
        category.Slug = SlugGenerator.Generate(category.Name);

        // Update the category in the database via repository
        await _categoryRepository.UpdateAsync(category);

        // Commit the transaction to the database
        await _categoryRepository.SaveChangesAsync();
    }

    // Delete a category by its Id (returns false if category has posts)
    // Business rule: Categories with posts cannot be deleted (cascade protection)
    public async Task<bool> DeleteCategoryAsync(int id)
    {
        // BUSINESS RULE: Check if category has any posts (cascade protection)
        // If posts exist, deletion is blocked to prevent cascade data loss
        var hasPosts = await CategoryHasPostsAsync(id);
        if (hasPosts)
        {
            // Return false to indicate deletion was blocked
            return false;
        }

        // Get the category by Id from repository
        var category = await _categoryRepository.GetByIdAsync(id);

        // If category exists, delete it
        if (category != null)
        {
            await _categoryRepository.DeleteAsync(category);
            await _categoryRepository.SaveChangesAsync();
        }

        // Return true to indicate deletion was successful
        return true;
    }

    // Check if a category has any posts (for cascade protection)
    public async Task<bool> CategoryHasPostsAsync(int categoryId)
    {
        // Delegate to repository — no additional business logic needed
        return await _categoryRepository.CategoryHasPostsAsync(categoryId);
    }
}
