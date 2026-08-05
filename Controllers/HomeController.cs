// =============================================================================
// HomeController.cs — Public-Facing Home Controller
// =============================================================================
// This controller handles the public (visitor-facing) pages of the blog.
// It contains actions for:
//   - Index: List of published posts on the home page
//   - Detail: Single post view by slug
//   - Category: Posts filtered by category slug
//   - Error: Error page with RequestId for debugging
// =============================================================================

// Import for generating a trace identifier on error pages
using System.Diagnostics;
// Import MVC base classes and attributes
using Microsoft.AspNetCore.Mvc;
// Import the ErrorViewModel used by the Error action
using DevCoreBlog.Models;
// Import the database context for querying posts and categories
using DevCoreBlog.Data;
// Import Entity Framework Core for Include, Where, OrderByDescending, ToListAsync
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Controllers;

// Inherit from Controller for access to View(), HttpContext, etc.
public class HomeController : Controller
{
    // ---------------------------------------------------------------------------
    // DEPENDENCY INJECTION
    // ---------------------------------------------------------------------------
    // Private readonly field to hold the injected database context.
    // Used by all actions in this controller to query posts and categories.
    private readonly ApplicationDbContext _context;

    // Constructor receives ApplicationDbContext via dependency injection.
    // The DI container (configured in Program.cs) provides the instance.
    public HomeController(ApplicationDbContext context)
    {
        // Store the injected context for use in action methods
        _context = context;
    }

    // ---------------------------------------------------------------------------
    // PUBLIC ACTIONS
    // ---------------------------------------------------------------------------

    // GET: /
    // Displays the public home page with a list of published blog posts.
    // Only published posts are shown, ordered by creation date (newest first).
    // Each post includes its related Category for display in the view.
    public async Task<IActionResult> Index()
    {
        // Query published posts, include their Category navigation property,
        // order by CreatedAt descending (newest first), and convert to a List.
        var posts = await _context.Posts
            .Include(p => p.Category)           // Eager-load the related Category
            .Where(p => p.IsPublished)          // Only show published posts
            .OrderByDescending(p => p.CreatedAt) // Newest posts first
            .ToListAsync();                     // Execute query asynchronously

        // Pass the list of posts to the view
        return View(posts);
    }

    // GET: /yazi/{slug}
    // Displays a single blog post identified by its slug.
    // Only published posts are accessible; unpublished or non-existent posts return 404.
    // The post's Category is eager-loaded for display in the view.
    public async Task<IActionResult> Detail(string slug)
    {
        // Find the first post matching the slug AND is published.
        // Include the Category navigation property so the view can display it.
        var post = await _context.Posts
            .Include(p => p.Category)                              // Eager-load Category
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished); // Match slug + published

        // If no matching post found, return 404 Not Found
        if (post == null)
        {
            return NotFound();
        }

        // Pass the post to the Detail view
        return View(post);
    }

    // GET: /kategori/{slug}
    // Displays all published posts in a specific category (identified by slug).
    // If the category doesn't exist, returns 404.
    // The category name is passed via ViewBag for display in the view.
    public async Task<IActionResult> Category(string slug)
    {
        // Find the category by slug
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == slug);

        // If category not found, return 404 Not Found
        if (category == null)
        {
            return NotFound();
        }

        // Query published posts in this category, ordered by creation date (newest first)
        var posts = await _context.Posts
            .Include(p => p.Category)                // Eager-load Category for display
            .Where(p => p.CategoryId == category.Id && p.IsPublished) // Filter by category + published
            .OrderByDescending(p => p.CreatedAt)      // Newest posts first
            .ToListAsync();                           // Execute query asynchronously

        // Pass the category name to the view via ViewBag
        ViewBag.CategoryName = category.Name;

        // Pass the list of posts to the Category view
        return View(posts);
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
