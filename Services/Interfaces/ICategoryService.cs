// =============================================================================
// ICategoryService.cs — Category Service Interface (Business Logic Contract)
// =============================================================================
// This interface defines the business logic operations for blog categories.
// It acts as a contract between the Controller and the Service layer.
//
// What is a Service Interface?
//   - It defines WHAT operations the service can perform, but not HOW.
//   - The actual implementation is in CategoryService.cs.
//   - Benefits:
//     1. Loose Coupling — Controllers depend on ICategoryService, not CategoryService
//     2. Testability — You can create a "fake" service for unit testing
//     3. Flexibility — You can swap implementations without changing the controller
//
// Why separate from Repository?
//   - Repository handles data access (CRUD operations on the database)
//   - Service handles business logic (rules, validations, transformations)
//   - Example: Slug generation is a business rule → belongs in Service
//   - Example: Cascade protection (check if category has posts) → belongs in Service
// =============================================================================

using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Services.Interfaces;

// Service interface for Category-related business logic
public interface ICategoryService
{
    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all categories (without posts)
    Task<IEnumerable<Category>> GetAllCategoriesAsync();

    // Get all categories with their posts (for admin listing with post counts)
    Task<IEnumerable<Category>> GetAllCategoriesWithPostsAsync();

    // Get a single category by its slug
    Task<Category?> GetCategoryBySlugAsync(string slug);

    // Get a single category by its Id (for admin edit form)
    Task<Category?> GetCategoryByIdAsync(int id);

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Create a new category (handles slug generation)
    Task CreateCategoryAsync(Category category);

    // Update an existing category (handles slug regeneration)
    Task UpdateCategoryAsync(Category category);

    // Delete a category by its Id (returns false if category has posts)
    Task<bool> DeleteCategoryAsync(int id);

    // Check if a category has any posts (for cascade protection)
    Task<bool> CategoryHasPostsAsync(int categoryId);
}
