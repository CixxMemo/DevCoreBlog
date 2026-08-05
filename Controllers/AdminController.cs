// =============================================================================
// AdminController.cs — Admin Dashboard Controller
// =============================================================================
// This controller handles the admin panel's main dashboard page.
// It is a simple controller that only renders the Dashboard view.
// In Phase 4, an [Authorize] attribute is added to restrict access
// to logged-in administrators only.
// =============================================================================

// Import the ASP.NET Core MVC namespace for Controller base class and IActionResult
using Microsoft.AspNetCore.Mvc;
// Import the Authorize attribute to restrict access to authenticated users only
using Microsoft.AspNetCore.Authorization;

// Place this controller in the DevCoreBlog.Controllers namespace
namespace DevCoreBlog.Controllers;

// [Authorize] attribute ensures only logged-in users can access any action in this controller.
// Unauthenticated users will be redirected to the login page (configured in Program.cs).
[Authorize]
// Inherit from the base Controller class to get access to View(), RedirectToAction(), etc.
public class AdminController : Controller
{
    // GET: /Admin/Dashboard
    // Returns the Dashboard.cshtml view, which shows admin panel navigation cards.
    // Uses expression-bodied member syntax (shorthand for a method with a single return).
    public IActionResult Dashboard() => View();
}
