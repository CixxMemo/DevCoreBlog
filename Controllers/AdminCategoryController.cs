// =============================================================================
// AdminCategoryController.cs — Admin Category CRUD Controller
// =============================================================================
// This controller provides full CRUD (Create, Read, Update, Delete) operations
// for blog categories in the admin panel. It uses Entity Framework Core to
// interact with the PostgreSQL database through ApplicationDbContext.
//
// Key design decisions:
//   - Slug is auto-generated from the category Name (not entered by user)
//   - Categories with associated posts cannot be deleted (cascade protection)
//   - ModelState.Remove("Slug") prevents validation errors on the auto-generated field
// =============================================================================

// Import the database context for querying/saving data
using DevCoreBlog.Data;
// Import the slug generator helper for URL-friendly string conversion
using DevCoreBlog.Helpers;
// Import the Category model
using DevCoreBlog.Models;
// Import MVC attributes and base classes
using Microsoft.AspNetCore.Mvc;
// Import EF Core extension methods (Include, FindAsync, ToListAsync, etc.)
using Microsoft.EntityFrameworkCore;
// Import the Authorize attribute to restrict access to authenticated users only
using Microsoft.AspNetCore.Authorization;

namespace DevCoreBlog.Controllers;

// [Authorize] attribute ensures only logged-in users can access any action in this controller.
// Unauthenticated users will be redirected to the login page (configured in Program.cs).
[Authorize]
// Inherit from Controller to access View(), RedirectToAction(), TempData, etc.
public class AdminCategoryController : Controller
{
    // Database context field — injected via constructor (dependency injection)
    private readonly ApplicationDbContext _context;

    // Constructor receives the database context from the DI container.
    // ASP.NET Core creates a new scope per request, so this context is request-scoped.
    public AdminCategoryController(ApplicationDbContext context)
    {
        _context = context;
    }

    // -------------------------------------------------------------------------
    // INDEX — List all categories
    // -------------------------------------------------------------------------
    // GET: /AdminCategory
    // Fetches all categories from the database along with their related posts
    // (via .Include) so the view can display the post count per category.
    // Results are ordered by the default order (insertion order / Id).
    public async Task<IActionResult> Index()
    {
        // Query all categories, eagerly loading their Posts navigation property.
        // Include() performs a SQL JOIN so we don't get N+1 query problems.
        var categories = await _context.Categories.Include(c => c.Posts).ToListAsync();
        // Pass the list to the Index.cshtml view (which expects @model List<Category>)
        return View(categories);
    }

    // -------------------------------------------------------------------------
    // CREATE — Show the "New Category" form
    // -------------------------------------------------------------------------
    // GET: /AdminCategory/Create
    // Returns an empty form for creating a new category.
    public IActionResult Create()
    {
        return View();
    }

    // -------------------------------------------------------------------------
    // CREATE — Process the "New Category" form submission
    // -------------------------------------------------------------------------
    // POST: /AdminCategory/Create
    // Receives the form data bound to a Category object.
    // The Slug field is auto-generated from the Name, so we remove it from
    // ModelState validation to prevent a false "required field" error.
    [HttpPost]          // Only respond to POST requests (form submissions)
    [ValidateAntiForgeryToken]  // Protect against CSRF attacks using the hidden token in the form
    public async Task<IActionResult> Create(Category category)
    {
        // Remove "Slug" from validation because it's auto-generated, not user-provided.
        // Without this, ModelState would fail since Slug is a non-nullable string.
        ModelState.Remove("Slug");
        // Auto-generate a URL-friendly slug from the category name
        // (e.g. "C# Dersleri" → "c-sharp-dersleri" — handled by SlugGenerator)
        category.Slug = SlugGenerator.Generate(category.Name);

        // Check if all model validations passed (e.g. Name is not empty)
        if (ModelState.IsValid)
        {
            // Add the new category to the EF Core change tracker
            _context.Categories.Add(category);
            // Persist changes to the PostgreSQL database
            await _context.SaveChangesAsync();
            // Redirect to the category list page after successful creation
            return RedirectToAction(nameof(Index));
        }

        // If validation failed, re-show the form with the user's input and error messages
        return View(category);
    }

    // -------------------------------------------------------------------------
    // EDIT — Show the "Edit Category" form (pre-filled)
    // -------------------------------------------------------------------------
    // GET: /AdminCategory/Edit/5
    // Finds the category by its Id and passes it to the Edit view.
    // If not found, returns a 404 Not Found response.
    public async Task<IActionResult> Edit(int id)
    {
        // FindAsync uses the primary key (Id) for a direct lookup
        var category = await _context.Categories.FindAsync(id);
        // If no category exists with this Id, return 404
        if (category == null)
            return NotFound();

        // Pass the found category to the Edit.cshtml view for pre-filling the form
        return View(category);
    }

    // -------------------------------------------------------------------------
    // EDIT — Process the "Edit Category" form submission
    // -------------------------------------------------------------------------
    // POST: /AdminCategory/Edit/5
    // Updates an existing category. The Id in the URL must match the Id in the form.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category category)
    {
        // Safety check: ensure the URL Id matches the form's hidden Id field
        if (id != category.Id)
            return NotFound();

        // Remove auto-generated fields from validation (same reason as Create)
        ModelState.Remove("Slug");
        // Regenerate the slug in case the Name was changed
        category.Slug = SlugGenerator.Generate(category.Name);

        if (ModelState.IsValid)
        {
            // Mark the entity as modified in EF Core's change tracker
            _context.Categories.Update(category);
            // Persist the update to the database
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // If validation failed, re-show the form with errors
        return View(category);
    }

    // -------------------------------------------------------------------------
    // DELETE — Remove a category (with protection)
    // -------------------------------------------------------------------------
    // POST: /AdminCategory/Delete/5
    // Deletes a category only if it has no associated posts.
    // If posts exist, deletion is blocked to prevent cascade data loss.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        // Load the category along with its Posts collection to check the count.
        // Include() is critical here — without it, Posts would be null/empty.
        var category = await _context.Categories
            .Include(c => c.Posts)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
            return NotFound();

        // PROTECTION: If any posts belong to this category, block deletion.
        // This prevents accidental cascade deletion of blog posts.
        if (category.Posts.Count > 0)
        {
            // Store error message in TempData (survives one redirect)
            TempData["Error"] = "Bu kategoriye ait yazılar var, önce onları silin/taşıyın.";
            return RedirectToAction(nameof(Index));
        }

        // No posts — safe to remove the category
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
