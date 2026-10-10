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

        modelBuilder.Entity<Post>().Property(post => post.ThumbnailPublicId).HasMaxLength(255);
        modelBuilder.Entity<Post>().Property(post => post.ThumbnailAlt).HasMaxLength(300);

        // Explicit legacy mapping preserves existing visibility without guessing a content kind.
        modelBuilder.Entity<Post>().Property(post => post.ContentKind).HasDefaultValue(PostContentKind.Unclassified);
        modelBuilder.Entity<Post>().Property(post => post.AccessScope).HasDefaultValue(PostAccessScope.Public);
        modelBuilder.Entity<Post>().ToTable(table =>
        {
            table.HasCheckConstraint("CK_Posts_ContentKind", "\"ContentKind\" IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_Posts_AccessScope", "\"AccessScope\" IN (0, 1)");
            table.HasCheckConstraint("CK_Posts_NewsletterAccess", "\"ContentKind\" <> 4 OR \"AccessScope\" = 1");
            // Explicit NOT NULL terms prevent SQL's unknown result from admitting a partial set.
            table.HasCheckConstraint("CK_Posts_DocumentFacts", """
                ("DocumentVersion" IS NULL AND "DocumentJson" IS NULL AND "DocumentPlainText" IS NULL
                    AND "DocumentWordCount" IS NULL AND "DocumentReadingMinutes" IS NULL)
                OR
                ("DocumentVersion" IS NOT NULL AND "DocumentJson" IS NOT NULL AND "DocumentPlainText" IS NOT NULL
                    AND "DocumentWordCount" IS NOT NULL AND "DocumentReadingMinutes" IS NOT NULL
                    AND "DocumentVersion" = 1 AND octet_length("DocumentJson") BETWEEN 1 AND 1048576
                    AND octet_length("DocumentPlainText") <= 1048576
                    AND "DocumentWordCount" BETWEEN 0 AND 200000
                    AND "DocumentReadingMinutes" = ("DocumentWordCount" + 199) / 200)
                """);
        });

        modelBuilder.Entity<Post>()
            .Property(post => post.EditVersion)
            .IsConcurrencyToken();

        modelBuilder.Entity<Category>()
            .Property(category => category.EditVersion)
            .IsConcurrencyToken();
    }
}
