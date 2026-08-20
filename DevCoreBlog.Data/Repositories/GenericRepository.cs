// =============================================================================
// GenericRepository.cs — Generic Data Access Layer (Repository Pattern)
// =============================================================================
// This class provides common database operations (CRUD) for any entity that
// inherits from BaseEntity. It implements the IRepository<T> interface.
//
// What is the Repository Pattern?
//   - It separates the data access logic (HOW to get/save data) from the
//     business logic (WHAT to do with the data).
//   - Controllers don't talk to the database directly. Instead, they call
//     the repository, which handles the EF Core queries.
//   - Benefits:
//     1. Testability — You can mock the repository in unit tests
//     2. Separation of Concerns — Each layer has one responsibility
//     3. Reusability — Common operations are written once, used everywhere
//
// Why Generic (<T>)?
//   - Instead of writing separate repositories for Post, Category, etc.,
//     we write one generic repository that works with any entity type.
//   - T is a placeholder for the entity type. When you use GenericRepository<Post>,
//     T becomes Post. When you use GenericRepository<Category>, T becomes Category.
//
// How does DbSet<T> work?
//   - DbSet<T> represents a collection of all entities of type T in the database.
//   - _context.Set<T>() returns the DbSet for the entity type T.
//   - Example: If T is Post, _context.Set<Post>() returns _context.Posts.
//   - This allows us to write generic code that works with any entity.
//
// Note: "where T : BaseEntity" means T must be a class that inherits from BaseEntity.
// This ensures all entities have Id, CreatedDate, and IsActive properties.
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Repositories;

// Generic repository class — implements IRepository<T> for any entity type T
// T must inherit from BaseEntity (ensures Id, CreatedDate, IsActive exist)
public class GenericRepository<T> : IRepository<T> where T : BaseEntity
{
    // Protected field to hold the database context
    // Protected allows derived classes (PostRepository, CategoryRepository) to access it
    // Injected via constructor (dependency injection)
    protected readonly ApplicationDbContext _context;

    // DbSet<T> represents the collection of entities of type T in the database
    // _dbSet is initialized once in the constructor and reused in all methods
    private readonly DbSet<T> _dbSet;

    // Constructor receives the database context via dependency injection
    // The DI container (configured in Program.cs) provides the ApplicationDbContext instance
    public GenericRepository(ApplicationDbContext context)
    {
        // Store the injected context for use in all repository methods
        _context = context;
        // Initialize _dbSet using _context.Set<T>()
        // This returns the DbSet for the entity type T (e.g., _context.Posts if T is Post)
        _dbSet = _context.Set<T>();
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync — Get a single entity by its Id
    // -------------------------------------------------------------------------
    // Returns the entity with the specified Id, or null if not found.
    // Uses FindAsync() which is optimized for primary key lookups.
    public async Task<T?> GetByIdAsync(int id)
    {
        // FindAsync uses the primary key (Id) for a direct database lookup
        // Returns null if no entity with this Id exists
        return await _dbSet.FindAsync(id);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — Get all entities from the table
    // -------------------------------------------------------------------------
    // Returns a list of all entities in the table.
    // Uses ToListAsync() to execute the query asynchronously.
    public async Task<IEnumerable<T>> GetAllAsync()
    {
        // ToListAsync() executes the query and returns all results as a List<T>
        // This loads all entities from the table into memory
        return await _dbSet.ToListAsync();
    }

    // -------------------------------------------------------------------------
    // AddAsync — Add a new entity to the database
    // -------------------------------------------------------------------------
    // Adds the entity to the EF Core change tracker.
    // The entity's Id will be auto-generated after calling SaveChangesAsync().
    public async Task AddAsync(T entity)
    {
        // Add() marks the entity as "Added" in the change tracker
        // The actual INSERT happens when SaveChangesAsync() is called
        await _dbSet.AddAsync(entity);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync — Update an existing entity in the database
    // -------------------------------------------------------------------------
    // Marks the entity as "Modified" in the EF Core change tracker.
    // The actual UPDATE happens when SaveChangesAsync() is called.
    public Task UpdateAsync(T entity)
    {
        // Update() marks the entity as "Modified" in the change tracker
        // EF Core will generate an UPDATE SQL statement when SaveChangesAsync() is called
        _dbSet.Update(entity);
        // Return a completed task since this method is synchronous
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // DeleteAsync — Delete an entity from the database
    // -------------------------------------------------------------------------
    // Marks the entity as "Deleted" in the EF Core change tracker.
    // The actual DELETE happens when SaveChangesAsync() is called.
    public Task DeleteAsync(T entity)
    {
        // Remove() marks the entity as "Deleted" in the change tracker
        // EF Core will generate a DELETE SQL statement when SaveChangesAsync() is called
        _dbSet.Remove(entity);
        // Return a completed task since this method is synchronous
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // SaveChangesAsync — Save all pending changes to the database
    // -------------------------------------------------------------------------
    // Commits all pending Add/Update/Delete operations to the database.
    // This is called after Add/Update/Delete to persist the changes.
    public async Task SaveChangesAsync()
    {
        // SaveChangesAsync() executes all pending INSERT/UPDATE/DELETE operations
        // It returns the number of entities affected
        await _context.SaveChangesAsync();
    }
}
