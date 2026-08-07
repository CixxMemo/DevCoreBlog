// =============================================================================
// PostRepository.cs — Post-Specific Data Access Layer
// =============================================================================
// This class extends GenericRepository<Post> to provide Post-specific queries
// that go beyond the basic CRUD operations.
//
// Why create a separate PostRepository?
//   - GenericRepository<Post> provides basic CRUD (GetById, GetAll, Add, etc.)
//   - But sometimes we need custom queries specific to Post, like:
//     - Getting all published posts with their Category
//     - Searching posts by title or content
//     - Getting posts by category slug
//   - PostRepository is the place for these custom queries.
//
// How does inheritance work here?
//   - PostRepository inherits from GenericRepository<Post>
//   - This means it automatically gets all the basic CRUD methods
//   - We only add new methods that are specific to Post
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Repositories;

// PostRepository extends GenericRepository<Post>
// T is replaced with Post, so all methods work with Post entities
public class PostRepository : GenericRepository<Post>
{
    // Constructor passes the ApplicationDbContext to the base class
    public PostRepository(ApplicationDbContext context) : base(context)
    {
    }

    // -------------------------------------------------------------------------
    // GetPublishedPostsAsync — Get all published posts with their Category
    // -------------------------------------------------------------------------
    // Returns all posts where IsActive is true, ordered by CreatedDate descending.
    // Includes the Category navigation property for display in the view.
    public async Task<IEnumerable<Post>> GetPublishedPostsAsync()
    {
        // Query published posts, include their Category navigation property,
        // order by CreatedDate descending (newest first), and convert to a List.
        return await _context.Posts
            .Include(p => p.Category)           // Eager-load the related Category
            .Where(p => p.IsActive)              // Only show published posts
            .OrderByDescending(p => p.CreatedDate) // Newest posts first
            .ToListAsync();                     // Execute query asynchronously
    }

    // -------------------------------------------------------------------------
    // GetPostBySlugAsync — Get a single post by its slug
    // -------------------------------------------------------------------------
    // Returns the post with the specified slug, or null if not found.
    // Only returns published posts (IsActive = true).
    public async Task<Post?> GetPostBySlugAsync(string slug)
    {
        // Find the first post matching the slug AND is published.
        // Include the Category navigation property so the view can display it.
        return await _context.Posts
            .Include(p => p.Category)                              // Eager-load Category
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive); // Match slug + published
    }

    // -------------------------------------------------------------------------
    // GetPostsByCategorySlugAsync — Get all posts in a category (by slug)
    // -------------------------------------------------------------------------
    // Returns all published posts in the category with the specified slug.
    public async Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug)
    {
        // First, find the category by slug
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == categorySlug);

        // If category not found, return empty list
        if (category == null)
        {
            return Enumerable.Empty<Post>();
        }

        // Query published posts in this category, ordered by creation date (newest first)
        return await _context.Posts
            .Include(p => p.Category)                // Eager-load Category for display
            .Where(p => p.CategoryId == category.Id && p.IsActive) // Filter by category + published
            .OrderByDescending(p => p.CreatedDate)      // Newest posts first
            .ToListAsync();                           // Execute query asynchronously
    }

    // -------------------------------------------------------------------------
    // SearchPostsAsync — Search posts by title or content
    // -------------------------------------------------------------------------
    // Returns all published posts where the title or content contains the search query.
    public async Task<IEnumerable<Post>> SearchPostsAsync(string query)
    {
        // Convert query to lowercase for case-insensitive search
        var lowerQuery = query.ToLower();

        // Search in Title and Content fields
        return await _context.Posts
            .Include(p => p.Category)                // Eager-load Category for display
            .Where(p => p.IsActive &&                 // Only published posts
                       (p.Title.ToLower().Contains(lowerQuery) ||
                        p.Content.ToLower().Contains(lowerQuery)))
            .OrderByDescending(p => p.CreatedDate)      // Newest posts first
            .ToListAsync();                           // Execute query asynchronously
    }

    // -------------------------------------------------------------------------
    // GetAllPostsWithCategoryAsync — Get ALL posts (including unpublished) with Category
    // -------------------------------------------------------------------------
    // This method is used by the Admin panel to list all posts.
    // Unlike GetPublishedPostsAsync, it returns posts regardless of IsActive status.
    // The Category navigation property is eager-loaded for display in the admin table.
    public async Task<IEnumerable<Post>> GetAllPostsWithCategoryAsync()
    {
        // Query all posts (no IsActive filter), include Category,
        // order by CreatedDate descending (newest first)
        return await _context.Posts
            .Include(p => p.Category)              // Eager-load the related Category
            .OrderByDescending(p => p.CreatedDate) // Newest posts first
            .ToListAsync();                        // Execute query asynchronously
    }
}
