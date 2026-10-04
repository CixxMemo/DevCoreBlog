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
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services.Interfaces;

// Service interface for Post-related business logic
public interface IPostService
{
    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all published posts with their Category (ordered by newest first)
    Task<IEnumerable<Post>> GetPublishedPostsAsync();
    // All visible posts contribute to this fixed-size lifetime request ranking.
    Task<IReadOnlyList<TopReadPost>> GetMostReadPublicPostsAsync(
        CancellationToken cancellationToken = default);

    // Get a single post by its slug (only if published)
    Task<Post?> GetPostBySlugAsync(string slug);

    // Get all posts in a category (by category slug, only published posts)
    Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug);

    // Search posts by title or content (only published posts)
    Task<PublicPostPage> SearchPostsPagedAsync(string query, string? categorySlug, int page, int pageSize,
        CancellationToken cancellationToken = default);

    // Get published posts with pagination
    Task<PublicPostPage> GetPublishedPostsPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    // Get published posts by category with pagination
    Task<PublicPostPage> GetPostsByCategorySlugPagedAsync(string categorySlug, int page, int pageSize, CancellationToken cancellationToken = default);

    // Get related posts in the same category (excluding the current post)
    Task<IReadOnlyList<PublicPostSummary>> GetRelatedPostsAsync(int currentPostId, int categoryId,
        CancellationToken cancellationToken = default);

    // Atomically count an eligible public detail GET; null means no visible row.
    Task<int?> IncrementViewCountAsync(
        int id, CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Get all posts (including unpublished) for admin listing
    Task<AdminPostListPage> GetAdminPostsPagedAsync(AdminPostQuery query, CancellationToken cancellationToken = default);
    Task<IEnumerable<Post>> GetAllPostsAsync();

    // Get a single post by its Id (for admin edit form)
    Task<Post?> GetPostByIdAsync(int id);

    // Validate a post before side effects such as media upload.
    Task<ContentValidationResult> ValidatePostAsync(
        Post post,
        CancellationToken cancellationToken = default);

    // Apply editor intent and validate before uploads; other integrations keep their contracts.
    Task<ContentValidationResult> PrepareEditorSaveAsync(
        Post post, PostSaveAction action, CancellationToken cancellationToken = default);

    // Create a new post (handles validation, slug generation and date setting)
    Task<ContentValidationResult> CreatePostAsync(
        Post post,
        CancellationToken cancellationToken = default,
        PostSaveAction? saveAction = null);

    // Update an existing post without changing its public slug.
    Task<ContentValidationResult> UpdatePostAsync(
        Post post,
        CancellationToken cancellationToken = default,
        long? expectedEditVersion = null,
        PostSaveAction? saveAction = null);

    // Delete a post by its Id
    Task DeletePostAsync(int id);
}
