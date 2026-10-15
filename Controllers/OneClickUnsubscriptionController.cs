using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Only the exact RFC8058 form and purpose-bound URL proof authorize this cookie-independent mutation.</summary>
[AllowAnonymous]
[EnableRateLimiting("OneClickLimiter")]
public sealed class OneClickUnsubscriptionController(OneClickUnsubscriptionService service) : Controller
{
    [HttpGet(OneClickUnsubscriptionLinks.Path)]
    [HttpHead(OneClickUnsubscriptionLinks.Path)]
    public IActionResult Inspect() => StatusCode(204);

    [HttpPost(OneClickUnsubscriptionLinks.Path)]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [RequestSizeLimit(2048)]
    [RequestFormLimits(ValueCountLimit = 1, KeyLengthLimit = 32, ValueLengthLimit = 32,
        MultipartBoundaryLengthLimit = 70, MultipartHeadersLengthLimit = 256, MultipartBodyLengthLimit = 2048)]
    public async Task<IActionResult> Exit(CancellationToken cancellationToken)
    {
        // Bind manually: proof must never become an MVC action argument printed by diagnostics.
        if (Request.Headers.ContainsKey("Cookie") || Request.Headers.ContainsKey("Authorization")) return StatusCode(400);
        if (!Request.IsHttps) return StatusCode(400); // Trusted Nginx forwarding establishes production HTTPS.
        if (Request.QueryString.Value?.Length > 520 || Request.Query.Count != 1 || Request.Query.Keys.Single() != "t" || Request.Query["t"].Count != 1)
            return StatusCode(400);
        if (Request.ContentLength > 2048) return StatusCode(413);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var form = await Request.ReadFormAsync(deadline.Token);
            if (form.Files.Count != 0 || form.Count != 1 || form.Keys.Single() != "List-Unsubscribe"
                || form["List-Unsubscribe"].Count != 1 || form["List-Unsubscribe"][0] != "One-Click") return StatusCode(400);
            var result = await service.ExitAsync(Request.Query["t"][0], deadline.Token);
            return StatusCode(result == UnsubscriptionOutcome.Unsubscribed ? 204 : 400);
        }
        catch (InvalidDataException) { return StatusCode(400); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return StatusCode(413); }
        catch (BadHttpRequestException e) when (e.StatusCode == 400) { return StatusCode(400); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503); }
    }
}
