using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Translates authorized admin page requests into service reads.</summary>
[Authorize]
public sealed class AdminController : Controller
{
    private readonly IAdminDashboardService _dashboard;
    private readonly ICategoryService _categories;
    private readonly IWebHostEnvironment _environment;

    public AdminController(
        IAdminDashboardService dashboard,
        ICategoryService categories,
        IWebHostEnvironment environment)
    {
        _dashboard = dashboard;
        _categories = categories;
        _environment = environment;
    }

    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboard.GetSnapshotAsync(cancellationToken);
        ViewData["EnvironmentName"] = _environment.EnvironmentName;
        return View(snapshot);
    }

    public async Task<IActionResult> Automations()
    {
        ViewBag.Categories = (await _categories.GetActiveCategoriesAsync()).ToList();
        ViewBag.CorsOrigins = Environment.GetEnvironmentVariable("PORTFOLIO_CORS_ORIGIN")
            ?? "http://localhost:3000,http://localhost:5173,https://mehmetcan.dev";
        return View();
    }
}
