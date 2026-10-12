using DevCoreBlog.Configuration;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Private unsaved email preview; no persistence, media upload or delivery dependencies.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminEmailController(ContentDocumentValidator validator, DocumentEmailRenderer renderer,
    SiteUrlOptions siteUrl) : Controller
{
    private const int FormRequestBytes = DocumentLimits.Utf8Bytes * 3 + 65_536;

    [HttpGet]
    public IActionResult Preview() => PreviewView(new(), null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(FormRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = FormRequestBytes, ValueCountLimit = 8)]
    public IActionResult Preview([Bind(Prefix = "Input")] DocumentPreviewInput input)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return PreviewView(input, null);
        }
        var validation = validator.Validate(input.DocumentJson);
        if (validation.Document is not { } document)
        {
            var limited = validation.Error?.Kind == DocumentFailureKind.Limit;
            Response.StatusCode = limited ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
            ModelState.AddModelError("Input.DocumentJson", limited
                ? "İçerik belgesi izin verilen sınırları aşıyor."
                : "İçerik belgesi geçersiz. Sürüm, biçim ve bağlantıları kontrol edin.");
            return PreviewView(input, null);
        }
        if (!siteUrl.Origin.StartsWith("https://", StringComparison.Ordinal))
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", "E-posta önizlemesi için güvenilir HTTPS site adresi yapılandırılmalıdır.");
            return PreviewView(input, null);
        }
        // This unsaved snapshot has no canonical post URL or recipient-specific unsubscribe link.
        var context = EmailRenderContext.Create(siteUrl.Origin + "/");
        return PreviewView(input, renderer.Render(document, context));
    }

    private ViewResult PreviewView(DocumentPreviewInput? input, RenderedEmailDocument? rendered)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        return View("Preview", new EmailPreviewModel(input ?? new(), rendered));
    }
}
