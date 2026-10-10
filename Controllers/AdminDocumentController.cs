using DevCoreBlog.Core.Documents;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Authorized, non-persistent document preview; it never reads or writes posts.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDocumentController(ContentDocumentValidator validator, DocumentWebRenderer renderer) : Controller
{
    // URL-encoded UTF-8 may require three wire bytes per document byte, plus form metadata.
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
        return PreviewView(input, renderer.Render(document));
    }

    private ViewResult PreviewView(DocumentPreviewInput input, RenderedContentDocument? rendered)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        return View("Preview", new DocumentPreviewModel(input, rendered));
    }
}
