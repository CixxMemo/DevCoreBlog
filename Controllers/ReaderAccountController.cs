using System.Security.Claims;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Models.Subscriptions;
using DevCoreBlog.Security;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Reader HTTP boundary only. Opening a link never signs in; a protected POST consumes one proof.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReaderAccountController(ReaderIdentityService identity, ReaderLoginAvailability availability) : Controller
{
    [HttpGet("/sign-in")]
    [HttpHead("/sign-in")]
    public IActionResult SignIn() => View(new ReaderLoginPage { Available = availability.CanRequest });

    [HttpPost("/sign-in")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ReaderRequestLimiter")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(4096)]
    [RequestFormLimits(ValueCountLimit = 4, KeyLengthLimit = 64, ValueLengthLimit = 512)]
    public async Task<IActionResult> SignIn([FromForm] ReaderLoginInput input, CancellationToken ct)
    {
        var valid = ModelState.IsValid;
        ModelState.Clear();
        if (!valid) return LoginResult(400, error: "Geçerli, tek bir e-posta adresi girin.");
        try
        {
            var result = await identity.RequestAsync(input.Email, ct);
            return result switch
            {
                ReaderLoginRequestOutcome.InvalidAddress => LoginResult(400, error: "Geçerli, tek bir e-posta adresi girin."),
                ReaderLoginRequestOutcome.Unavailable => LoginResult(503, failed: true),
                _ => LoginResult(200, accepted: true)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return LoginResult(503, failed: true); }
    }

    [HttpGet(ReaderLoginMessageFactory.Path)]
    [HttpHead(ReaderLoginMessageFactory.Path)]
    [EnableRateLimiting("ReaderProofLimiter")]
    public IActionResult Confirm(string? returnUrl)
    {
        if (!ValidReturn(returnUrl)) return Confirmation(ReaderLoginConfirmation.Invalid, 400);
        ViewData["ReturnUrl"] = returnUrl ?? "/abonelik/oturum";
        return Confirmation(ReaderLoginConfirmation.Ready, 200);
    }

    [HttpPost(ReaderLoginMessageFactory.Path)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ReaderProofLimiter")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(4096)]
    [RequestFormLimits(ValueCountLimit = 4, KeyLengthLimit = 64, ValueLengthLimit = 512)]
    public async Task<IActionResult> Confirm([FromForm] ReaderLoginProof input, CancellationToken ct)
    {
        var valid = ModelState.IsValid && ValidReturn(input.ReturnUrl);
        ModelState.Clear();
        if (!valid) return Confirmation(ReaderLoginConfirmation.Invalid, 400);
        try
        {
            var grant = await identity.ConsumeAsync(input.Token, ct);
            if (grant is null) return Confirmation(ReaderLoginConfirmation.Invalid, 400);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ReaderCookieAuthenticationEvents.SessionClaim, grant.Id.ToString("N")),
                 new Claim(ClaimTypes.NameIdentifier, grant.Id.ToString("N"))], ReaderCookieAuthenticationEvents.Scheme));
            await HttpContext.SignInAsync(ReaderCookieAuthenticationEvents.Scheme, principal, new AuthenticationProperties
            {
                IssuedUtc = new DateTimeOffset(grant.CreatedUtc), ExpiresUtc = new DateTimeOffset(grant.ExpiresUtc),
                IsPersistent = true, AllowRefresh = false
            });
            // A fresh GET gets an antiforgery token bound to the new identity; never echo the consumed proof.
            return LocalRedirect(input.ReturnUrl ?? "/abonelik/oturum");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Confirmation(ReaderLoginConfirmation.Unavailable, 503); }
    }

    [HttpGet("/abonelik/oturum")]
    [HttpHead("/abonelik/oturum")]
    [Authorize(Policy = ReaderCookieAuthenticationEvents.Policy)]
    public IActionResult Session() => View();

    [Authorize(Policy = ReaderCookieAuthenticationEvents.Policy)]
    [HttpPost("/abonelik/oturumu-kapat")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4096)]
    [RequestFormLimits(ValueCountLimit = 2, KeyLengthLimit = 64, ValueLengthLimit = 512)]
    public async Task<IActionResult> SignOutReader(CancellationToken ct)
    {
        try
        {
            var auth = await HttpContext.AuthenticateAsync(ReaderCookieAuthenticationEvents.Scheme);
            if (HttpContext.Items.ContainsKey(ReaderCookieAuthenticationEvents.Unavailable))
                return Confirmation(ReaderLoginConfirmation.Unavailable, 503);
            if (ReaderCookieAuthenticationEvents.TrySession(auth.Principal, out var id)) await identity.RevokeAsync(id, ct);
            await HttpContext.SignOutAsync(ReaderCookieAuthenticationEvents.Scheme);
            return LocalRedirect("/sign-in");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Confirmation(ReaderLoginConfirmation.Unavailable, 503); }
    }

    private bool ValidReturn(string? value) => value is null || (value.Length is > 0 and <= 512 && Url.IsLocalUrl(value)
        && !value.Any(c => char.IsControl(c) || c is '\\' or '%' or '?' or '#')
        && (value == "/abonelik/oturum" || value == "/bulten" || value == "/bultenler" || value.StartsWith("/bulten/", StringComparison.Ordinal)));
    private ViewResult LoginResult(int status, bool accepted = false, bool failed = false, string? error = null)
    {
        Response.StatusCode = status;
        return View("SignIn", new ReaderLoginPage { Available = availability.CanRequest, Accepted = accepted, Failed = failed, Error = error });
    }
    private ViewResult Confirmation(ReaderLoginConfirmation state, int status)
    {
        Response.StatusCode = status;
        return View("Confirm", state);
    }
}
