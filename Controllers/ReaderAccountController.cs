using DevCoreBlog.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Provides the reader account entry point while reader sign-in is unavailable.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReaderAccountController(PageMetadataFactory metadata) : Controller
{
    [HttpGet("/sign-in")]
    [HttpHead("/sign-in")]
    public IActionResult SignIn()
    {
        ViewData["Metadata"] = metadata.Information("Sign In",
            "For now, enjoy DevCoreBlog without signing in.", "/sign-in") with { NoIndex = true };
        return View();
    }
}
