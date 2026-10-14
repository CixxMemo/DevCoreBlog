using System.Security.Claims;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DevCoreBlog.Security;

/// <summary>Explicit reader scheme never becomes the default administrator identity. Every authorized request checks current DB facts.</summary>
public sealed class ReaderCookieAuthenticationEvents(ReaderIdentityService identity) : CookieAuthenticationEvents
{
    public const string Scheme = "AetherReader";
    public const string Policy = "ActiveReader";
    public const string CookieName = "AetherNotes.Reader";
    public const string SessionClaim = "aether:reader-session";
    public const string Unavailable = "ReaderIdentity.Unavailable";
    public static bool TrySession(ClaimsPrincipal? principal, out Guid id)
    {
        id = Guid.Empty;
        var claims = principal?.FindAll(SessionClaim).ToArray();
        return claims is { Length: 1 } && Guid.TryParseExact(claims[0].Value, "N", out id) && id != Guid.Empty;
    }
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            if (TrySession(context.Principal, out var id) && await identity.IsValidAsync(id, deadline.Token)) return;
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Keep the cookie retryable during storage failure, but authorize no request.
            context.RejectPrincipal();
            context.HttpContext.Items[Unavailable] = true;
        }
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = context.HttpContext.Items.ContainsKey(Unavailable) ? 503 : 401;
        return Task.CompletedTask;
    }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
