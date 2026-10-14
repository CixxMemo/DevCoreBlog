using System.Globalization;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Models.Public;
using DevCoreBlog.Models.Seo;
using DevCoreBlog.Services.Newsletters;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Public teaser HTML is a separate bounded surface, never reader archive authorization.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NewsletterController(PublicNewsletterTeaserService teasers, PageMetadataFactory metadata) : Controller
{
    [HttpGet("/bulten")]
    [HttpHead("/bulten")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || page is < 1 or > PublicNewsletterTeaserPage.MaximumPage)
            return BadRequest("Sayfa 1–1000 arasında olmalıdır.");
        try
        {
            var result = await teasers.ReadPageAsync(page, cancellationToken);
            if (page > 1 && result.Items.Count == 0) return NotFound();
            var path = "/bulten" + (page > 1 ? "?page=" + page.ToString(CultureInfo.InvariantCulture) : "");
            ViewData["Metadata"] = metadata.Information("Bülten", "Yayımlanmış Aether Notes sayılarının başlıkları ve kısa özetleri.", path);
            return View(new NewsletterTeaserListModel(result, page));
        }
        catch (NewsletterTeaserStorageException)
        {
            return StatusCode(503, "Bülten tanıtımları şu anda okunamıyor. Daha sonra yeniden deneyin.");
        }
    }
}
