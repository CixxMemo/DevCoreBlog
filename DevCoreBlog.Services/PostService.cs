using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services;

/// <summary>Coordinates post validation, publication rules and persistence contracts.</summary>
public class PostService : IPostService, IPublicationSchedule, IWebhookPostService, IPublicFeedService, ISitemapPostReader, IRssPostReader
{
    private const int MaximumSlugAttempts = 100;
    // Post use cases depend on the domain persistence contract.
    private readonly IPostRepository _postRepository;
    private readonly IWebhookPostRepository _webhookRepository;
    private readonly IActiveCategoryLookup _activeCategoryLookup;
    private readonly PublicationTimeZone _publicationTimeZone;
    private readonly TimeProvider _timeProvider;
    private readonly PublicListCacheInvalidator _publicListCache;

    // Constructor receives the repository contract via dependency injection
    // The DI container (configured in Program.cs) provides the instance
    public PostService(
        IPostRepository postRepository,
        IWebhookPostRepository webhookRepository,
        IActiveCategoryLookup activeCategoryLookup,
        PublicationTimeZone publicationTimeZone,
        TimeProvider timeProvider,
        PublicListCacheInvalidator publicListCache)
    {
        // Store the injected repository for use in all service methods
        _postRepository = postRepository;
        _webhookRepository = webhookRepository;
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

    public Task<IReadOnlyList<SitemapPost>> GetSitemapPostsAsync(
        CancellationToken cancellationToken = default) =>
        _postRepository.GetSitemapPostsAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<TopReadPost>> GetMostReadPublicPostsAsync(
        CancellationToken cancellationToken = default) =>
        _postRepository.GetMostReadPublicPostsAsync(
            _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

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

    // Validate even non-HTTP callers before bounded persistence queries run.
    public Task<PublicPostPage> SearchPostsPagedAsync(string query, string? categorySlug, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize, query, categorySlug);
        return _postRepository.SearchPostsPagedAsync(query.Trim(), categorySlug, page, pageSize,
            _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public Task<PublicPostPage> GetPublishedPostsPagedAsync(int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize);
        return _postRepository.GetPublishedPostsPagedAsync(
            page, pageSize, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public Task<PublicPostPage> GetPostsByCategorySlugPagedAsync(string categorySlug, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        PublicListBounds.Validate(page, pageSize, category: categorySlug);
        return _postRepository.GetPostsByCategorySlugPagedAsync(
            categorySlug, page, pageSize, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    // Get related posts in the same category (excluding the current post)
    public Task<IReadOnlyList<PublicPostSummary>> GetRelatedPostsAsync(int currentPostId, int categoryId,
        CancellationToken cancellationToken = default)
    {
        return _postRepository.GetRelatedPostsAsync(
            currentPostId, categoryId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public Task<IReadOnlyList<RssPost>> GetRssPostsAsync(
        CancellationToken cancellationToken = default) =>
        _postRepository.GetRssPostsAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyList<PublicFeedPost>> GetLatestPublicPostsAsync(
        CancellationToken cancellationToken = default) =>
        _postRepository.GetLatestPublicPostsAsync(
            _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    // Public detail GETs increment one database column without saving a stale post.
    public async Task<int?> IncrementViewCountAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var currentCount = await _postRepository.IncrementVisibleViewCountAsync(
            id, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (currentCount is not null)
        {
            // Public lists display the count; invalidate after a successful write.
            await _publicListCache.InvalidateAsync(cancellationToken);
        }

        return currentCount;
    }

    // -------------------------------------------------------------------------
    // ADMIN METHODS (for admin panel CRUD operations)
    // -------------------------------------------------------------------------

    // Capture the same UTC instant for SQL status filtering and displayed state.
    public async Task<AdminPostListPage> GetAdminPostsPagedAsync(AdminPostQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var page = await _postRepository.GetAdminPostsPagedAsync(query with { Query = query.Query?.Trim() }, now, cancellationToken);
        var rows = page.Posts.Select(post => new AdminPostListItem(post,
            PostPublication.StateAt(post.IsActive, post.CategoryIsActive, post.IsPublished, post.PublishDate, now))).ToList().AsReadOnly();
        return new AdminPostListPage(rows, page.TotalCount);
    }

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
        errors.AddRange(PostAccessRules.Validate(post).Errors);

        // The legacy writer cannot accept document fields or keep their derivatives in sync.
        if (post.DocumentVersion is not null || post.DocumentJson is not null ||
            post.DocumentPlainText is not null || post.DocumentWordCount is not null ||
            post.DocumentReadingMinutes is not null)
            errors.Add(new(nameof(Post.Content), "JSON belge kaydı için belge kayıt akışını kullanın."));

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

    /// <summary>Validates site time before applying explicit draft, schedule or publish intent.</summary>
    public async Task<ContentValidationResult> PrepareEditorSaveAsync(
        Post post, PostSaveAction action, CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePostAsync(post, cancellationToken);
        if (!validation.IsValid) return validation;
        var stored = post.Id > 0 ? await _postRepository.GetByIdAsync(post.Id) : null;
        if (stored is { DocumentVersion: not null })
            return ContentValidationResult.Conflict(
                "Bu yazı JSON belge kullanıyor. Değişiklikleriniz korunuyor; belge editörüyle düzenleyin.");
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        switch (action)
        {
            case PostSaveAction.SaveDraft:
                post.IsPublished = false;
                break;
            case PostSaveAction.Schedule:
                if (post.PublishDate <= utcNow)
                    return ContentValidationResult.FromErrors([
                        new(nameof(Post.PublishDate), "Choose a future publish date to schedule this post.")]);
                post.IsPublished = true;
                break;
            case PostSaveAction.Publish:
                post.IsPublished = true;
                post.PublishDate = utcNow;
                break;
            case PostSaveAction.Save:
                if (stored is null)
                    return ContentValidationResult.FromErrors([
                        new("SaveAction", "Save changes requires an existing post.")]);
                post.IsPublished = stored.IsPublished;
                post.PublishDate = stored.PublishDate;
                break;
            default:
                return ContentValidationResult.FromErrors([
                    new("SaveAction", "Choose a valid save action.")]);
        }
        return ContentValidationResult.Success();
    }

    public async Task<ContentValidationResult> CreatePostAsync(
        Post post,
        CancellationToken cancellationToken = default,
        PostSaveAction? saveAction = null)
    {
        var validationResult = saveAction is { } action
            ? await PrepareEditorSaveAsync(post, action, cancellationToken)
            : await ValidatePostAsync(post, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        return (await CreateValidatedPostAsync(post, null, null, cancellationToken)).Validation;
    }

    /// <summary>Replays successful submissions before current validation or publication settings.</summary>
    public async Task<WebhookPostResult> CreateAsync(
        Post post, string key, string payloadHash, CancellationToken cancellationToken)
    {
        var receipt = await _webhookRepository.FindAsync(key, cancellationToken);
        if (receipt is not null)
        {
            return Replay(receipt, payloadHash);
        }

        var validation = await ValidatePostAsync(post, cancellationToken);
        if (!validation.IsValid)
        {
            return new(validation);
        }

        return await CreateValidatedPostAsync(post, key, payloadHash, cancellationToken);
    }

    // Normal and webhook creation share date, slug retry and cache invalidation rules.
    private async Task<WebhookPostResult> CreateValidatedPostAsync(
        Post post, string? key, string? payloadHash, CancellationToken cancellationToken)
    {
        post.CreatedDate = _timeProvider.GetUtcNow().UtcDateTime;
        post.UpdatedDate = null;
        var baseSlug = SlugGenerator.GenerateBase(post.Title, "post");
        for (var attempt = 0; attempt < MaximumSlugAttempts; attempt++)
        {
            post.Slug = SlugGenerator.Candidate(baseSlug, attempt);
            if (await _postRepository.SlugExistsAsync(post.Slug, cancellationToken))
            {
                continue;
            }

            if (key is not null && payloadHash is not null)
            {
                var result = await _webhookRepository.TryCreateAsync(
                    post, key, payloadHash, cancellationToken);
                if (result is null)
                {
                    continue;
                }

                if (result.Created)
                {
                    await _publicListCache.InvalidateAsync(cancellationToken);
                }
                return Replay(result.Receipt, payloadHash);
            }

            if (await _postRepository.TryCreateWithSlugAsync(post, cancellationToken))
            {
                await _publicListCache.InvalidateAsync(cancellationToken);
                return new(ContentValidationResult.Success(), WebhookPostSnapshot.FromPost(post));
            }
        }

        // A competing keyed request may have won while all slug candidates were occupied.
        if (key is not null && payloadHash is not null)
        {
            var receipt = await _webhookRepository.FindAsync(key, cancellationToken);
            if (receipt is not null)
            {
                return Replay(receipt, payloadHash);
            }
        }

        return new(ContentValidationResult.FromErrors(
        [
            new(nameof(Post.Title), "Could not reserve a unique address. Try another title.")
        ]));
    }

    private static WebhookPostResult Replay(WebhookReceipt receipt, string payloadHash) =>
        receipt.PayloadHash == payloadHash
            ? new(ContentValidationResult.Success(), WebhookPostSnapshot.FromReceipt(receipt))
            : new(ContentValidationResult.Success(), KeyConflict: true);

    // Update editable fields while preserving the existing public URL.
    public async Task<ContentValidationResult> UpdatePostAsync(
        Post post,
        CancellationToken cancellationToken = default,
        long? expectedEditVersion = null,
        PostSaveAction? saveAction = null)
    {
        var validationResult = saveAction is { } action
            ? await PrepareEditorSaveAsync(post, action, cancellationToken)
            : await ValidatePostAsync(post, cancellationToken);
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

        var loadedVersion = expectedEditVersion ?? existingPost.EditVersion;
        if (existingPost.DocumentVersion is not null || existingPost.DocumentJson is not null)
            return ContentValidationResult.Conflict(
                "Bu yazı JSON belge kullanıyor. Değişiklikleriniz korunuyor; belge editörüyle düzenleyin.");
        if (loadedVersion != existingPost.EditVersion)
        {
            return ContentValidationResult.Conflict(
                "This post changed since you opened it. Your edits are still here. Reload the current post before trying again.");
        }

        // Publication flags/dates and counters do not represent an editorial content revision.
        if (existingPost.Title != post.Title || existingPost.Summary != post.Summary ||
            existingPost.Content != post.Content || existingPost.Excerpt != post.Excerpt ||
            existingPost.ThumbnailUrl != post.ThumbnailUrl || existingPost.ThumbnailAlt != post.ThumbnailAlt ||
            existingPost.CategoryId != post.CategoryId || existingPost.ContentKind != post.ContentKind ||
            existingPost.AccessScope != post.AccessScope)
            existingPost.UpdatedDate = _timeProvider.GetUtcNow().UtcDateTime;

        existingPost.Title = post.Title;
        existingPost.Summary = post.Summary;
        existingPost.Content = post.Content;
        existingPost.ThumbnailUrl = post.ThumbnailUrl;
        existingPost.ThumbnailPublicId = post.ThumbnailPublicId;
        existingPost.ThumbnailWidth = post.ThumbnailWidth;
        existingPost.ThumbnailHeight = post.ThumbnailHeight;
        existingPost.ThumbnailAlt = post.ThumbnailAlt;
        existingPost.Excerpt = post.Excerpt;
        existingPost.IsPublished = post.IsPublished;
        existingPost.CategoryId = post.CategoryId;
        existingPost.IsActive = post.IsActive;
        existingPost.ContentKind = post.ContentKind;
        existingPost.AccessScope = post.AccessScope;

        existingPost.PublishDate = post.PublishDate;

        existingPost.EditVersion = checked(loadedVersion + 1);

        if (!await _postRepository.TrySaveVersionedEditAsync(
                existingPost, loadedVersion, cancellationToken))
        {
            return ContentValidationResult.Conflict(
                "This post changed since you opened it. Your edits are still here. Reload the current post before trying again.");
        }
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
