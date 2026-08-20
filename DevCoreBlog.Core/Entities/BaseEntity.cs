// =============================================================================
// BaseEntity.cs — Base Class for All Database Entities
// =============================================================================
// This abstract class provides common properties that all database entities share.
// By inheriting from BaseEntity, entities automatically get:
//   - Id: Primary key (auto-incremented integer)
//   - CreatedDate: Timestamp of when the record was created
//   - IsActive: Flag to soft-delete or deactivate records without removing them
//
// Why use a base class?
//   1. Avoids code duplication (DRY principle - Don't Repeat Yourself)
//   2. Ensures consistency across all entities
//   3. Makes it easier to add common fields later (e.g., UpdatedDate, CreatedBy)
//
// Note: This is an "abstract" class, meaning you cannot create an instance of it
// directly. It's only meant to be inherited by other classes like Post, Category.
// =============================================================================

namespace DevCoreBlog.Core.Entities;

// Abstract base class — all database entities must inherit from this
public abstract class BaseEntity
{
    // Primary key — auto-incremented integer by EF Core convention
    // Every table in the database will have an "Id" column
    public int Id { get; set; }

    // When this record was created — always set to DateTime.UtcNow in the service layer
    // This replaces the old "CreatedAt" property from Post
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // Soft-delete flag — when false, the record is hidden from public view
    // This replaces the old "IsPublished" property from Post
    // For categories, this can indicate if the category is active or archived
    public bool IsActive { get; set; } = true;
}
