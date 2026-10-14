using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Models.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Mail scanners may open GET/HEAD safely. Only a protected POST changes sending eligibility.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("UnsubscriptionLimiter")]
public sealed class SubscriberUnsubscriptionController(SubscriberUnsubscriptionService unsubscription) : Controller
{
    [HttpGet(SubscriberExitLinks.Path)]
    [HttpHead(SubscriberExitLinks.Path)]
    public IActionResult Confirm() => View(SubscriberUnsubscriptionPage.Ready);

    [HttpPost(SubscriberExitLinks.Path)]
    [ValidateAntiForgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(4096)]
    [RequestFormLimits(ValueCountLimit = 4, KeyLengthLimit = 64, ValueLengthLimit = 1024)]
    public async Task<IActionResult> Confirm([FromForm] SubscriberUnsubscriptionInput input, CancellationToken cancellationToken)
    {
        var valid = ModelState.IsValid;
        ModelState.Clear();
        if (!valid) return Result(SubscriberUnsubscriptionPage.Invalid, 400);
        try
        {
            var result = await unsubscription.UnsubscribeAsync(input.Token, cancellationToken);
            return result == UnsubscriptionOutcome.Unsubscribed
                ? Result(SubscriberUnsubscriptionPage.Unsubscribed, 200) : Result(SubscriberUnsubscriptionPage.Invalid, 400);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Result(SubscriberUnsubscriptionPage.Unavailable, 503); }
    }

    private ViewResult Result(SubscriberUnsubscriptionPage page, int status)
    {
        Response.StatusCode = status;
        return View("Confirm", page);
    }
}
