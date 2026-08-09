// =============================================================================
// PostService.cs — Post Business Logic Layer
// =============================================================================
// This class implements IPostService and contains all business logic for blog posts.
// It sits between the Controller and the Repository layer.
//
// What is the Service Layer?
//   - The Service layer contains business logic (rules, validations, transformations).
//   - It coordinates between the Controller and the Repository.
//   - Example flow:
//     1. Controller receives a request (e.g., "Create a new post")
//     2. Controller calls PostService.CreatePostAsync(post)
//     3. PostService applies business rules (generate slug, set date)
//     4. PostService calls PostRepository.AddAsync(post) to save to database
//     5. PostService calls PostRepository.SaveChangesAsync() to commit
//     6. Controller receives the result and returns a view/redirect
//
// Why not put business logic in the Controller?
//   - Controllers should only handle HTTP concerns (requests, responses, routing).
//   - Business logic should be in Services for:
//     1. Reusability — Multiple controllers can use the same service
//     2. Testability — Services can be unit tested without HTTP context
//     3. Separation of Concerns — Each layer has one responsibility
//
// Business Rules in PostService:
//   - Slug is auto-generated from Title (using SlugGenerator)
//   - CreatedDate is set to DateTime.UtcNow on creation
//   - Slug is regenerated on update (in case Title changed)
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Helpers;
using DevCoreBlog.Repositories;
using DevCoreBlog.Services.Interfaces;

namespace DevCoreBlog.Services;

// Service class for Post-related business logic
// Implements IPostService interface
public class PostService : IPostService
{
    // Private readonly field to hold the injected PostRepository
    private readonly PostRepository _postRepository;

    // Constructor receives PostRepository via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public PostService(PostRepository postRepository)
    {
        // Store the injected repository for use in all service methods
        _postRepository = postRepository;
    }

    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all published posts with their Category (ordered by newest first)
    // Business rule: Only return posts where IsActive = true
    public async Task<IEnumerable<Post>> GetPublishedPostsAsync()
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPublishedPostsAsync();
    }

    // Get a single post by its slug (only if published)
    // Business rule: Only return posts where IsActive = true
    public async Task<Post?> GetPostBySlugAsync(string slug)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPostBySlugAsync(slug);
    }

    // Get all posts in a category (by category slug, only published posts)
    // Business rule: Only return posts where IsActive = true
    public async Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPostsByCategorySlugAsync(categorySlug);
    }

    // Search posts by title or content (only published posts)
    // Business rule: Only return posts where IsActive = true
    public async Task<IEnumerable<Post>> SearchPostsAsync(string query)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.SearchPostsAsync(query);
    }

    // Get published posts with pagination
    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPublishedPostsPagedAsync(int page, int pageSize)
    {
        return await _postRepository.GetPublishedPostsPagedAsync(page, pageSize);
    }

    // Get published posts by category with pagination
    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPostsByCategorySlugPagedAsync(string categorySlug, int page, int pageSize)
    {
        return await _postRepository.GetPostsByCategorySlugPagedAsync(categorySlug, page, pageSize);
    }

    // Get related posts in the same category (excluding the current post)
    public async Task<IEnumerable<Post>> GetRelatedPostsAsync(int currentPostId, int categoryId)
    {
        return await _postRepository.GetRelatedPostsAsync(currentPostId, categoryId);
    }

    // -------------------------------------------------------------------------
    // VIEW COUNT — Increment when Detail page is visited
    // -------------------------------------------------------------------------
    // This method increments the ViewCount property of a post by 1.
    // It is called by HomeController.Detail() every time a visitor opens a post.
    //
    // Why in the Service layer?
    //   - The Service layer coordinates between Controller and Repository.
    //   - The Controller shouldn't directly modify database entities.
    //   - The Service encapsulates the "get → increment → save" logic.
    //
    // Returns the updated Post (with new ViewCount) so the Controller can use it.
    // Returns null if the post doesn't exist.
    public async Task<Post?> IncrementViewCountAsync(int id)
    {
        // Step 1: Get the post from the database by its Id
        var post = await _postRepository.GetByIdAsync(id);

        // If post doesn't exist, return null (Controller will handle 404)
        if (post == null)
        {
            return null;
        }

        // Step 2: Increment the ViewCount by 1
        post.ViewCount++;

        // Step 3: Update the post in the database
        await _postRepository.UpdateAsync(post);

        // Step 4: Commit the transaction to the database
        await _postRepository.SaveChangesAsync();

        // Return the updated post (with new ViewCount)
        return post;
    }

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Get all posts (including unpublished) for admin listing
    // Business rule: Return all posts regardless of IsActive status
    // This method uses GetAllPostsWithCategoryAsync to eager-load Category navigation property
    public async Task<IEnumerable<Post>> GetAllPostsAsync()
    {
        // Delegate to repository — uses GetAllPostsWithCategoryAsync to include Category
        return await _postRepository.GetAllPostsWithCategoryAsync();
    }

    // Get a single post by its Id (for admin edit form)
    public async Task<Post?> GetPostByIdAsync(int id)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetByIdAsync(id);
    }

    // Create a new post (handles slug generation and date setting)
    // Business rules:
    //   1. Slug is auto-generated from Title (using SlugGenerator)
    //   2. CreatedDate is set to DateTime.UtcNow
    //   3. IsActive defaults to true (from BaseEntity)
    public async Task CreatePostAsync(Post post)
    {
        // BUSINESS RULE: Auto-generate URL-friendly slug from the post title
        // Example: "ASP.NET Core ile Blog Yazma" → "asp-net-core-ile-blog-yazma"
        post.Slug = SlugGenerator.Generate(post.Title);

        // BUSINESS RULE: Set creation time to UTC (required for PostgreSQL timestamp with time zone)
        // This ensures consistent timestamps across different time zones
        post.CreatedDate = DateTime.UtcNow;

        // BUSINESS RULE: Ensure PublishDate is in UTC (form bindings are usually Unspecified Local time)
        if (post.PublishDate.Kind == DateTimeKind.Unspecified)
        {
            post.PublishDate = DateTime.SpecifyKind(post.PublishDate, DateTimeKind.Local).ToUniversalTime();
        }
        else
        {
            post.PublishDate = post.PublishDate.ToUniversalTime();
        }

        // Add the post to the database via repository
        await _postRepository.AddAsync(post);

        // Commit the transaction to the database
        await _postRepository.SaveChangesAsync();
    }

    // Update an existing post (handles slug regeneration)
    // Business rule: Slug is regenerated in case the Title was changed
    public async Task UpdatePostAsync(Post post)
    {
        // Get the existing post from database to avoid overwriting CreatedDate/ViewCount
        var existingPost = await _postRepository.GetByIdAsync(post.Id);
        if (existingPost == null)
            return;

        // BUSINESS RULE: Regenerate slug in case the title was changed
        // This ensures the slug always matches the current title
        existingPost.Title = post.Title;
        existingPost.Slug = SlugGenerator.Generate(post.Title);
        existingPost.Summary = post.Summary;
        existingPost.Content = post.Content;
        existingPost.CategoryId = post.CategoryId;
        existingPost.IsActive = post.IsActive;

        // Ensure PublishDate is in UTC
        if (post.PublishDate.Kind == DateTimeKind.Unspecified)
        {
            existingPost.PublishDate = DateTime.SpecifyKind(post.PublishDate, DateTimeKind.Local).ToUniversalTime();
        }
        else
        {
            existingPost.PublishDate = post.PublishDate.ToUniversalTime();
        }

        // Update the post in the database via repository
        await _postRepository.UpdateAsync(existingPost);

        // Commit the transaction to the database
        await _postRepository.SaveChangesAsync();
    }

    // Delete a post by its Id
    public async Task DeletePostAsync(int id)
    {
        // Get the post by Id from repository
        var post = await _postRepository.GetByIdAsync(id);

        // If post exists, delete it
        if (post != null)
        {
            await _postRepository.DeleteAsync(post);
            await _postRepository.SaveChangesAsync();
        }
    }
}
