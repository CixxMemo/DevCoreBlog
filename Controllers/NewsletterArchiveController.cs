using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Security;
using DevCoreBlog.Services.Newsletters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Reader-only MVC archive; administrative authentication never grants this policy.</summary>
[Authorize(Policy = ReaderCookieAuthenticationEvents.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NewsletterArchiveController(NewsletterArchiveService archive) : Controller
{
    [HttpGet("/bultenler")]
    [HttpHead("/bultenler")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || page is < 1 or > NewsletterArchivePage.MaximumPage)
            return BadRequest("Sayfa 1–1000 arasında olmalıdır.");
        var result = await archive.ReadPageAsync(await SessionAsync(), page, cancellationToken);
        if (result is { Status: ArchiveReadStatus.Ready, Value: { } value })
        {
            if (page > 1 && value.Items.Count == 0) return NotFound();
            return View(new Models.Public.NewsletterArchiveListModel(value, page));
        }
        return Failure(result.Status);
    }

    [HttpGet("/bulten/{slug}")]
    [HttpHead("/bulten/{slug}")]
    public async Task<IActionResult> Detail(string slug, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(slug) || slug.Length > 200) return BadRequest();
        var result = await archive.ReadAsync(await SessionAsync(), slug, cancellationToken);
        return result is { Status: ArchiveReadStatus.Ready, Value: { } value } ? View(value) : Failure(result.Status);
    }

    private async Task<Guid> SessionAsync()
    {
        var auth = await HttpContext.AuthenticateAsync(ReaderCookieAuthenticationEvents.Scheme);
        return ReaderCookieAuthenticationEvents.TrySession(auth.Principal, out var id) ? id : Guid.Empty;
    }
    private IActionResult Failure(ArchiveReadStatus status) => status switch
    {
        ArchiveReadStatus.Denied => Unauthorized(),
        ArchiveReadStatus.NotFound => NotFound(),
        _ => StatusCode(503, "Bülten arşivi şu anda okunamıyor. Daha sonra yeniden deneyin.")
    };
}
