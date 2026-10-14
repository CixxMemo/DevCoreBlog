using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Infrastructure;
using DevCoreBlog.Models.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Public ownership request boundary; generic responses never identify existing subscriptions.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubscriptionRegistrationController(SubscriptionRegistrationService registration,
    SubscriptionRegistrationAvailability availability) : Controller
{
    public const string Path = "/abone-ol";
    [HttpGet(Path)]
    [HttpHead(Path)]
    public IActionResult Index() => View(new SubscriptionRegistrationPage { Available = availability.CanRegister });

    [HttpPost(Path)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(SubscriptionRegistrationRateLimits.Policy)]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(4096)]
    [RequestFormLimits(ValueCountLimit = 4, KeyLengthLimit = 64, ValueLengthLimit = 1024)]
    public async Task<IActionResult> Index([FromForm] SubscriptionRegistrationInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Invalid(input);
        try
        {
            var result = await registration.RegisterAsync(input.Email, cancellationToken);
            if (result == SubscriptionRegistrationOutcome.InvalidAddress)
            {
                ModelState.AddModelError("Input.Email", "Geçerli, tek bir e-posta adresi girin.");
                return Invalid(input);
            }
            ModelState.Clear();
            Response.StatusCode = result == SubscriptionRegistrationOutcome.Unavailable ? 503 : 200;
            return View(new SubscriptionRegistrationPage { Available = availability.CanRegister,
                Accepted = result == SubscriptionRegistrationOutcome.Accepted, Failed = result == SubscriptionRegistrationOutcome.Unavailable });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            ModelState.Clear(); Response.StatusCode = 503;
            return View(new SubscriptionRegistrationPage { Available = availability.CanRegister, Failed = true });
        }
    }
    private ViewResult Invalid(SubscriptionRegistrationInput input)
    {
        Response.StatusCode = 400;
        return View("Index", new SubscriptionRegistrationPage { Available = availability.CanRegister, Input = input });
    }
}
