using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Configuration;
using DevCoreBlog.Models.Admin;
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
    private readonly SiteUrlOptions _siteUrl;
    private readonly PortfolioCorsOptions _portfolioCors;

    public AdminController(
        IAdminDashboardService dashboard,
        ICategoryService categories,
        IWebHostEnvironment environment,
        SiteUrlOptions siteUrl,
        PortfolioCorsOptions portfolioCors)
    {
        _dashboard = dashboard;
        _categories = categories;
        _environment = environment;
        _siteUrl = siteUrl;
        _portfolioCors = portfolioCors;
    }

    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboard.GetSnapshotAsync(cancellationToken);
        ViewData["EnvironmentName"] = _environment.EnvironmentName;
        return View(snapshot);
    }

    public async Task<IActionResult> Automations()
    {
        var categories = (await _categories.GetActiveCategoriesAsync()).ToList();
        return View(new AutomationsViewModel(categories, _siteUrl.Origin, _portfolioCors.Origins));
    }
}
