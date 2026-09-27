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
//   - CreatedDate is set from TimeProvider in UTC on creation
//   - Existing slugs stay stable when a title changes
// =============================================================================

using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services;

// Service class for Post-related business logic
// Implements IPostService interface
public class PostService : IPostService, IPublicationSchedule
{
    private const int MaximumSlugAttempts = 100;
    // Private readonly field to hold the injected PostRepository
    private readonly PostRepository _postRepository;
    private readonly IActiveCategoryLookup _activeCategoryLookup;
    private readonly PublicationTimeZone _publicationTimeZone;
    private readonly TimeProvider _timeProvider;
    private readonly PublicListCacheInvalidator _publicListCache;

    // Constructor receives PostRepository via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public PostService(
        PostRepository postRepository,
        IActiveCategoryLookup activeCategoryLookup,
        PublicationTimeZone publicationTimeZone,
        TimeProvider timeProvider,
        PublicListCacheInvalidator publicListCache)
    {
        // Store the injected repository for use in all service methods
        _postRepository = postRepository;
        _activeCategoryLookup = activeCategoryLookup;
        _publicationTimeZone = publicationTimeZone;
        _timeProvider = timeProvider;
        _publicListCache = publicListCache;
    }

    // Cache policy needs only this boundary; visitor controllers use IPostService.
    public Task<DateTime?> GetNextScheduledPublicationAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        _postRepository.GetNextScheduledPublicationAsync(utcNow, cancellationToken);

    // -------------------------------------------------------------------------
    // PUBLIC METHODS (for visitor-facing pages)
    // -------------------------------------------------------------------------

    // Get all published posts with their Category (ordered by newest first)
    // Capture one UTC instant for each public query.
    public async Task<IEnumerable<Post>> GetPublishedPostsAsync()
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPublishedPostsAsync(_timeProvider.GetUtcNow().UtcDateTime);
    }

    // Get a single post by its slug (only if published)
    // The repository applies the shared public visibility rule at the current UTC instant.
    public async Task<Post?> GetPostBySlugAsync(string slug)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPostBySlugAsync(slug, _timeProvider.GetUtcNow().UtcDateTime);
    }

    // Get all posts in a category (by category slug, only published posts)
    // The repository applies the shared public visibility rule at the current UTC instant.
    public async Task<IEnumerable<Post>> GetPostsByCategorySlugAsync(string categorySlug)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.GetPostsByCategorySlugAsync(
            categorySlug, _timeProvider.GetUtcNow().UtcDateTime);
    }

    // Search posts by title or content (only published posts)
    // Business rule: Only return posts where IsActive = true
    public async Task<IEnumerable<Post>> SearchPostsAsync(string query)
    {
        // Delegate to repository — no additional business logic needed
        return await _postRepository.SearchPostsAsync(query, _timeProvider.GetUtcNow().UtcDateTime);
    }

    // Keep public data uncached until F18 defines safe invalidation and expiry.
    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPublishedPostsPagedAsync(int page, int pageSize)
    {
        return await _postRepository.GetPublishedPostsPagedAsync(
            page, pageSize, _timeProvider.GetUtcNow().UtcDateTime);
    }

    // Get published posts by category with pagination
    public async Task<(IEnumerable<Post> Posts, int TotalCount)> GetPostsByCategorySlugPagedAsync(string categorySlug, int page, int pageSize)
    {
        return await _postRepository.GetPostsByCategorySlugPagedAsync(
            categorySlug, page, pageSize, _timeProvider.GetUtcNow().UtcDateTime);
    }

    // Get related posts in the same category (excluding the current post)
    public async Task<IEnumerable<Post>> GetRelatedPostsAsync(int currentPostId, int categoryId)
    {
        return await _postRepository.GetRelatedPostsAsync(
            currentPostId, categoryId, _timeProvider.GetUtcNow().UtcDateTime);
    }

    public Task<IEnumerable<Post>> GetLatestPublicPostsAsync() =>
        _postRepository.GetLatestPublicPostsAsync(_timeProvider.GetUtcNow().UtcDateTime);

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

        // The visible list also displays this count; keep it current after a detail hit.
        await _publicListCache.InvalidateAsync();

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
    public async Task<ContentValidationResult> ValidatePostAsync(
        Post post,
        CancellationToken cancellationToken = default)
    {
        PostContentRules.Normalize(post);
        var result = PostContentRules.Validate(post);
        var errors = result.Errors.ToList();

        // Normalize the shared service input before uploads or persistence.
        if (_publicationTimeZone.TryConvertToUtc(
                post.PublishDate, out var publishDateUtc, out var dateError))
        {
            post.PublishDate = publishDateUtc;
        }
        else
        {
            errors.Add(new(nameof(Post.PublishDate), dateError ?? "Publish date is invalid."));
        }

        if (post.CategoryId > 0 &&
            !await _activeCategoryLookup.IsActiveCategoryAsync(
                post.CategoryId,
                cancellationToken))
        {
            errors.Add(new(
                nameof(Post.CategoryId),
                "Select an active category."));
        }

        return ContentValidationResult.FromErrors(errors);
    }

    public async Task<ContentValidationResult> CreatePostAsync(
        Post post,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await ValidatePostAsync(post, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        // BUSINESS RULE: Set creation time to UTC (required for PostgreSQL timestamp with time zone)
        // This ensures consistent timestamps across different time zones
        post.CreatedDate = _timeProvider.GetUtcNow().UtcDateTime;

        var baseSlug = SlugGenerator.GenerateBase(post.Title, "post");
        for (var attempt = 0; attempt < MaximumSlugAttempts; attempt++)
        {
            post.Slug = SlugGenerator.Candidate(baseSlug, attempt);
            if (await _postRepository.SlugExistsAsync(post.Slug, cancellationToken))
            {
                continue;
            }

            if (await _postRepository.TryCreateWithSlugAsync(post, cancellationToken))
            {
                await _publicListCache.InvalidateAsync(cancellationToken);
                return ContentValidationResult.Success();
            }
        }

        return ContentValidationResult.FromErrors(
        [
            new(nameof(Post.Title), "Could not reserve a unique address. Try another title.")
        ]);
    }

    // Update editable fields while preserving the existing public URL.
    public async Task<ContentValidationResult> UpdatePostAsync(
        Post post,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await ValidatePostAsync(post, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        // Get the existing post from database to avoid overwriting CreatedDate/ViewCount
        var existingPost = await _postRepository.GetByIdAsync(post.Id);
        if (existingPost == null)
        {
            return ContentValidationResult.FromErrors(
            [
                new(nameof(Post.Id), "Post not found.")
            ]);
        }

        existingPost.Title = post.Title;
        existingPost.Summary = post.Summary;
        existingPost.Content = post.Content;
        existingPost.ThumbnailUrl = post.ThumbnailUrl;
        existingPost.Excerpt = post.Excerpt;
        existingPost.IsPublished = post.IsPublished;
        existingPost.CategoryId = post.CategoryId;
        existingPost.IsActive = post.IsActive;

        existingPost.PublishDate = post.PublishDate;

        // Update the post in the database via repository
        await _postRepository.UpdateAsync(existingPost);

        // Commit the transaction to the database
        await _postRepository.SaveChangesAsync();
        await _publicListCache.InvalidateAsync(cancellationToken);

        return ContentValidationResult.Success();
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
            await _publicListCache.InvalidateAsync();

        }
    }
}
