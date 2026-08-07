// =============================================================================
// CategoryRepository.cs — Category-Specific Data Access Layer
// =============================================================================
// This class extends GenericRepository<Category> to provide Category-specific
// queries that go beyond the basic CRUD operations.
//
// Why create a separate CategoryRepository?
//   - GenericRepository<Category> provides basic CRUD (GetById, GetAll, Add, etc.)
//   - But sometimes we need custom queries specific to Category, like:
//     - Getting a category by its slug
//     - Getting all categories with their post counts
//     - Checking if a category has any posts (before deletion)
//   - CategoryRepository is the place for these custom queries.
//
// How does inheritance work here?
//   - CategoryRepository inherits from GenericRepository<Category>
//   - This means it automatically gets all the basic CRUD methods
//   - We only add new methods that are specific to Category
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Repositories;

// CategoryRepository extends GenericRepository<Category>
// T is replaced with Category, so all methods work with Category entities
public class CategoryRepository : GenericRepository<Category>
{
    // Constructor passes the ApplicationDbContext to the base class
    public CategoryRepository(ApplicationDbContext context) : base(context)
    {
    }

    // -------------------------------------------------------------------------
    // GetCategoryBySlugAsync — Get a single category by its slug
    // -------------------------------------------------------------------------
    // Returns the category with the specified slug, or null if not found.
    public async Task<Category?> GetCategoryBySlugAsync(string slug)
    {
        // Find the first category matching the slug
        return await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == slug);
    }

    // -------------------------------------------------------------------------
    // GetAllCategoriesWithPostsAsync — Get all categories with their posts
    // -------------------------------------------------------------------------
    // Returns all categories, including their Posts collection.
    // This is useful for displaying the post count per category.
    public async Task<IEnumerable<Category>> GetAllCategoriesWithPostsAsync()
    {
        // Query all categories, eagerly loading their Posts navigation property.
        // Include() performs a SQL JOIN so we don't get N+1 query problems.
        return await _context.Categories
            .Include(c => c.Posts)
            .ToListAsync();
    }

    // -------------------------------------------------------------------------
    // CategoryHasPostsAsync — Check if a category has any posts
    // -------------------------------------------------------------------------
    // Returns true if the category with the specified Id has at least one post.
    // This is used to prevent deletion of categories that have posts.
    public async Task<bool> CategoryHasPostsAsync(int categoryId)
    {
        // Check if any post belongs to this category
        return await _context.Posts
            .AnyAsync(p => p.CategoryId == categoryId);
    }

    // -------------------------------------------------------------------------
    // GetCategoryWithPostsAsync — Get a category with its posts
    // -------------------------------------------------------------------------
    // Returns the category with the specified Id, including its Posts collection.
    // This is used to check the post count before deletion.
    public async Task<Category?> GetCategoryWithPostsAsync(int id)
    {
        // Find the category by Id, including its Posts collection
        return await _context.Categories
            .Include(c => c.Posts)
            .FirstOrDefaultAsync(c => c.Id == id);
    }
}
