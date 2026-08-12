// =============================================================================
// AdminController.cs — Admin Dashboard Controller
// =============================================================================
// This controller handles the admin panel's main dashboard page.
// It injects the ApplicationDbContext to query total category and post counts,
// passing them to the Dashboard view via ViewBag.
// In Phase 4, an [Authorize] attribute is added to restrict access
// to logged-in administrators only.
// =============================================================================

// Import the ASP.NET Core MVC namespace for Controller base class and IActionResult
using Microsoft.AspNetCore.Mvc;
// Import the Authorize attribute to restrict access to authenticated users only
using Microsoft.AspNetCore.Authorization;
// Import EF Core namespace for CountAsync() extension method
using Microsoft.EntityFrameworkCore;
// Import the project's data namespace to access ApplicationDbContext
using DevCoreBlog.Data;

// Place this controller in the DevCoreBlog.Controllers namespace
namespace DevCoreBlog.Controllers;

// [Authorize] attribute ensures only logged-in users can access any action in this controller.
// Unauthenticated users will be redirected to the login page (configured in Program.cs).
[Authorize]
// Inherit from the base Controller class to get access to View(), RedirectToAction(), etc.
public class AdminController : Controller
{
    // Private readonly field to hold the injected database context
    private readonly ApplicationDbContext _context;

    // Constructor receives ApplicationDbContext via dependency injection
    // The DI container provides the same scoped DbContext instance used throughout the request
    public AdminController(ApplicationDbContext context)
    {
        // Store the injected context for use in action methods
        _context = context;
    }

    // GET: /Admin/Dashboard
    // Queries total category and post counts from the database,
    // stores them in ViewBag, and returns the Dashboard view.
    public async Task<IActionResult> Dashboard()
    {
        // Count total categories — quick scalar query, no need to load entities
        ViewBag.CategoryCount = await _context.Categories.CountAsync();
        // Count total posts — quick scalar query, no need to load entities
        ViewBag.PostCount = await _context.Posts.CountAsync();
        // Sum total views across all posts — returns 0 if no posts exist
        ViewBag.TotalViews = await _context.Posts.SumAsync(p => p.ViewCount);
        // Fetch the 5 most recent posts for the "Recent Posts" widget
        // Include Category so we can display the category name in the dashboard
        ViewBag.RecentPosts = await _context.Posts
            .Include(p => p.Category)
            .OrderByDescending(p => p.CreatedDate)
            .Take(5)
            .ToListAsync();
        // Return the Dashboard view with ViewBag data available for rendering
        return View();
    }
}
