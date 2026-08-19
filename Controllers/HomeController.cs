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

// Import for generating a trace identifier on error pages
using System.Diagnostics;
// Import MVC base classes and attributes
using Microsoft.AspNetCore.Mvc;
// Import the ErrorViewModel used by the Error action
using DevCoreBlog.Models;
// Import the Service interfaces for business logic
using DevCoreBlog.Services.Interfaces;
// Import the Post entity (used in Search action return type)
using DevCoreBlog.Core.Entities;
// Import Output Caching for performance
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
    [OutputCache(Duration = 60)]
    public async Task<IActionResult> Index(int page = 1)
    {
        int pageSize = 9;
        var result = await _postService.GetPublishedPostsPagedAsync(page, pageSize);

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)result.TotalCount / pageSize);

        // Set Open Graph (OG) meta tags for social media sharing (home page)
        ViewBag.OgTitle = "DevCoreBlog - ASP.NET Core ve Modern Web Geliştirme";
        ViewBag.OgDescription = "DevCoreBlog - ASP.NET Core, C#, Entity Framework Core ve modern web geliştirme hakkında teknik yazılar. Senior ve Junior geliştiriciler için eğitim içerikleri.";
        ViewBag.OgType = "website";
        ViewBag.OgUrl = "/";

        // Pass the list of posts to the view
        return View(result.Posts.ToList());
    }

    // GET: /yazi/{slug}
    // Displays a single blog post identified by its slug.
    // Only published posts are accessible; unpublished or non-existent posts return 404.
    // The post's Category is eager-loaded for display in the view.
    //
    // Side Effect: Increments the ViewCount by 1 every time this action is called.
    // This provides a simple analytics metric for post popularity.
    [OutputCache(Duration = 60)]
    public async Task<IActionResult> Detail(string slug)
    {
        // Step 1: Get the post by slug from service layer
        var post = await _postService.GetPostBySlugAsync(slug);

        // If no matching post found, return 404 Not Found
        if (post == null)
        {
            return NotFound();
        }

        // Step 2: Increment the view count (tracks how many times this post was viewed)
        // The service handles the "get → increment → save" logic
        var updatedPost = await _postService.IncrementViewCountAsync(post.Id);

        // If increment failed (post deleted between get and increment), return 404
        if (updatedPost == null)
        {
            return NotFound();
        }

        // Step 3: Calculate reading time based on word count
        // Average reading speed: 200 words per minute (standard for technical content)
        // Formula: ReadingTime = WordCount / 200 (rounded up to nearest minute)
        var wordCount = updatedPost.Content.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries
        ).Length;

        // Calculate minutes (minimum 1 minute for very short posts)
        var readingTimeMinutes = Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));

        // Step 4: Pass data to the view via ViewBag
        // ViewBag is a dynamic container for passing extra data from Controller to View
        ViewBag.ViewCount = updatedPost.ViewCount;
        ViewBag.ReadingTime = readingTimeMinutes;
        
        // Fetch related posts and pass to ViewBag
        ViewBag.RelatedPosts = await _postService.GetRelatedPostsAsync(updatedPost.Id, updatedPost.CategoryId);

        // Step 5: Set Open Graph (OG) meta tags for social media sharing
        // These values are used by _Layout.cshtml to generate <meta property="og:..."> tags
        // When someone shares this post on Facebook/Twitter/LinkedIn, these values appear
        ViewBag.OgTitle = updatedPost.Title;
        ViewBag.OgDescription = updatedPost.Summary;
        ViewBag.OgType = "article";
        ViewBag.OgUrl = $"/yazi/{updatedPost.Slug}";

        // Pass the post to the Detail view
        return View(updatedPost);
    }

    // GET: /kategori/{slug}
    // Displays all published posts in a specific category (identified by slug).
    // If the category doesn't exist, returns 404.
    // The category name is passed via ViewBag for display in the view.
    [OutputCache(Duration = 60)]
    public async Task<IActionResult> Category(string slug, int page = 1)
    {
        // First, get the category by slug from service layer
        var category = await _categoryService.GetCategoryBySlugAsync(slug);

        // If category not found, return 404 Not Found
        if (category == null)
        {
            return NotFound();
        }

        int pageSize = 9;
        var result = await _postService.GetPostsByCategorySlugPagedAsync(slug, page, pageSize);

        // Pass the category name and slug to the view via ViewBag
        ViewBag.CategoryName = category.Name;
        ViewBag.CategorySlug = category.Slug;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)result.TotalCount / pageSize);

        // Set Open Graph (OG) meta tags for social media sharing
        ViewBag.OgTitle = $"{category.Name} Kategorisi - DevCoreBlog";
        ViewBag.OgDescription = $"{category.Name} kategorisindeki tüm yazılar. DevCoreBlog'da {category.Name} hakkında teknik içerikleri keşfedin.";
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
    public async Task<IActionResult> Search(string query)
    {
        // Guard clause: if query is empty or whitespace, return empty results
        if (string.IsNullOrWhiteSpace(query))
        {
            ViewBag.SearchQuery = "";
            ViewBag.OgTitle = "Arama - DevCoreBlog";
            ViewBag.OgDescription = "DevCoreBlog'da yazılarda arama yapın.";
            return View(Enumerable.Empty<Post>());
        }

        // Delegate to service layer — business logic is in PostService
        var posts = await _postService.SearchPostsAsync(query);

        // Pass the search query to the view for display ("Results for: ...")
        ViewBag.SearchQuery = query;

        // Set Open Graph (OG) meta tags for social media sharing
        ViewBag.OgTitle = $"\"{query}\" Arama Sonuçları - DevCoreBlog";
        ViewBag.OgDescription = $"DevCoreBlog'da \"{query}\" için arama sonuçları.";
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
    public async Task<IActionResult> ApiCategories()
    {
        var categories = await _categoryService.GetAllCategoriesAsync();
        var result = categories.Select(c => new { slug = c.Slug, name = c.Name });
        return Json(result);
    }

    // GET: /Home/Error
    // Displays the error page with a RequestId for debugging.
    // [ResponseCache] prevents caching so each error gets a fresh trace ID.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // Use the current Activity's trace ID, or fall back to HttpContext's trace ID.
        // This helps correlate error reports with server logs.
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
