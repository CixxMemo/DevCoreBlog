using DevCoreBlog.Core.Documents;
using DevCoreBlog.Extensions;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DevCoreBlog.Controllers;

/// <summary>Private JSON writing forms translate HTTP input and expected outcomes only.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class DocumentWritingControllerBase(IPostWritingUseCase writing, WritingCategoryService categories,
    PublicationTimeZone zone, TimeProvider clock, ContentDocumentValidator validator) : Controller
{
    protected abstract bool IsNewsletter { get; }
    private const int FormBytes = DocumentLimits.Utf8Bytes * 3 + 65_536;
    [HttpGet]
    public Task<IActionResult> Create() => ShowAsync(new() {
        DocumentJson = "{\"version\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}}",
        PublishDate = zone.ToSiteTime(clock.GetUtcNow().UtcDateTime)
    });

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!SafeReturn(returnUrl)) return BadRequest();
        var read = await writing.ReadAsync(id, cancellationToken);
        if (read is null) return NotFound();
        var p = read.Metadata;
        return await ShowAsync(new() { Id = id, EditVersion = p.EditVersion, Title = p.Title,
            DocumentJson = read.Json ?? string.Empty, CategoryId = p.CategoryId, Summary = p.Summary,
            Excerpt = p.Excerpt, ThumbnailAlt = p.ThumbnailAlt, IsActive = p.IsActive,
            PublishDate = zone.ToSiteTime(p.PublishDate), SaveAction = PostSaveAction.Save,
            Slug = p.Slug, ReturnUrl = returnUrl,
            RecordedState = read.State, BlockingMessage = read.Json is null ? "Bu belge güvenle açılamadı. Kaydetme kapalı; kayıtlı içerik korunuyor." : null });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(FormBytes)]
    [RequestFormLimits(ValueLengthLimit = FormBytes, ValueCountLimit = 32, MultipartBodyLengthLimit = FormBytes)]
    public Task<IActionResult> Create(DocumentPostFormInput input, CancellationToken cancellationToken)
    {
        // Route, never a hidden input, owns creation identity.
        input.Id = 0; input.EditVersion = 0;
        return SaveAsync(input, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(FormBytes)]
    [RequestFormLimits(ValueLengthLimit = FormBytes, ValueCountLimit = 32, MultipartBodyLengthLimit = FormBytes)]
    public Task<IActionResult> Edit(int id, DocumentPostFormInput input, string? returnUrl, CancellationToken cancellationToken)
    {
        if (id <= 0 || id != input.Id) return Task.FromResult<IActionResult>(NotFound());
        if (!SafeReturn(returnUrl)) return Task.FromResult<IActionResult>(BadRequest());
        input.ReturnUrl = returnUrl;
        return SaveAsync(input, cancellationToken);
    }

    private async Task<IActionResult> SaveAsync(DocumentPostFormInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { Response.StatusCode = 400; return await ShowAsync(input); }
        try
        {
            if (!(await categories.ReadAsync(cancellationToken)).Complete)
            {
                input.BlockingMessage = "Kategori listesi sınırı aşıldı. Kaydetme kapalı; mevcut içerik korunuyor.";
                Response.StatusCode = 503;
                return await ShowAsync(input);
            }
        }
        catch (EditorPostStorageException)
        {
            input.BlockingMessage = "Kategori listesi okunamadı. Kaydetme kapalı; mevcut içerik korunuyor.";
            Response.StatusCode = 503;
            return await ShowAsync(input);
        }
        var result = await writing.SaveAsync(new(input.Id, input.EditVersion, input.Title, input.DocumentJson,
            input.CategoryId, input.Summary ?? string.Empty, input.Excerpt ?? string.Empty,
            input.ThumbnailAlt, input.IsActive, input.PublishDate, input.SaveAction), cancellationToken);
        if (result.Status == EditorPostSaveStatus.NotFound) return NotFound();
        if (result.Status == EditorPostSaveStatus.Saved)
        {
            if (result.Id is not int savedId || savedId <= 0)
                throw new InvalidOperationException("A committed writing result requires a persisted identity.");
            TempData["DocumentSaved"] = IsNewsletter ? "Bülten kaydedildi. E-posta gönderilmedi." : "Yazı kaydedildi.";
            // Only a committed save can issue a receipt; route-bound identity owns its source key.
            if (input.RecoveryRevision is { Length: 36 } revision)
                TempData["DocumentRecoveryReceipt"] = JsonSerializer.Serialize(new DocumentRecoveryReceipt(savedId, input.Id, revision));
            return RedirectToAction(nameof(Edit), new { id = savedId, returnUrl = input.ReturnUrl });
        }
        Response.StatusCode = result.Status switch { EditorPostSaveStatus.Limit => 413, EditorPostSaveStatus.Conflict => 409,
            EditorPostSaveStatus.Unavailable => 503, _ => 400 };
        foreach (var error in result.Errors) ModelState.AddModelError(error.Field, error.Message);
        return await ShowAsync(input);
    }

    private async Task<IActionResult> ShowAsync(DocumentPostFormInput input)
    {
        if (Request.Method == "GET" && TempData.Peek("DocumentRecoveryReceipt") is string serialized)
        {
            var receipt = JsonSerializer.Deserialize<DocumentRecoveryReceipt>(serialized);
            if (receipt is not null && receipt.PostId == input.Id)
            {
                input.RecoveryReceipt = receipt;
                TempData.Remove("DocumentRecoveryReceipt");
            }
        }
        ModelState.PrepareBooleanRedisplay(nameof(input.IsActive), input.IsActive);
        input.IsNewsletter = IsNewsletter;
        input.SiteTimeZoneId = zone.Id;
        input.EditorEnabled = input.BlockingMessage is null && !string.IsNullOrWhiteSpace(input.DocumentJson) &&
            validator.Validate(input.DocumentJson).Document is not null;
        try
        {
            var choices = await categories.ReadAsync(HttpContext.RequestAborted);
            input.Categories = choices.Items;
            if (!choices.Complete)
            {
                input.BlockingMessage = "Kategori listesi sınırı aşıldı. Kaydetme kapalı; mevcut içerik korunuyor.";
                Response.StatusCode = 503;
            }
        }
        catch (EditorPostStorageException)
        {
            input.BlockingMessage = "Kategori listesi okunamadı. Kaydetme kapalı; mevcut içerik korunuyor.";
            Response.StatusCode = 503;
        }
        if (input.BlockingMessage is not null) input.EditorEnabled = false;
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        return View("~/Views/AdminWriting/Editor.cshtml", input);
    }

    private bool SafeReturn(string? url) => url is null || (url.Length <= 2048 && Url.IsLocalUrl(url) &&
        !url.Any(char.IsControl) && url.Split('?')[0].TrimEnd('/').Equals(IsNewsletter ? "/AdminNewsletter" : "/AdminPost", StringComparison.OrdinalIgnoreCase));
}
