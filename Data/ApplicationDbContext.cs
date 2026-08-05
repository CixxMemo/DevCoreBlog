// =============================================================================
// ApplicationDbContext.cs — Entity Framework Core Database Context
// =============================================================================
// This is the main database context class that EF Core uses to interact
// with the PostgreSQL database. It defines which entity sets (tables) exist
// and is registered in Program.cs via AddDbContext<ApplicationDbContext>().
//
// Each DbSet<T> property corresponds to a table in the database:
//   - Posts      → "Posts" table (blog posts)
//   - Categories → "Categories" table (blog categories)
// =============================================================================

// Import the model classes (Post, Category)
using DevCoreBlog.Models;
// Import EF Core base classes
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data;

// Inherit from DbContext — the core EF Core class for database operations
public class ApplicationDbContext : DbContext
{
    // Constructor passes the options (connection string, provider, etc.)
    // to the base DbContext. These options are configured in Program.cs.
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // Represents the "Posts" table — use _context.Posts to query/insert/update/delete posts
    public DbSet<Post> Posts { get; set; }

    // Represents the "Categories" table — use _context.Categories to query/manage categories
    public DbSet<Category> Categories { get; set; }
}
