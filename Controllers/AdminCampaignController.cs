using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Newsletters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Private MVC confirmation and explicit commands; GET never prepares, arms, retries or cancels durable work.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCampaignController(CampaignAdministrationService service, TimeProvider clock) : Controller
{
    [HttpGet]
    public Task<IActionResult> Index(int page = 1, int? postId = null, CancellationToken cancellationToken = default) => Guard(async () =>
    {
        if (!ModelState.IsValid || page is < 1 or > CampaignListPage.MaximumPage || postId is <= 0) return BadRequest();
        var result = await service.FindAsync(page, postId, cancellationToken);
        if (page > 1 && result.Items.Count == 0) return NotFound();
        return View(new CampaignListModel(result, postId));
    }, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Compose(int postId, CancellationToken cancellationToken) => Guard(async () =>
    {
        if (!ModelState.IsValid || postId <= 0) return BadRequest();
        var source = await service.SourceAsync(postId, cancellationToken);
        return source is null ? NotFound() : View(new CampaignComposeModel(source, Guid.NewGuid()));
    }, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Details(Guid id, CancellationToken cancellationToken) => Guard(async () =>
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var detail = await service.ReadAsync(id, cancellationToken);
        return detail is null ? NotFound() : View(new CampaignDetailModel(detail, service.IsReady, service.HasCurrentArchiveLink(detail), clock.GetUtcNow().UtcDateTime));
    }, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("CampaignAdminLimiter")]
    [RequestSizeLimit(4096), RequestFormLimits(ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64)]
    public Task<IActionResult> Prepare(CampaignPrepareInput input, CancellationToken cancellationToken) => Guard(async () =>
    {
        if (!ModelState.IsValid || input.StartKey == Guid.Empty || input.Decision != "prepare") return BadRequest();
        var result = await service.PrepareAsync(input.PostId, input.Revision, input.StartKey, cancellationToken);
        if (result.Status is CampaignStartStatus.Created or CampaignStartStatus.Existing && result.Intent is { } intent)
            return RedirectToAction(nameof(Details), new { id = intent.Id });
        return result.Status switch
        {
            CampaignStartStatus.NotFound => NotFound(),
            CampaignStartStatus.SourceChanged or CampaignStartStatus.Conflict => Result(409, "İçerik değişmiş veya bu hazırlık isteği başka bir sürüme ait. Bülten listesinden yeniden hazırlayın."),
            CampaignStartStatus.NotConfigured => Result(503, "Gönderen bilgileri henüz hazır değil. Bülteni kaydetmeye ve önizlemeye devam edebilirsiniz."),
            _ => Result(400, "Bülten hazırlığı doğrulanamadı. Kayıtlı içeriği kontrol edin.")
        };
    }, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("CampaignAdminLimiter")]
    [RequestSizeLimit(4096), RequestFormLimits(ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64)]
    public Task<IActionResult> Start(CampaignActionInput input, CancellationToken cancellationToken) => Act(input, "start", cancellationToken);

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("CampaignAdminLimiter")]
    [RequestSizeLimit(4096), RequestFormLimits(ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64)]
    public Task<IActionResult> Retry(CampaignActionInput input, CancellationToken cancellationToken) => Act(input, "retry", cancellationToken);

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("CampaignAdminLimiter")]
    [RequestSizeLimit(4096), RequestFormLimits(ValueLengthLimit = 512, ValueCountLimit = 8, KeyLengthLimit = 64)]
    public Task<IActionResult> Cancel(CampaignActionInput input, CancellationToken cancellationToken) => Act(input, "cancel", cancellationToken);

    private Task<IActionResult> Act(CampaignActionInput input, string decision, CancellationToken ct) => Guard(async () =>
    {
        if (!ModelState.IsValid || input.Id == Guid.Empty || input.Decision != decision || !input.Confirm) return BadRequest();
        var result = decision == "cancel" ? await service.CancelAsync(input.Id, input.Version, ct) : await service.StartAsync(input.Id, input.Version, ct);
        return result.Status switch
        {
            CampaignActionStatus.Ready or CampaignActionStatus.Selecting or CampaignActionStatus.Existing or CampaignActionStatus.Cancelled =>
                RedirectToAction(nameof(Details), new { id = input.Id }),
            CampaignActionStatus.NotFound => NotFound(),
            CampaignActionStatus.Conflict => Result(409, "Gönderimin durumu değişmiş veya bu sürüm başlatılamıyor. Güncel durumu açın.", input.Id),
            CampaignActionStatus.Disabled => Result(503, "Toplu gönderim henüz etkin değil. Gönderen ve canlı gönderim kontrolleri tamamlandığında açılır.", input.Id),
            _ => Result(503, "İşlem tamamlanamadı. Güncel durumu açın; aynı gönderimden güvenle devam edebilirsiniz.", input.Id)
        };
    }, ct);

    private IActionResult Result(int status, string message, Guid? id = null)
    {
        Response.StatusCode = status;
        return View("Result", new CampaignResultModel(message, id));
    }
    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        try { return await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Result(503, "Gönderim bilgileri şu anda okunamadı veya işlem kesinleşmedi. Biraz sonra aynı sayfayı yeniden açın."); }
    }
}
