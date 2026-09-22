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
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Security;
using Microsoft.Extensions.Options;
// Import authentication-related classes (ClaimsIdentity, SignInAsync, etc.)
using Microsoft.AspNetCore.Authentication;
// Import cookie authentication defaults (e.g. "Cookies" scheme name)
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly IAdminPasswordVerifier _passwordVerifier;
        private readonly AdminSessionPolicy _sessionPolicy;
        private readonly AdminSessionStamp _sessionStamp;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AccountController> _logger;

        // ---------------------------------------------------------------------------
        // CONSTRUCTOR — Dependency Injection
        // ---------------------------------------------------------------------------
        // ASP.NET Core resolves options only after startup validation succeeds.
        public AccountController(
            IOptions<AdminCredentialsOptions> adminCredentials,
            IAdminPasswordVerifier passwordVerifier,
            AdminSessionPolicy sessionPolicy,
            AdminSessionStamp sessionStamp,
            TimeProvider timeProvider,
            ILogger<AccountController> logger)
        {
            _adminCredentials = adminCredentials.Value;
            _passwordVerifier = passwordVerifier;
            _sessionPolicy = sessionPolicy;
            _sessionStamp = sessionStamp;
            _timeProvider = timeProvider;
            _logger = logger;
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
        [EnableRateLimiting("LoginLimiter")]
        public async Task<IActionResult> Login(string? username, string? password)
        {
            if (!ModelState.IsValid || !CredentialsMatch(username, password))
            {
                _logger.LogWarning(
                    "Admin sign-in attempt failed from direct connection IP {RemoteIpAddress}.",
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                ViewBag.Error = InvalidCredentialsMessage;
                return View();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(AdminSessionPolicy.VersionClaimType, _sessionStamp.ClaimValue)
            };
            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);
            var issuedUtc = _timeProvider.GetUtcNow();
            var authProperties = new AuthenticationProperties
            {
                AllowRefresh = false,
                ExpiresUtc = issuedUtc.Add(_sessionPolicy.Lifetime),
                IssuedUtc = issuedUtc,
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
            var configuredUsername = _adminCredentials.Username;
            var configuredPasswordHash = _adminCredentials.PasswordHash;
            if (!_adminCredentials.IsConfigured ||
                string.IsNullOrWhiteSpace(configuredUsername) ||
                string.IsNullOrWhiteSpace(configuredPasswordHash) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            // Calculate the password result independently so a wrong username does not
            // provide a cheap path that reveals the configured administrator name.
            var passwordMatches = _passwordVerifier.VerifyPassword(
                password,
                configuredPasswordHash);
            var usernameMatches = string.Equals(
                username,
                configuredUsername,
                StringComparison.Ordinal);

            return usernameMatches && passwordMatches;
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
