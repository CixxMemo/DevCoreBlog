// =============================================================================
// IPostService.cs — Post Service Interface (Business Logic Contract)
// =============================================================================
// This interface defines the business logic operations for blog posts.
// It acts as a contract between the Controller and the Service layer.
//
// What is a Service Interface?
//   - It defines WHAT operations the service can perform, but not HOW.
//   - The actual implementation is in PostService.cs.
//   - Benefits:
//     1. Loose Coupling — Controllers depend on IPostService, not PostService
//     2. Testability — You can create a "fake" service for unit testing
//     3. Flexibility — You can swap implementations without changing the controller
//
// Why separate from Repository?
//   - Repository handles data access (CRUD operations on the database)
//   - Service handles business logic (rules, validations, transformations)
//   - Example: Slug generation is a business rule → belongs in Service
//   - Example: Getting a post by Id is data access → belongs in Repository
// =============================================================================

using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Services.Interfaces;

// Service interface for Post-related business logic
public interface IPostService
{
    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all published posts with their Category (ordered by newest first)
    Task<IEnumerable<Post>> GetPublishedPostsAsync();

    // Get a single post by its slug (only if published)
    Task<Post?> GetPostBySlugAsync(string slug);

    // Get all posts in a category (by category slug, only published posts)
    Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug);

    // Search posts by title or content (only published posts)
    Task<IEnumerable<Post>> SearchPostsAsync(string query);

    // Increment the view count of a post by 1 (called when Detail page is visited)
    // Returns the updated Post with the new ViewCount value
    Task<Post?> IncrementViewCountAsync(int id);

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Get all posts (including unpublished) for admin listing
    Task<IEnumerable<Post>> GetAllPostsAsync();

    // Get a single post by its Id (for admin edit form)
    Task<Post?> GetPostByIdAsync(int id);

    // Create a new post (handles slug generation and date setting)
    Task CreatePostAsync(Post post);

    // Update an existing post (handles slug regeneration)
    Task UpdatePostAsync(Post post);

    // Delete a post by its Id
    Task DeletePostAsync(int id);
}
