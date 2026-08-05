// =============================================================================
// AdminPostController.cs — Admin Blog Post CRUD Controller
// =============================================================================
// This controller provides full CRUD (Create, Read, Update, Delete) operations
// for blog posts in the admin panel. It uses Entity Framework Core to interact
// with the PostgreSQL database through ApplicationDbContext.
//
// Key design decisions:
//   - Slug is auto-generated from the post Title (not entered by user)
//   - CreatedAt is set to DateTime.UtcNow on creation (never DateTime.Now)
//   - Category dropdown is populated via ViewBag using SelectList
//   - ModelState.Remove("Slug") and ModelState.Remove("Category") prevent
//     validation errors on auto-generated and navigation properties
// =============================================================================

// Import the database context for querying/saving data
using DevCoreBlog.Data;
// Import the slug generator helper for URL-friendly string conversion
using DevCoreBlog.Helpers;
// Import the Post model
using DevCoreBlog.Models;
// Import MVC attributes and base classes
using Microsoft.AspNetCore.Mvc;
// Import SelectList for populating the category dropdown in views
using Microsoft.AspNetCore.Mvc.Rendering;
// Import EF Core extension methods (Include, FindAsync, OrderByDescending, etc.)
using Microsoft.EntityFrameworkCore;
// Import the Authorize attribute to restrict access to authenticated users only
using Microsoft.AspNetCore.Authorization;

namespace DevCoreBlog.Controllers;

// [Authorize] attribute ensures only logged-in users can access any action in this controller.
// Unauthenticated users will be redirected to the login page (configured in Program.cs).
[Authorize]

// Inherit from Controller to access View(), RedirectToAction(), ViewBag, etc.
public class AdminPostController : Controller
{
    // Database context field — injected via constructor (dependency injection)
    private readonly ApplicationDbContext _context;

    // Constructor receives the database context from the DI container
    public AdminPostController(ApplicationDbContext context)
    {
        _context = context;
    }

    // -------------------------------------------------------------------------
    // INDEX — List all posts
    // -------------------------------------------------------------------------
    // GET: /AdminPost
    // Fetches all posts ordered by creation date (newest first),
    // including their related Category for display in the table.
    public async Task<IActionResult> Index()
    {
        // Query all posts, eagerly loading the Category navigation property.
        // OrderByDescending ensures the newest posts appear at the top.
        var posts = await _context.Posts
            .Include(p => p.Category)       // JOIN with Categories table
            .OrderByDescending(p => p.CreatedAt)  // Newest first
            .ToListAsync();
        return View(posts);
    }

    // -------------------------------------------------------------------------
    // CREATE — Show the "New Post" form
    // -------------------------------------------------------------------------
    // GET: /AdminPost/Create
    // Populates the category dropdown via ViewBag and returns the empty form.
    public IActionResult Create()
    {
        // Create a SelectList from all categories for the dropdown.
        // "Id" is the value field, "Name" is the display text.
        // Stored in ViewBag so the view can access it via ViewBag.CategoryId.
        ViewBag.CategoryId = new SelectList(_context.Categories, "Id", "Name");
        return View();
    }

    // -------------------------------------------------------------------------
    // CREATE — Process the "New Post" form submission
    // -------------------------------------------------------------------------
    // POST: /AdminPost/Create
    // Receives form data bound to a Post object, generates the slug,
    // sets the creation timestamp, and saves to the database.
    [HttpPost]
    [ValidateAntiForgeryToken]  // CSRF protection
    public async Task<IActionResult> Create(Post post)
    {
        // Remove auto-generated and navigation fields from validation.
        // "Slug" is auto-generated from Title — not user input.
        // "Category" is a navigation property — EF Core handles it, not the form.
        ModelState.Remove("Slug");
        ModelState.Remove("Category");
        // Auto-generate a URL-friendly slug from the post title
        post.Slug = SlugGenerator.Generate(post.Title);
        // Set creation time to UTC (required for PostgreSQL timestamp with time zone)
        post.CreatedAt = DateTime.UtcNow;

        if (ModelState.IsValid)
        {
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // If validation failed, re-populate the dropdown and re-show the form
        ViewBag.CategoryId = new SelectList(_context.Categories, "Id", "Name");
        return View(post);
    }

    // -------------------------------------------------------------------------
    // EDIT — Show the "Edit Post" form (pre-filled)
    // -------------------------------------------------------------------------
    // GET: /AdminPost/Edit/5
    // Finds the post by Id and populates the category dropdown with the
    // current category pre-selected.
    public async Task<IActionResult> Edit(int id)
    {
        var post = await _context.Posts.FindAsync(id);
        if (post == null)
            return NotFound();

        // The 4th parameter (post.CategoryId) pre-selects the current category
        // in the dropdown when the form loads
        ViewBag.CategoryId = new SelectList(_context.Categories, "Id", "Name", post.CategoryId);
        return View(post);
    }

    // -------------------------------------------------------------------------
    // EDIT — Process the "Edit Post" form submission
    // -------------------------------------------------------------------------
    // POST: /AdminPost/Edit/5
    // Updates an existing post. The Id in the URL must match the form's hidden Id.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Post post)
    {
        // Safety check: URL Id must match the form's hidden Id field
        if (id != post.Id)
            return NotFound();

        // Remove auto-generated and navigation fields from validation
        ModelState.Remove("Slug");
        ModelState.Remove("Category");
        // Regenerate slug in case the title was changed
        post.Slug = SlugGenerator.Generate(post.Title);

        if (ModelState.IsValid)
        {
            _context.Posts.Update(post);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // If validation failed, re-populate the dropdown and re-show the form
        ViewBag.CategoryId = new SelectList(_context.Categories, "Id", "Name", post.CategoryId);
        return View(post);
    }

    // -------------------------------------------------------------------------
    // DELETE — Remove a post
    // -------------------------------------------------------------------------
    // POST: /AdminPost/Delete/5
    // Deletes a post by Id. No protection needed here (unlike categories),
    // because deleting a post doesn't cascade-delete anything else.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var post = await _context.Posts.FindAsync(id);
        if (post == null)
            return NotFound();

        _context.Posts.Remove(post);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
