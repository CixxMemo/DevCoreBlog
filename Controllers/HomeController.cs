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

    // Constructor receives services via dependency injection.
    // The DI container (configured in Program.cs) provides the instances.
    public HomeController(IPostService postService, ICategoryService categoryService)
    {
        // Store the injected services for use in action methods
        _postService = postService;
        _categoryService = categoryService;
    }

    // ---------------------------------------------------------------------------
    // PUBLIC ACTIONS
    // ---------------------------------------------------------------------------

    // GET: /
    // Displays the public home page with a list of published blog posts.
    // Only published posts are shown, ordered by creation date (newest first).
    // Each post includes its related Category for display in the view.
    [OutputCache(PolicyName = "PublicLists")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        const int pageSize = 9;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var result = await _postService.GetPublishedPostsPagedAsync(page, pageSize);
        if (page > 1 && (long)(page - 1) * pageSize >= result.TotalCount) return NotFound();
        var mostRead = await _postService.GetMostReadPublicPostsAsync(cancellationToken);

        // Set Open Graph (OG) meta tags for social media sharing (home page)
        ViewBag.OgTitle = "DevCoreBlog - ASP.NET Core and Modern Web Development";
        ViewBag.OgDescription = "DevCoreBlog - Technical articles on ASP.NET Core, C#, Entity Framework Core, and modern web development.";
        ViewBag.OgType = "website";
        ViewBag.OgUrl = "/";

        // Pass the list of posts to the view
        return View(new HomePageModel(result.Posts.ToList(), mostRead, page, pageSize, result.TotalCount));
    }

    // GET: /yazi/{slug}
    // Displays a single blog post identified by its slug.
    // Only published posts are accessible; unpublished or non-existent posts return 404.
    // The post's Category is eager-loaded for display in the view.
    //
    // Anonymous public GETs count page requests, not unique people.
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Detail(
        string slug, CancellationToken cancellationToken)
    {
        // Step 1: Get the post by slug from service layer
        var post = await _postService.GetPostBySlugAsync(slug);

        // If no matching post found, return 404 Not Found
        if (post == null)
        {
            return NotFound();
        }

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

        // Step 3: Calculate reading time based on word count
        // Average reading speed: 200 words per minute (standard for technical content)
        // Formula: ReadingTime = WordCount / 200 (rounded up to nearest minute)
        // Render the public-filtered snapshot, not the administrative row reloaded for the counter.
        var wordCount = post.Content.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries
        ).Length;

        // Calculate minutes (minimum 1 minute for very short posts)
        var readingTimeMinutes = Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));

        // Step 4: Pass data to the view via ViewBag
        // ViewBag is a dynamic container for passing extra data from Controller to View
        ViewBag.ViewCount = viewCount;
        ViewBag.ReadingTime = readingTimeMinutes;
        
        // Fetch related posts and pass to ViewBag
        ViewBag.RelatedPosts = await _postService.GetRelatedPostsAsync(post.Id, post.CategoryId);

        // Step 5: Set Open Graph (OG) meta tags for social media sharing
        // These values are used by _Layout.cshtml to generate <meta property="og:..."> tags
        // When someone shares this post on Facebook/Twitter/LinkedIn, these values appear
        ViewBag.OgTitle = post.Title;
        ViewBag.OgDescription = post.Summary;
        ViewBag.OgType = "article";
        ViewBag.OgUrl = $"/yazi/{post.Slug}";

        // Pass the post to the Detail view
        ViewData["HideSidebar"] = true;
        ViewData["HideSearch"] = true;
        
        return View(post);
    }

    // GET: /kategori/{slug}
    // Displays all published posts in a specific category (identified by slug).
    // If the category doesn't exist, returns 404.
    // The category name is passed via ViewBag for display in the view.
    [OutputCache(PolicyName = "PublicLists")]
    public async Task<IActionResult> Category(string slug, int page = 1)
    {
        Response.Headers.CacheControl = "no-store";
        // First, get the category by slug from service layer
        var category = await _categoryService.GetActiveCategoryBySlugAsync(slug);

        // If category not found, return 404 Not Found
        if (category == null)
        {
            return NotFound();
        }

        const int pageSize = 9;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var result = await _postService.GetPostsByCategorySlugPagedAsync(slug, page, pageSize);
        if (page > 1 && (long)(page - 1) * pageSize >= result.TotalCount) return NotFound();

        // Pass the category name and slug to the view via ViewBag
        ViewBag.TotalCount = result.TotalCount;
        ViewBag.CategoryName = category.Name;
        ViewBag.CategorySlug = category.Slug;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)result.TotalCount / pageSize);

        // Set Open Graph (OG) meta tags for social media sharing
        ViewBag.OgTitle = $"{category.Name} Category - DevCoreBlog";
        ViewBag.OgDescription = $"All articles in the {category.Name} category on DevCoreBlog.";
        ViewBag.OgType = "website";
        ViewBag.OgUrl = $"/kategori/{category.Slug}";

        // Pass the list of posts to the Category view
        return View(result.Posts.ToList());
    }

    // -------------------------------------------------------------------------
    // SEARCH — Search posts by title or content
    // -------------------------------------------------------------------------
    // GET: /ara?query=aspnet
    // Displays search results for posts matching the query string.
    // Searches in both Title and Content fields (case-insensitive).
    // Only published posts are returned.
    //
    // How it works:
    //   1. User types in the search box (navbar)
    //   2. Form submits to /ara?query=...
    //   3. Controller calls PostService.SearchPostsAsync(query)
    //   4. Service delegates to PostRepository.SearchPostsAsync(query)
    //   5. Repository filters posts where Title OR Content contains the query
    //   6. Results are displayed in Search.cshtml view
    [Route("ara")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Search(string query)
    {
        // Guard clause: if query is empty or whitespace, return empty results
        if (string.IsNullOrWhiteSpace(query))
        {
            ViewBag.SearchQuery = "";
            ViewBag.OgTitle = "Search - DevCoreBlog";
            ViewBag.OgDescription = "Search articles on DevCoreBlog.";
            return View(Enumerable.Empty<Post>());
        }

        // Delegate to service layer — business logic is in PostService
        var posts = await _postService.SearchPostsAsync(query);

        // Pass the search query to the view for display ("Results for: ...")
        ViewBag.SearchQuery = query;

        // Set Open Graph (OG) meta tags for social media sharing
        ViewBag.OgTitle = $"\"{query}\" Search Results - DevCoreBlog";
        ViewBag.OgDescription = $"Search results for \"{query}\" on DevCoreBlog.";
        ViewBag.OgType = "website";
        ViewBag.OgUrl = $"/ara?query={Uri.EscapeDataString(query)}";

        // Pass the list of matching posts to the Search view
        return View(posts);
    }

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
