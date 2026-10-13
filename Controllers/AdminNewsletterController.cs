using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Newsletters;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Private newsletter inventory and scoped writing; no email provider is a dependency.</summary>
public sealed class AdminNewsletterController(NewsletterWritingService newsletters, WritingCategoryService categories,
    PublicationTimeZone zone, TimeProvider clock, ContentDocumentValidator validator)
    : DocumentWritingControllerBase(newsletters, categories, zone, clock, validator)
{
    protected override bool IsNewsletter => true;

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        if (!ModelState.IsValid || page is < 1 or > NewsletterPage.MaximumPage) return BadRequest();
        try { return View(await newsletters.FindPageAsync(page, cancellationToken)); }
        catch (EditorPostStorageException)
        {
            return StatusCode(503, "Bülten listesi okunamadı. Biraz sonra yeniden deneyin.");
        }
    }
}
