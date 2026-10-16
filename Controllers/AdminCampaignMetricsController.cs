using DevCoreBlog.Services.Newsletters;
using DevCoreBlog.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Private read-only campaign facts; no submission, recovery or polling endpoint.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCampaignMetricsController(CampaignMetricsService service, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest("Geçerli bir gönderim seçin.");
        var observed = clock.GetUtcNow().UtcDateTime;
        try
        {
            var metrics = await service.ReadAsync(id, cancellationToken);
            return metrics is null ? NotFound() : View(new CampaignMetricsModel(metrics, observed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            Response.StatusCode = 503;
            return View("Unavailable");
        }
    }
}
