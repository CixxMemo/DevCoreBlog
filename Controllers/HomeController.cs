// =============================================================================
// HomeController.cs — Public-Facing Home Controller
// =============================================================================
// This controller handles the public (visitor-facing) pages of the blog.
// It contains actions for:
//   - Index: List of published posts on the home page
//   - Detail: Single post view by slug
//   - Category: Posts filtered by category slug
//   - Error: Error page with RequestId for debugging
//
// Architecture: This controller uses IPostService and ICategoryService
// for business logic, not ApplicationDbContext directly.
// This follows the N-Tier architecture pattern:
//   Controller → Service → Repository → Database
// =============================================================================

// Import MVC base classes and attributes
using Microsoft.AspNetCore.Mvc;
// Import the ErrorViewModel used by the Error action
using DevCoreBlog.Models;
using DevCoreBlog.Models.Public;
using DevCoreBlog.Routing;
using DevCoreBlog.Models.Seo;
// Import the Service interfaces for business logic
using DevCoreBlog.Services.Interfaces;
// Import the Post entity (used in Search action return type)
using DevCoreBlog.Core.Entities;
using Microsoft.AspNetCore.OutputCaching;

namespace DevCoreBlog.Controllers;

// Inherit from Controller for access to View(), HttpContext, etc.
public class HomeController : Controller
{
    // ---------------------------------------------------------------------------
    // DEPENDENCY INJECTION
    // ---------------------------------------------------------------------------
    // Private readonly fields to hold the injected services.
    // These services handle all business logic for posts and categories.
    private readonly IPostService _postService;
    private readonly ICategoryService _categoryService;
    private readonly PublicUrlBuilder _publicUrls;
    private readonly PageMetadataFactory _metadata;

    // Constructor receives services via dependency injection.
    // The DI container (configured in Program.cs) provides the instances.
    public HomeController(IPostService postService, ICategoryService categoryService, PublicUrlBuilder publicUrls, PageMetadataFactory metadata)
    {
        // Store the injected services for use in action methods
        _postService = postService;
        _categoryService = categoryService;
        _publicUrls = publicUrls;
        _metadata = metadata;
    }

    // ---------------------------------------------------------------------------
    // PUBLIC ACTIONS
    // ---------------------------------------------------------------------------

    // GET: /
    // Displays the public home page with a list of published blog posts.
    // Only published posts are shown, ordered by creation date (newest first).
    // Each post includes its related Category for display in the view.
    [OutputCache(PolicyName = "PublicLists")]
    public async Task<IActionResult> Index([FromQuery] PublicListInput input, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid) return InvalidListRequest();
        var result = await _postService.GetPublishedPostsPagedAsync(input.Page, input.PageSize, cancellationToken);
        if (input.Page > 1 && result.Posts.Count == 0) return NotFound();
        var mostRead = await _postService.GetMostReadPublicPostsAsync(cancellationToken);

        ViewData["Metadata"] = _metadata.Home(input.Page, input.PageSize);

        // Pass the list of posts to the view
        return View(new HomePageModel(result.Posts, mostRead, input.Page, input.PageSize, result.TotalCount));
    }

    // GET: /post/{slug}; visible aliases redirect without incrementing the counter.
    // Displays a single blog post identified by its slug.
    // Only published posts are accessible; unpublished or non-existent posts return 404.
    // The post's Category is eager-loaded for display in the view.
    //
    // Anonymous public GETs count page requests, not unique people.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Detail(
        string slug, CancellationToken cancellationToken,
        [FromServices] ISafeMarkdownRenderer markdownRenderer)
    {
        // Step 1: Get the post by slug from service layer
        var post = await _postService.GetPostBySlugAsync(slug);

        // If no matching post found, return 404 Not Found
        if (post == null)
        {
            return NotFound();
        }

        var redirect = RedirectToCanonicalPath(_publicUrls.PostPath(post.Slug));
        if (redirect is not null) return redirect;

        var viewCount = post.ViewCount;
        if (HttpMethods.IsGet(Request.Method) &&
            User.Identity?.IsAuthenticated != true)
        {
            var updatedCount = await _postService.IncrementViewCountAsync(
                post.Id, cancellationToken);
            if (updatedCount is null)
            {
                // The row may have been deleted or hidden after the public lookup.
                return NotFound();
            }

            viewCount = updatedCount.Value;
        }

        // Preserve the visible view count without replacing the filtered content snapshot.
        post.ViewCount = viewCount;

        var content = markdownRenderer.RenderDocument(post.Content);
        var relatedPosts = await _postService.GetRelatedPostsAsync(post.Id, post.CategoryId);

        ViewData["Metadata"] = _metadata.Article(post);

        // Pass the post to the Detail view
        ViewData["HideSidebar"] = true;
        ViewData["HideSearch"] = true;
        
        return View(new PostDetailModel(post, content, relatedPosts.ToList()));
    }

    // Invalid filters are 400; a valid request for an absent category/page is 404.
    [OutputCache(PolicyName = "PublicLists")]
    public async Task<IActionResult> Category(string slug, [FromQuery] PublicListInput input,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid || slug.Length > 200) return InvalidListRequest();
        var category = await _categoryService.GetActiveCategoryBySlugAsync(slug);
        if (category is null) return NotFound();
        var redirect = RedirectToCanonicalPath(_publicUrls.CategoryPath(category.Slug));
        if (redirect is not null) return redirect;
        var result = await _postService.GetPostsByCategorySlugPagedAsync(slug, input.Page, input.PageSize, cancellationToken);
        if (input.Page > 1 && result.Posts.Count == 0) return NotFound();
        ViewData["Metadata"] = _metadata.Category(category, input.Page, input.PageSize);
        return View(new PublicListPageModel(result,
            new PublicPagingModel("Category", slug, null, null, input.Page, input.PageSize, result.TotalCount), category.Name, []));
    }

    [Route("ara")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Search([FromQuery] PublicListInput input, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return InvalidListRequest();
        var term = input.Query?.Trim() ?? string.Empty;
        var categorySlug = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        var category = categorySlug is null ? null : await _categoryService.GetActiveCategoryBySlugAsync(categorySlug);
        if (categorySlug is not null && category is null) return NotFound();
        var result = await _postService.SearchPostsPagedAsync(term, categorySlug, input.Page, input.PageSize, cancellationToken);
        if (input.Page > 1 && result.Posts.Count == 0) return NotFound();
        ViewData["SearchQuery"] = term;
        ViewData["SearchCategory"] = categorySlug;
        ViewData["SearchPageSize"] = input.PageSize;
        var categories = await _categoryService.GetActiveCategoriesAsync();
        ViewData["Metadata"] = _metadata.Search(term, categorySlug, input.Page, input.PageSize);
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        return View(new PublicListPageModel(result,
            new PublicPagingModel("Search", null, term, categorySlug, input.Page, input.PageSize, result.TotalCount),
            category?.Name, categories.ToList()));
    }

    // Check visibility first, preserve the raw query and use only an escaped local route target.
    private IActionResult? RedirectToCanonicalPath(string path) =>
        string.Equals(Request.Path.Value, PathString.FromUriComponent(path).Value, StringComparison.Ordinal)
            ? null : LocalRedirectPermanent(path + Request.QueryString.ToUriComponent());

    private BadRequestObjectResult InvalidListRequest() =>
        BadRequest("Use a search term up to 100 characters, page 1–1000, and page size 9, 18, or 27.");

    // -------------------------------------------------------------------------
    // API ENDPOINTS (for Phase 3 Terminal CLI)
    // -------------------------------------------------------------------------
    // GET: /api/categories
    // Lightweight JSON endpoint for the JS terminal 'ls' command to consume.
    [HttpGet("api/categories")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ApiCategories()
    {
        var categories = await _categoryService.GetActiveCategoriesAsync();
        var result = categories.Select(c => new { slug = c.Slug, name = c.Name });
        return Json(result);
    }

    // GET: /Home/Error
    // Displays the error page with a RequestId for debugging.
    // [ResponseCache] prevents caching so each error gets a fresh trace ID.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }
}
