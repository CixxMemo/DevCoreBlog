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
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DevCoreBlog.Data.Repositories;

// CategoryRepository extends GenericRepository<Category>
// T is replaced with Category, so all methods work with Category entities
public class CategoryRepository : GenericRepository<Category>, ICategoryRepository, IActiveCategoryLookup
{
    private const string PostsCategoryForeignKey = "FK_Posts_Categories_CategoryId";
    private const string SlugIndex = "IX_Categories_Slug";
    private readonly ILogger<CategoryRepository> _logger;

    // Constructor passes the ApplicationDbContext to the base class
    public CategoryRepository(
        ApplicationDbContext context,
        ILogger<CategoryRepository> logger) : base(context)
    {
        _logger = logger;
    }

    /// <summary>Persists one edit only when its loaded content version still matches.</summary>
    public Task<bool> TrySaveVersionedEditAsync(
        Category category, long expectedVersion, CancellationToken cancellationToken = default) =>
        base.TrySaveVersionedEditAsync(
            category, saved => saved.EditVersion, expectedVersion, cancellationToken);

    /// <summary>Checks a candidate before insert; the unique index resolves races.</summary>
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        _context.Categories.AsNoTracking()
            .AnyAsync(category => category.Slug == slug, cancellationToken);

    /// <summary>Detaches only the failed insert so another slug can be tried.</summary>
    public async Task<bool> TryCreateWithSlugAsync(
        Category category, CancellationToken cancellationToken)
    {
        await _context.Categories.AddAsync(category, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: SlugIndex
            })
        {
            _context.Entry(category).State = EntityState.Detached;
            category.Id = 0;
            return false;
        }
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

    // Public navigation must never expose an inactive category.
    public Task<Category?> GetActiveCategoryBySlugAsync(string slug) =>
        _context.Categories.AsNoTracking()
            .FirstOrDefaultAsync(category => category.Slug == slug && category.IsActive);

    public async Task<IEnumerable<Category>> GetActiveCategoriesAsync() =>
        await _context.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .ToListAsync();

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

    /// <summary>
    /// Deletes an empty category and converts only the known posts/category
    /// foreign-key race into a business-rule result.
    /// </summary>
    public async Task<bool> TryDeleteEmptyCategoryAsync(
        Category category,
        CancellationToken cancellationToken = default)
    {
        _context.Categories.Remove(category);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation,
                ConstraintName: PostsCategoryForeignKey
            })
        {
            _context.Entry(category).State = EntityState.Unchanged;
            _logger.LogWarning(
                "Category deletion was blocked because posts reference category {CategoryId}.",
                category.Id);
            return false;
        }
    }

    public Task<bool> IsActiveCategoryAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        return _context.Categories
            .AsNoTracking()
            .AnyAsync(
                category => category.Id == categoryId && category.IsActive,
                cancellationToken);
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
