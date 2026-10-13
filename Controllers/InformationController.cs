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
        ViewData["Metadata"] = metadata.Information("Hakkında", "Aether Notes ve Mehmet Can hakkında.", "/about");
        return View();
    }

    [HttpGet("/contact")]
    [HttpHead("/contact")]
    public IActionResult Contact()
    {
        ViewData["Metadata"] = metadata.Information("İletişim", "Mehmet Can ile e-posta üzerinden iletişime geçin veya GitHub projelerine bakın.", "/contact");
        return View();
    }
}
