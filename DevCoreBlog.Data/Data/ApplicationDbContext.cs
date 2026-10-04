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

// Import the entity classes (Post, Category) from Core/Entities
using DevCoreBlog.Core.Entities;
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

    public DbSet<WebhookReceipt> WebhookReceipts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var receipt = modelBuilder.Entity<WebhookReceipt>();
        receipt.HasKey(item => item.Key);
        receipt.Property(item => item.Key).HasMaxLength(128).HasAnnotation("Relational:Collation", "C");
        receipt.Property(item => item.PayloadHash).HasMaxLength(64);
        receipt.Property(item => item.Title).HasMaxLength(200);
        receipt.HasOne(item => item.Post).WithMany()
            .HasForeignKey(item => item.PostId).OnDelete(DeleteBehavior.SetNull);

        // The database must reject category deletion while posts still reference it.
        // This also closes the race between the service's early check and DELETE.
        modelBuilder.Entity<Post>()
            .HasOne(post => post.Category)
            .WithMany(category => category.Posts)
            .HasForeignKey(post => post.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Post>()
            .HasIndex(post => post.Slug)
            .IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(category => category.Slug)
            .IsUnique();

        // Measured public top-N reads share publication order; inactive/draft rows need no entry.
        modelBuilder.Entity<Post>()
            .HasIndex(post => new { post.PublishDate, post.Id })
            .IsDescending(true, false)
            .HasFilter("\"IsActive\" AND \"IsPublished\"");

        // Inventory paging uses creation order independently of publication state.
        modelBuilder.Entity<Post>()
            .HasIndex(post => new { post.CreatedDate, post.Id })
            .IsDescending(true, false);

        modelBuilder.Entity<Post>()
            .Property(post => post.EditVersion)
            .IsConcurrencyToken();

        modelBuilder.Entity<Category>()
            .Property(category => category.EditVersion)
            .IsConcurrencyToken();
    }
}
