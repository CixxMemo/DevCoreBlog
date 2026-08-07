// =============================================================================
// IRepository.cs — Generic Repository Interface
// =============================================================================
// This interface defines the standard database operations that every repository
// must implement. It uses generics (<T>) so it can work with any entity type
// (Post, Category, etc.) without duplicating code.
//
// What is an interface?
//   - An interface is a "contract" — it says WHAT methods a class must have,
//     but not HOW they work. The actual implementation is in GenericRepository.cs.
//   - Benefits:
//     1. Loose coupling — Controllers depend on IRepository<T>, not the concrete class
//     2. Testability — You can create a "fake" repository for unit testing
//     3. Flexibility — You can swap implementations without changing the controller
//
// Why generic (<T>)?
//   - Instead of writing separate methods for Post, Category, etc., we write them once.
//   - T is a placeholder for the entity type. When you use IRepository<Post>, T becomes Post.
//
// Note: "where T : BaseEntity" means T must be a class that inherits from BaseEntity.
// This ensures all entities have Id, CreatedDate, and IsActive properties.
// =============================================================================

using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Interfaces;

// Generic repository interface — T must inherit from BaseEntity
public interface IRepository<T> where T : BaseEntity
{
    // Get a single entity by its Id
    // Returns null if not found
    Task<T?> GetByIdAsync(int id);

    // Get all entities from the table
    // Returns an empty list if no records exist
    Task<IEnumerable<T>> GetAllAsync();

    // Add a new entity to the database
    // The entity's Id will be auto-generated after saving
    Task AddAsync(T entity);

    // Update an existing entity in the database
    // The entity must already exist (have a valid Id)
    Task UpdateAsync(T entity);

    // Delete an entity from the database
    // This is a "hard delete" — the record is permanently removed
    Task DeleteAsync(T entity);

    // Save all pending changes to the database
    // This is called after Add/Update/Delete to commit the transaction
    Task SaveChangesAsync();
}
