// =============================================================================
// AccountController.cs — Admin Authentication Controller
// =============================================================================
// This controller handles admin login and logout operations using cookie-based
// authentication. It uses credentials validated during application startup and validates
// user input against those credentials. No database user table is involved.
// =============================================================================

// Import ASP.NET Core MVC base controller class
using Microsoft.AspNetCore.Mvc;
// Import the validated admin configuration model
using DevCoreBlog.Configuration;
using Microsoft.Extensions.Options;
// Import authentication-related classes (ClaimsIdentity, SignInAsync, etc.)
using Microsoft.AspNetCore.Authentication;
// Import cookie authentication defaults (e.g. "Cookies" scheme name)
using Microsoft.AspNetCore.Authentication.Cookies;
// Import claims-based identity types (Claim, ClaimTypes, ClaimsIdentity)
using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;

namespace DevCoreBlog.Controllers
{
    // ---------------------------------------------------------------------------
    // ACCOUNT CONTROLLER
    // ---------------------------------------------------------------------------
    // Handles login/logout for the single admin user.
    // Credentials are supplied through validated environment configuration.
    public class AccountController : Controller
    {
        private const string InvalidCredentialsMessage = "Invalid username or password.";
        private readonly AdminCredentialsOptions _adminCredentials;

        // ---------------------------------------------------------------------------
        // CONSTRUCTOR — Dependency Injection
        // ---------------------------------------------------------------------------
        // ASP.NET Core resolves options only after startup validation succeeds.
        public AccountController(IOptions<AdminCredentialsOptions> adminCredentials)
        {
            _adminCredentials = adminCredentials.Value;
        }

        // ---------------------------------------------------------------------------
        // LOGIN — GET
        // ---------------------------------------------------------------------------
        // Displays the login form. If the user is already authenticated,
        // redirect them to the admin dashboard instead of showing the form again.
        [HttpGet]
        public IActionResult Login()
        {
            // Check if the current request is already authenticated
            // Use null-conditional operator to avoid CS8602 warning
            if (User.Identity?.IsAuthenticated == true)
            {
                // Already logged in — redirect to admin dashboard
                return RedirectToAction("Dashboard", "Admin");
            }

            // Not logged in — show the login form
            return View();
        }

        // ---------------------------------------------------------------------------
        // LOGIN — POST
        // ---------------------------------------------------------------------------
        // Validates the submitted username and password against startup-validated options.
        // If valid: creates a claims identity, signs the user in with a cookie,
        // and redirects to the admin dashboard.
        // If invalid: sets ViewBag.Error to show an error message on the form.
        [HttpPost]
        public async Task<IActionResult> Login(string? username, string? password)
        {
            if (!ModelState.IsValid || !CredentialsMatch(username, password))
            {
                ViewBag.Error = InvalidCredentialsMessage;
                return View();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username)
            };
            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = false
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("Dashboard", "Admin");
        }

        private bool CredentialsMatch(
            [NotNullWhen(true)] string? username,
            [NotNullWhen(true)] string? password)
        {
            if (!_adminCredentials.IsConfigured ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            return string.Equals(username, _adminCredentials.Username, StringComparison.Ordinal) &&
                   string.Equals(password, _adminCredentials.Password, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------------------
        // LOGOUT — POST
        // ---------------------------------------------------------------------------
        // Signs the user out by removing the authentication cookie.
        // Must be POST to prevent CSRF via GET requests (security best practice).
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            // Remove the authentication cookie from the response
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Redirect to the home page after logout
            return RedirectToAction("Index", "Home");
        }
    }
}
