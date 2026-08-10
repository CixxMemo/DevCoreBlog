// =============================================================================
// AccountController.cs — Admin Authentication Controller
// =============================================================================
// This controller handles admin login and logout operations using cookie-based
// authentication. It reads admin credentials from appsettings.json and validates
// user input against those credentials. No database user table is involved.
// =============================================================================

// Import ASP.NET Core MVC base controller class
using Microsoft.AspNetCore.Mvc;
// Import configuration access (to read appsettings.json values)
using Microsoft.Extensions.Configuration;
// Import authentication-related classes (ClaimsIdentity, SignInAsync, etc.)
using Microsoft.AspNetCore.Authentication;
// Import cookie authentication defaults (e.g. "Cookies" scheme name)
using Microsoft.AspNetCore.Authentication.Cookies;
// Import claims-based identity types (Claim, ClaimTypes, ClaimsIdentity)
using System.Security.Claims;

namespace DevCoreBlog.Controllers
{
    // ---------------------------------------------------------------------------
    // ACCOUNT CONTROLLER
    // ---------------------------------------------------------------------------
    // Handles login/logout for the single admin user.
    // Credentials are stored in appsettings.json under "AdminCredentials".
    public class AccountController : Controller
    {
        // Store configuration reference to access appsettings.json values
        private readonly IConfiguration _configuration;

        // ---------------------------------------------------------------------------
        // CONSTRUCTOR — Dependency Injection
        // ---------------------------------------------------------------------------
        // ASP.NET Core injects IConfiguration automatically.
        // We store it in a private field to use in action methods.
        public AccountController(IConfiguration configuration)
        {
            _configuration = configuration;
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
        // Validates the submitted username and password against appsettings.json.
        // If valid: creates a claims identity, signs the user in with a cookie,
        // and redirects to the admin dashboard.
        // If invalid: sets ViewBag.Error to show an error message on the form.
        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            // Read expected credentials from environment variables (.env file)
            var expectedUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
            var expectedPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

            // Compare submitted credentials with configured ones
            if (username == expectedUsername && password == expectedPassword)
            {
                // Credentials match — create a claim for the username
                var claims = new List<Claim>
                {
                    // ClaimTypes.Name is a standard claim for the user's name
                    new Claim(ClaimTypes.Name, username)
                };

                // Create a ClaimsIdentity with the cookie authentication scheme
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                // Build the authentication properties (can add expiration, etc. later)
                var authProperties = new AuthenticationProperties
                {
                    // IsPersistent = true would make the cookie survive browser close
                    // For now, leave it false (session cookie)
                    IsPersistent = false
                };

                // Sign the user in — this sets the authentication cookie in the response
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Redirect to admin dashboard after successful login
                return RedirectToAction("Dashboard", "Admin");
            }

            // Credentials don't match — show error message on the login form
            ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";

            // Return the login view with the error message
            return View();
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
