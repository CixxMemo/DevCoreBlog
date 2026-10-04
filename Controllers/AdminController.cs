using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Operations;
using DevCoreBlog.Configuration;
using DevCoreBlog.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Translates authorized admin page requests into service reads.</summary>
[Authorize]
public sealed class AdminController : Controller
{
    private readonly IAdminOverviewService _dashboard;
    private readonly WebhookIngressOptions _webhook;
    private readonly ICategoryService _categories;
    private readonly IWebHostEnvironment _environment;
    private readonly SiteUrlOptions _siteUrl;
    private readonly PortfolioCorsOptions _portfolioCors;

    public AdminController(
        IAdminOverviewService dashboard,
        ICategoryService categories,
        IWebHostEnvironment environment,
        SiteUrlOptions siteUrl,
        PortfolioCorsOptions portfolioCors,
        WebhookIngressOptions webhook)
    {
        _webhook = webhook;
        _dashboard = dashboard;
        _categories = categories;
        _environment = environment;
        _siteUrl = siteUrl;
        _portfolioCors = portfolioCors;
    }

    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboard.GetAsync(cancellationToken);
        if (snapshot.Dashboard is null) Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View(new AdminDashboardViewModel(snapshot, _environment.EnvironmentName, !string.IsNullOrWhiteSpace(_webhook.Secret)));
    }

    public async Task<IActionResult> Automations()
    {
        var categories = (await _categories.GetActiveCategoriesAsync()).ToList();
        return View(new AutomationsViewModel(categories, _siteUrl.Origin, _portfolioCors.Origins));
    }
}
