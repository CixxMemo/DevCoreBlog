using DevCoreBlog.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Serves read-only information pages without changing posts or collecting messages.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InformationController(PageMetadataFactory metadata) : Controller
{
    [HttpGet("/about")]
    [HttpHead("/about")]
    public IActionResult About()
    {
        ViewData["Metadata"] = metadata.Information("About", "Meet Mehmet Can, the person behind DevCoreBlog.", "/about");
        return View();
    }

    [HttpGet("/contact")]
    [HttpHead("/contact")]
    public IActionResult Contact()
    {
        ViewData["Metadata"] = metadata.Information("Contact", "Contact Mehmet Can by email or find his projects on GitHub.", "/contact");
        return View();
    }
}
