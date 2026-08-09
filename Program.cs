// =============================================================================
// Program.cs — Application Entry Point
// =============================================================================
// This is the main entry point of the ASP.NET Core application.
// It configures services (like the database) and sets up the HTTP request
// middleware pipeline that handles every incoming web request.
// =============================================================================

// Import the namespace where our database context class lives
using DevCoreBlog.Data;
// Import Entity Framework Core so we can use the PostgreSQL database provider
using Microsoft.EntityFrameworkCore;
// Import cookie authentication defaults (e.g. "Cookies" scheme name)
using Microsoft.AspNetCore.Authentication.Cookies;
// Import Repository layer for dependency injection
using DevCoreBlog.Repositories;
// Import Service layer for dependency injection
using DevCoreBlog.Services;
// Import Service interfaces for dependency injection
using DevCoreBlog.Services.Interfaces;

// Create the application builder, which loads configuration from appsettings.json,
// environment variables, and command-line arguments
var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// SERVICE REGISTRATION (Dependency Injection Container)
// ---------------------------------------------------------------------------

// Register the ApplicationDbContext with the DI container.
// This tells EF Core to use PostgreSQL (via Npgsql) as the database provider,
// reading the connection string named "DefaultConnection" from appsettings.json.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------------------------------------------------------------------
// REPOSITORY LAYER REGISTRATION (Data Access)
// ---------------------------------------------------------------------------
// Register repositories with Scoped lifetime (one instance per HTTP request).
// This ensures each request gets its own repository instance, which shares
// the same DbContext instance within that request.
builder.Services.AddScoped<PostRepository>();
builder.Services.AddScoped<CategoryRepository>();

// ---------------------------------------------------------------------------
// SERVICE LAYER REGISTRATION (Business Logic)
// ---------------------------------------------------------------------------
// Register services with Scoped lifetime (one instance per HTTP request).
// Services depend on repositories, which are also scoped.
// Controllers will depend on service interfaces (IPostService, ICategoryService).
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

// Register MVC services (controllers + views + tag helpers + model binding).
// This is required for the app to handle controller-based routes and render Razor views.
builder.Services.AddControllersWithViews();

// Register Output Caching services
builder.Services.AddOutputCache();

// ---------------------------------------------------------------------------
// COOKIE AUTHENTICATION REGISTRATION
// ---------------------------------------------------------------------------
// Register cookie-based authentication with the default "Cookies" scheme.
// LoginPath: where unauthenticated users are redirected (must be a GET route).
// AccessDeniedPath: where unauthorized users are redirected (same login page for MVP).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Redirect unauthenticated users to the login page
        options.LoginPath = "/Account/Login";
        // Redirect unauthorized (logged-in but not allowed) users to the login page
        options.AccessDeniedPath = "/Account/Login";
    });

// ---------------------------------------------------------------------------
// BUILD THE APPLICATION
// ---------------------------------------------------------------------------

// Build the app instance from the configured builder.
// After this point, we configure the middleware pipeline (the request pipeline).
var app = builder.Build();

// ---------------------------------------------------------------------------
// MIDDLEWARE PIPELINE (order matters — top to bottom)
// ---------------------------------------------------------------------------

// In non-development environments, use a generic error handler page
// and enable HTTP Strict Transport Security (HSTS) for browser security.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Redirect all HTTP requests to HTTPS for secure communication
app.UseHttpsRedirection();

// Enable URL-based routing — this must come before endpoint mapping
app.UseRouting();

// Enable output caching
app.UseOutputCache();

// Enable cookie authentication middleware — MUST come before UseAuthorization.
// This reads the auth cookie on each request and sets HttpContext.User.
app.UseAuthentication();

// Enable authorization checks (e.g. [Authorize] attribute on controllers)
app.UseAuthorization();

// Serve static files (CSS, JS, images) from the wwwroot folder
app.MapStaticAssets();

// ---------------------------------------------------------------------------
// CUSTOM PUBLIC ROUTES (slug-based URLs for visitors)
// ---------------------------------------------------------------------------
// These routes must be registered BEFORE the default route so they take priority.
// They map friendly slug-based URLs to the Home controller's Detail and Category actions.
// Note: .WithStaticAssets() is intentionally NOT chained here — only the default route uses it.

// Route for individual blog post pages: /yazi/{slug}
// Maps to HomeController.Detail(string slug) action
app.MapControllerRoute(
    name: "post",
    pattern: "yazi/{slug}",
    defaults: new { controller = "Home", action = "Detail" });

// Route for category listing pages: /kategori/{slug}
// Maps to HomeController.Category(string slug) action
app.MapControllerRoute(
    name: "category",
    pattern: "kategori/{slug}",
    defaults: new { controller = "Home", action = "Category" });

// ---------------------------------------------------------------------------
// DEFAULT MVC ROUTE
// ---------------------------------------------------------------------------
// Define the default MVC route pattern:
//   {controller=Home}  → defaults to HomeController
//   {action=Index}     → defaults to Index action
//   {id?}              → optional parameter (e.g. /Edit/5)
// .WithStaticAssets() enables cache-busting for static assets referenced in views
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Start listening for HTTP requests
app.Run();
