using System.Globalization;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Read-only administrator inventory. Reader cookies never satisfy the default admin scheme.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSubscriberController(SubscriberAdministrationService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, SubscriberListStatus status = SubscriberListStatus.All,
        string? from = null, string? to = null, CancellationToken cancellationToken = default)
    {
        if ((Request.Query.ContainsKey("from") && string.IsNullOrWhiteSpace(from))
            || (Request.Query.ContainsKey("to") && string.IsNullOrWhiteSpace(to))
            || !ModelState.IsValid || page is < 1 or > SubscriberListQuery.MaximumPage || !Enum.IsDefined(status)
            || !ParseDate(from, out var start) || !ParseDate(to, out var end)) return Invalid();
        var dates = service.Dates(start, end);
        if (dates is null) return Invalid();
        try
        {
            var data = await service.ReadAsync(page, status, dates, cancellationToken);
            if (page > 1 && data.Items.Count == 0) return NotFound();
            return View(new SubscriberListModel(data, page, status, dates));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            Response.StatusCode = 503;
            return View("Unavailable");
        }
    }
    private IActionResult Invalid() => BadRequest("Filtreler geçersiz. Sayfa 1–1000 olmalı; tarih aralığı en fazla 366 gün ve bugüne kadar olmalıdır.");
    private static bool ParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null) return true;
        if (value.Length != 10 || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        date = parsed;
        return true;
    }
}
