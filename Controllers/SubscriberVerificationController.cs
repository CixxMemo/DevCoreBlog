using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Models.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Opening a link only renders confirmation; an antiforgery-protected POST consumes proof without signing in.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("VerificationLimiter")]
public sealed class SubscriberVerificationController(SubscriberVerificationConsumer verification) : Controller
{
    [HttpGet(SubscriberVerificationLinks.Path)]
    [HttpHead(SubscriberVerificationLinks.Path)]
    public IActionResult Confirm() => View(SubscriberVerificationPage.Ready);

    [HttpPost(SubscriberVerificationLinks.Path)]
    [ValidateAntiForgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(8192)]
    [RequestFormLimits(ValueCountLimit = 4, KeyLengthLimit = 64, ValueLengthLimit = 2048)]
    public async Task<IActionResult> Confirm([FromForm] SubscriberVerificationInput input, CancellationToken cancellationToken)
    {
        var valid = ModelState.IsValid;
        ModelState.Clear(); // Never echo a proof through field values or validation messages.
        if (!valid) return Result(SubscriberVerificationPage.Invalid, 400);
        try
        {
            var result = await verification.ConfirmAsync(input.Token, cancellationToken);
            return result == VerificationConsumeOutcome.Confirmed
                ? Result(SubscriberVerificationPage.Confirmed, 200) : Result(SubscriberVerificationPage.Invalid, 400);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Database/provider exception text must never expose a digest, address or posted proof.
            return Result(SubscriberVerificationPage.Unavailable, 503);
        }
    }
    private ViewResult Result(SubscriberVerificationPage page, int status)
    {
        Response.StatusCode = status;
        return View("Confirm", page);
    }
}
