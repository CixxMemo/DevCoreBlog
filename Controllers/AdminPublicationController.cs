using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Private settings form; saving never changes environment, DNS, source posts or existing sending intent.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPublicationController(PublicationSettingsService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var page = await service.ReadAsync(cancellationToken);
            return View(new PublicationSettingsModel(new() { Name = page.Settings?.Name ?? PublicationSettings.DefaultName,
                Cadence = page.Settings?.Cadence ?? PublicationSettings.DefaultCadence, Version = page.Settings?.Version ?? 0,
                ApproveSender = page.SenderApproved, SenderEmail = page.ConfiguredSender?.Email, SenderName = page.ConfiguredSender?.Name }, page));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("PublicationSettingsLimiter"), Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(4096), RequestFormLimits(ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64)]
    public async Task<IActionResult> Save(PublicationSettingsInput input, CancellationToken cancellationToken)
    {
        try
        {
            string[] allowed = ["Name", "Cadence", "Version", "ApproveSender", "SenderEmail", "SenderName", "__RequestVerificationToken"];
            if (!ModelState.IsValid || input.Version is null || Request.Form.Keys.Any(k => !allowed.Contains(k, StringComparer.Ordinal)))
                return await Failed(400, input, "Alanlar geçersiz. Yalnız yayın adı, ritim bilgisi ve yapılandırılmış gönderen onayını kaydedebilirsiniz.", cancellationToken);
            var result = await service.SaveAsync(input.Name, input.Cadence, input.Version.Value, input.ApproveSender, input.SenderEmail, input.SenderName, cancellationToken);
            if (result is PublicationSaveStatus.Saved or PublicationSaveStatus.Unchanged)
            {
                TempData["Success"] = result == PublicationSaveStatus.Saved ? "Yayın ayarları kaydedildi." : "Yayın ayarları zaten güncel.";
                return RedirectToAction(nameof(Index));
            }
            return await Failed(result == PublicationSaveStatus.Conflict ? 409 : 400, input, result switch
            {
                PublicationSaveStatus.Conflict => "Ayarlar başka bir işlemde değişmiş veya saat geriye gitmiş. Sayfayı yenileyip güncel değerlerle tekrar deneyin.",
                PublicationSaveStatus.SenderMismatch => "Gönderen yapılandırmayla eşleşmiyor. Güncel ayarları yeniden açın; farklı bir gönderici adresi bu formdan atanamaz.",
                _ => "Yayın adı en fazla 80, ritim bilgisi en fazla 200 karakter olmalı; boş değer, HTML veya kontrol karakteri kullanmayın."
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }
    private async Task<IActionResult> Failed(int status, PublicationSettingsInput input, string error, CancellationToken ct)
    {
        var page = await service.ReadAsync(ct); Response.StatusCode = status;
        return View("Index", new PublicationSettingsModel(input, page, error));
    }
    private IActionResult Unavailable() { Response.StatusCode = 503; return View("Unavailable"); }
}
