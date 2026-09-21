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
// Import EF Core namespace for async LINQ execution methods
using Microsoft.EntityFrameworkCore;
// Import ASP.NET Core hosting environment interface
using Microsoft.AspNetCore.Hosting;
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
    // Private readonly field to hold the web hosting environment (Development/Production)
    private readonly IWebHostEnvironment _env;

    // Constructor receives ApplicationDbContext and IWebHostEnvironment via dependency injection
    public AdminController(ApplicationDbContext context, IWebHostEnvironment env)
    {
        // Store the injected dependencies for use in action methods
        _context = context;
        _env = env;
    }

    // GET: /Admin/Dashboard
    // Aggregates real-time blog metrics, top read articles, and system health status.
    public async Task<IActionResult> Dashboard()
    {
        // 1. Total active posts in the system
        ViewBag.TotalPosts = await _context.Posts.CountAsync(p => p.IsActive);

        // 2. Published posts (visible to readers)
        ViewBag.PublishedPosts = await _context.Posts.CountAsync(p => p.IsActive && p.IsPublished);

        // 3. Draft posts (work in progress)
        ViewBag.DraftPosts = await _context.Posts.CountAsync(p => p.IsActive && !p.IsPublished);

        // 4. Sum of all post views (returns 0 if no posts exist)
        ViewBag.TotalViews = await _context.Posts
            .Where(p => p.IsActive)
            .SumAsync(p => (int?)p.ViewCount) ?? 0;

        // 5. Total active categories
        ViewBag.CategoryCount = await _context.Categories.CountAsync(c => c.IsActive);

        // 6. Top 5 most read articles ranked by ViewCount DESC
        ViewBag.TopPosts = await _context.Posts
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.ViewCount)
            .Take(5)
            .ToListAsync();

        // 7. System environment and connectivity status
        ViewBag.EnvironmentName = _env.EnvironmentName;
        ViewBag.IsDatabaseConnected = await _context.Database.CanConnectAsync();

        // Return the Dashboard view
        return View();
    }

    // GET: /Admin/Automations
    // Renders the Automations and Integration Hub page (n8n/Make webhook & Portfolio API docs).
    public async Task<IActionResult> Automations()
    {
        // Load active categories to provide exact CategoryId reference in payload template
        ViewBag.Categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();

        ViewBag.CorsOrigins = Environment.GetEnvironmentVariable("PORTFOLIO_CORS_ORIGIN") 
            ?? "http://localhost:3000,http://localhost:5173,https://mehmetcan.dev";

        return View();
    }
}



