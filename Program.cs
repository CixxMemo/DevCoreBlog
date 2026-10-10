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
using DevCoreBlog.Data.Repositories;
// Import Service layer for dependency injection
using DevCoreBlog.Services;
// Import Service interfaces for dependency injection
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Security;
using DevCoreBlog.Services.Images;
using DevCoreBlog.Services.Publishing;
using DevCoreBlog.Services.Rendering;
using CloudinaryDotNet;
// Import Middlewares
using DevCoreBlog.Middlewares;
// Import validated application configuration models
using DevCoreBlog.Configuration;
using DevCoreBlog.Security;
// Import Rate Limiting namespaces for endpoint protection
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Caching;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HostFiltering;

if (args.Length > 0 && args[0] == ImageDecodeWorker.Argument)
{
    Environment.ExitCode = args.Length == 2 ? await ImageDecodeWorker.RunAsync(args[1]) : 2;
    return;
}

// Create the application builder, which loads configuration from appsettings.json,
// environment variables, and command-line arguments
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => { options.IncludeScopes = true; options.UseUtcTimestamp = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; });
// Provider diagnostics can contain SQL/connection details; application events use safe summaries.
builder.Services.PostConfigure<LoggerFilterOptions>(options =>
{
    // Even a more-specific environment override must not print provider errors containing input/SQL.
    for (var i = options.Rules.Count - 1; i >= 0; i--)
        if (options.Rules[i].CategoryName is { } category &&
            (category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || category.StartsWith("Npgsql", StringComparison.Ordinal)))
            options.Rules.RemoveAt(i);
    options.Rules.Add(new LoggerFilterRule(null, "Microsoft.EntityFrameworkCore", LogLevel.None, null));
    options.Rules.Add(new LoggerFilterRule(null, "Npgsql", LogLevel.None, null));
});
// Development dotenv values fill missing variables only; production uses the service environment.
var dotenvPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (builder.Environment.IsDevelopment())
    DotNetEnv.Env.NoClobber().Load(dotenvPath);

var siteUrl = new SiteUrlOptions(
    Environment.GetEnvironmentVariable("SITE_URL"), builder.Environment.IsDevelopment());
builder.Services.AddSingleton(siteUrl);
var deploymentBoundary = new DeploymentBoundary(builder.Environment.IsDevelopment(), siteUrl,
    Environment.GetEnvironmentVariable("DEPLOYMENT_PROFILE"),
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? builder.Configuration["urls"],
    builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any(),
    Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"));
builder.Services.AddSingleton(deploymentBoundary);
if (deploymentBoundary.ListenUrl is not null) builder.WebHost.UseUrls(deploymentBoundary.ListenUrl);
builder.Configuration["AllowedHosts"] = string.Join(';', deploymentBoundary.AllowedHosts);
builder.Services.PostConfigure<HostFilteringOptions>(options =>
{
    options.AllowedHosts = deploymentBoundary.AllowedHosts.ToArray();
    options.AllowEmptyHosts = false;
    options.IncludeFailureMessage = false;
});
builder.Services.PostConfigure<ForwardedHeadersOptions>(deploymentBoundary.ConfigureForwarding);
if (!builder.Environment.IsDevelopment())
    builder.Services.AddHttpsRedirection(options =>
    {
        options.HttpsPort = 443;
        options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
    });
builder.Services.AddSingleton<DevCoreBlog.Routing.PublicUrlBuilder>();
builder.Services.AddScoped<DevCoreBlog.Models.Seo.PageMetadataFactory>();

// ---------------------------------------------------------------------------
// SERVICE REGISTRATION (Dependency Injection Container)
// ---------------------------------------------------------------------------

// Read the database connection string from the .env file (loaded above).
// Environment.GetEnvironmentVariable reads the value that DotNetEnv.Env.Load() injected.
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("DB_CONNECTION_STRING was not found in the .env file or environment variables.");
}

var migrationsAssemblyName = typeof(Program).Assembly.GetName().Name
    ?? throw new InvalidOperationException(
        "The Web assembly name is required for Entity Framework migrations.");

// Register the ApplicationDbContext with the DI container.
// This tells EF Core to use PostgreSQL (via Npgsql) as the database provider.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(migrationsAssemblyName)));

// Load the single-admin credentials once and validate them when the host starts.
// Validation messages identify only the missing key and never include its value.
var adminUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
var adminPasswordHash = Environment.GetEnvironmentVariable("ADMIN_PASSWORD_HASH");
var adminLogin = new AdminLoginOptions(Environment.GetEnvironmentVariable("ADMIN_LOGIN_PATH"));
builder.Services.AddSingleton(adminLogin);
var adminSessionVersion = Environment.GetEnvironmentVariable("ADMIN_SESSION_VERSION") ?? "1";

if (string.IsNullOrWhiteSpace(adminSessionVersion) || adminSessionVersion.Length > 128)
{
    throw new InvalidOperationException(
        "ADMIN_SESSION_VERSION is required and must contain between 1 and 128 characters.");
}

var rawAdminSessionLifetime = Environment.GetEnvironmentVariable(
    "ADMIN_SESSION_LIFETIME_SECONDS");
if (!int.TryParse(
        rawAdminSessionLifetime ?? AdminSessionPolicy.DefaultLifetimeSeconds.ToString(
            CultureInfo.InvariantCulture),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var adminSessionLifetimeSeconds) ||
    adminSessionLifetimeSeconds is < 1 or > AdminSessionPolicy.MaximumLifetimeSeconds)
{
    throw new InvalidOperationException(
        $"ADMIN_SESSION_LIFETIME_SECONDS must be between 1 and {AdminSessionPolicy.MaximumLifetimeSeconds}.");
}

builder.Services
    .AddOptions<AdminCredentialsOptions>()
    .Configure(options =>
    {
        options.Username = adminUsername;
        options.PasswordHash = adminPasswordHash;
    })
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Username),
        "ADMIN_USERNAME is required and cannot be empty or whitespace.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.PasswordHash),
        "ADMIN_PASSWORD_HASH is required and cannot be empty or whitespace.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.PasswordHash) ||
            Pbkdf2PasswordHasher.IsValidEncodedHash(options.PasswordHash),
        "ADMIN_PASSWORD_HASH is malformed, unsupported, or outside the allowed cost bounds.")
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// REPOSITORY LAYER REGISTRATION (Data Access)
// ---------------------------------------------------------------------------
// Register repositories with Scoped lifetime (one instance per HTTP request).
// This ensures each request gets its own repository instance, which shares
// the same DbContext instance within that request.
builder.Services.AddScoped<PostRepository>();
builder.Services.AddScoped<IWebhookPostRepository, WebhookPostRepository>();
builder.Services.AddScoped<CategoryRepository>();
builder.Services.AddScoped<IPostRepository>(serviceProvider =>
    serviceProvider.GetRequiredService<PostRepository>());
builder.Services.AddScoped<ICategoryRepository>(serviceProvider =>
    serviceProvider.GetRequiredService<CategoryRepository>());
builder.Services.AddScoped<IActiveCategoryLookup>(serviceProvider =>
    serviceProvider.GetRequiredService<CategoryRepository>());
builder.Services.AddScoped<IAdminDashboardReadRepository, AdminDashboardReadRepository>();

// ---------------------------------------------------------------------------
// SERVICE LAYER REGISTRATION (Business Logic)
// ---------------------------------------------------------------------------
// Register services with Scoped lifetime (one instance per HTTP request).
// Services depend on repositories, which are also scoped.
// Controllers will depend on service interfaces (IPostService, ICategoryService).
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<IPostService>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<DevCoreBlog.Models.Seo.RssDocumentWriter>();
builder.Services.AddScoped<IRssPostReader>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<ISitemapPostReader>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<IPublicFeedService>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<IWebhookPostService>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<IPublicationSchedule>(serviceProvider =>
    serviceProvider.GetRequiredService<PostService>());
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ICategoryService>(serviceProvider =>
    serviceProvider.GetRequiredService<CategoryService>());
builder.Services.AddScoped<ISitemapCategoryReader>(serviceProvider =>
    serviceProvider.GetRequiredService<CategoryService>());
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IDatabaseConnectionProbe, DatabaseConnectionProbe>();
builder.Services.AddScoped<DevCoreBlog.Services.Operations.IAdminOverviewService, DevCoreBlog.Services.Operations.AdminOverviewService>();
builder.Services.AddSingleton<ISafeMarkdownRenderer, SafeMarkdownRenderer>();
builder.Services.AddSingleton<DevCoreBlog.Services.Documents.ContentDocumentValidator>();
builder.Services.AddSingleton<DevCoreBlog.Services.Documents.DocumentTextProducer>();
builder.Services.AddSingleton<DevCoreBlog.Services.Documents.DocumentWebRenderer>();
builder.Services.AddScoped<DevCoreBlog.Core.Documents.IPostDocumentRepository, PostDocumentRepository>();
builder.Services.AddScoped<DevCoreBlog.Services.Documents.PostDocumentService>();
builder.Services.AddScoped<DevCoreBlog.Core.Documents.IEditorPostRepository, EditorPostRepository>();
builder.Services.AddScoped<DevCoreBlog.Services.Documents.PostWritingService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddSingleton<IImageDecoder, ProcessImageDecoder>();
builder.Services.AddSingleton<ImageUploadPolicy>();
builder.Services.AddSingleton<CloudinaryImageUploadRequestFactory>();
var rawAllowWebhookPublish = Environment.GetEnvironmentVariable("ALLOW_WEBHOOK_PUBLISH");
var allowWebhookPublish = false;
if (!string.IsNullOrEmpty(rawAllowWebhookPublish) &&
    !bool.TryParse(rawAllowWebhookPublish, out allowWebhookPublish))
{
    throw new InvalidOperationException("ALLOW_WEBHOOK_PUBLISH must be true or false.");
}

builder.Services.AddSingleton(new WebhookIngressOptions(
    Environment.GetEnvironmentVariable("WEBHOOK_API_SECRET"),
    allowWebhookPublish));
var cloudinaryCloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME");
var cloudinaryApiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY");
var cloudinaryApiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET");
var mediaConfigured = !string.IsNullOrWhiteSpace(cloudinaryCloudName) &&
    !string.IsNullOrWhiteSpace(cloudinaryApiKey) && !string.IsNullOrWhiteSpace(cloudinaryApiSecret);
builder.Services.AddSingleton(new DevCoreBlog.Services.Operations.MediaOperationStatus(mediaConfigured, TimeProvider.System));
if (mediaConfigured)
{
    var cloudinary = new Cloudinary(new Account(cloudinaryCloudName, cloudinaryApiKey, cloudinaryApiSecret));
    cloudinary.Api.Secure = true;
    builder.Services.AddSingleton<ICloudinaryUploadApi>(cloudinary);
    builder.Services.AddScoped<IImageStorage, CloudinaryImageStorage>();
}
else builder.Services.AddScoped<IImageStorage, UnavailableImageStorage>();
builder.Services.AddSingleton<IAdminPasswordVerifier, Pbkdf2PasswordHasher>();

var adminSessionPolicy = new AdminSessionPolicy(
    TimeSpan.FromSeconds(adminSessionLifetimeSeconds));
builder.Services.AddSingleton(adminSessionPolicy);
builder.Services.AddSingleton(new AdminSessionStamp(
    adminUsername ?? string.Empty,
    adminPasswordHash ?? string.Empty,
    adminSessionVersion));
builder.Services.AddScoped<AdminCookieAuthenticationEvents>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PublicListCacheInvalidator>();
builder.Services.AddOutputCache(options =>
    options.AddPolicy("PublicLists", policy =>
        policy.AddPolicy<PublicListCachePolicy>()
            .Tag(PublicListCacheInvalidator.Tag)));

// Publication dates use one explicit site clock regardless of the host TZ.
var siteTimeZoneId = Environment.GetEnvironmentVariable("SITE_TIME_ZONE")
    ?? "Europe/Istanbul";
if (string.IsNullOrWhiteSpace(siteTimeZoneId))
{
    throw new InvalidOperationException("SITE_TIME_ZONE must name a valid time zone.");
}

TimeZoneInfo siteTimeZone;
try
{
    siteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(siteTimeZoneId);
}
catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
{
    throw new InvalidOperationException(
        "SITE_TIME_ZONE must name a time zone installed on this host.",
        exception);
}

builder.Services.AddSingleton(new PublicationTimeZone(siteTimeZone));
builder.Services.AddSingleton<DevCoreBlog.Models.Presentation.SiteDateFormatter>();

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName(AdminSessionPolicy.ApplicationName);
var dataProtectionKeysPath = Environment.GetEnvironmentVariable(
    "DATA_PROTECTION_KEYS_PATH");

if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    if (!Path.IsPathFullyQualified(dataProtectionKeysPath))
    {
        throw new InvalidOperationException(
            "DATA_PROTECTION_KEYS_PATH must be an absolute path.");
    }

    var keyDirectory = new DirectoryInfo(dataProtectionKeysPath);
    if (!keyDirectory.Exists)
    {
        throw new InvalidOperationException(
            "DATA_PROTECTION_KEYS_PATH must point to an existing directory.");
    }

    if (!builder.Environment.IsDevelopment() &&
        !OperatingSystem.IsWindows() &&
        HasSharedUnixPermissions(keyDirectory.FullName))
    {
        throw new InvalidOperationException(
            "DATA_PROTECTION_KEYS_PATH must be accessible only to its owner in production.");
    }

    dataProtectionBuilder.PersistKeysToFileSystem(keyDirectory);
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DATA_PROTECTION_KEYS_PATH is required outside Development.");
}

// Validate antiforgery tokens on every unsafe MVC request by default. The inbound
// secret-auth webhook declares its narrow exception on that action.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Conventions.Add(new DevCoreBlog.Routing.AdminLoginRouteConvention(adminLogin));
});

// AJAX callers send the request token in this header. Standard Razor forms keep
// using the generated __RequestVerificationToken form field.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});


// ---------------------------------------------------------------------------
// RATE LIMITING REGISTRATION (DDoS & Webhook Brute-Force Protection)
// ---------------------------------------------------------------------------
var loginRateLimitPermitLimit = builder.Configuration.GetValue(
    "Security:LoginRateLimit:PermitLimit",
    5);
var loginRateLimitWindowSeconds = builder.Configuration.GetValue(
    "Security:LoginRateLimit:WindowSeconds",
    60);
var webhookRateLimitPermitLimit = builder.Configuration.GetValue(
    "Security:WebhookRateLimit:PermitLimit",
    5);

if (loginRateLimitPermitLimit is < 1 or > 20)
{
    throw new InvalidOperationException(
        "Security:LoginRateLimit:PermitLimit must be between 1 and 20.");
}

if (loginRateLimitWindowSeconds is < 1 or > 300)
{
    throw new InvalidOperationException(
        "Security:LoginRateLimit:WindowSeconds must be between 1 and 300.");
}

if (webhookRateLimitPermitLimit is < 1 or > 20)
{
    throw new InvalidOperationException(
        "Security:WebhookRateLimit:PermitLimit must be between 1 and 20.");
}

builder.Services.AddRateLimiter(options =>
{
    // Partition by the client IP resolved only through the explicit ingress trust boundary.
    options.AddPolicy("LoginLimiter", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-login-client",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = loginRateLimitPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(loginRateLimitWindowSeconds)
            }));

    // Webhook and portfolio use the same trusted client-IP boundary as login.
    options.AddPolicy("WebhookLimiter", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous-webhook",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = webhookRateLimitPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Portfolio Limiter: Max 30 requests per minute for public read endpoint
    options.AddPolicy("PortfolioLimiter", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous-portfolio",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 30,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Reject excess requests with standard HTTP 429 Too Many Requests
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfterSeconds = loginRateLimitWindowSeconds;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        context.HttpContext.Response.Headers["Retry-After"] =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var request = context.HttpContext.Request;
        var isLoginPost = HttpMethods.IsPost(request.Method) &&
            string.Equals(request.Path.Value, adminLogin.Path, StringComparison.OrdinalIgnoreCase);

        if (!isLoginPost)
        {
            return;
        }

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevCoreBlog.Security.LoginRateLimit");
        logger.LogWarning(
            "Admin sign-in rate limit rejected a request from resolved client IP {RemoteIpAddress}.",
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            $"Too many sign-in attempts. Try again in {retryAfterSeconds} seconds.",
            cancellationToken);
    };
});

// ---------------------------------------------------------------------------
// CORS POLICY REGISTRATION (React Portfolio Showcase Integration)
// ---------------------------------------------------------------------------
// CORS is a browser read policy, not authorization for this public endpoint.
var portfolioCors = new PortfolioCorsOptions(
    Environment.GetEnvironmentVariable("PORTFOLIO_CORS_ORIGIN"), builder.Environment.IsDevelopment());
builder.Services.AddSingleton(portfolioCors);

builder.Services.AddCors(options =>
{
    options.AddPolicy("PortfolioPolicy", policy =>
    {
        policy.WithOrigins(portfolioCors.Origins.ToArray())
              .WithMethods("GET", "OPTIONS")
              .WithHeaders("Content-Type", "Accept", "X-Requested-With");
    });
});

// ---------------------------------------------------------------------------
// COOKIE AUTHENTICATION REGISTRATION
// ---------------------------------------------------------------------------
// Register cookie-based authentication with the default "Cookies" scheme.
// LoginPath: where unauthenticated users are redirected (must be a GET route).
// AccessDeniedPath: where unauthorized users are redirected (same login page for MVP).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AdminSessionPolicy.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = adminSessionPolicy.Lifetime;
        options.SlidingExpiration = false;
        options.EventsType = typeof(AdminCookieAuthenticationEvents);
        options.LoginPath = adminLogin.Path;
        options.AccessDeniedPath = adminLogin.Path;
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

// Resolve trusted ingress information before HTTPS, authentication and IP-based limits.
app.UseForwardedHeaders();
// Apply final headers before explicit host filtering and after cleared error responses.
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseHostFiltering();

// Global Error Handling Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable HTTP Strict Transport Security (HSTS) outside development.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Redirect all HTTP requests to HTTPS for secure communication
app.UseHttpsRedirection();

// Enable URL-based routing — this must come before endpoint mapping
app.UseRouting();

// Enable CORS middleware (positioned between UseRouting and Auth/Endpoints)
app.UseCors();

// Enable Rate Limiter middleware
app.UseRateLimiter();

// Enable cookie authentication middleware — MUST come before UseAuthorization.
// This reads the auth cookie on each request and sets HttpContext.User.
app.UseAuthentication();

// Enable authorization checks (e.g. [Authorize] attribute on controllers)
app.UseAuthorization();

// Evaluate authenticated state before serving anonymous public HTML from cache.
app.UseOutputCache();

// Serve static files (CSS, JS, images) from the wwwroot folder
app.MapStaticAssets();

// ---------------------------------------------------------------------------
// CUSTOM PUBLIC ROUTES (slug-based URLs for visitors)
// ---------------------------------------------------------------------------
// Named English paths are canonical; Home redirects visible aliases before rendering/counting.

// Route for individual blog post pages: /post/{slug} and /yazi/{slug}
app.MapControllerRoute(
    name: "post-en",
    pattern: "post/{slug}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "post",
    pattern: "yazi/{slug}",
    defaults: new { controller = "Home", action = "Detail" });

// Route for category listing pages: /category/{slug} and /kategori/{slug}
app.MapControllerRoute(
    name: "category-en",
    pattern: "category/{slug}",
    defaults: new { controller = "Home", action = "Category" });

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

static bool HasSharedUnixPermissions(string path)
{
    var mode = File.GetUnixFileMode(path);
    const UnixFileMode sharedPermissions =
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;

    return (mode & sharedPermissions) != 0;
}
