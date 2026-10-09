using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DevCoreBlog.Security;

public sealed class AdminCookieAuthenticationEvents : CookieAuthenticationEvents
{
    // A challenge must not reveal the configured login address in a Location header.
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }

    private readonly AdminSessionStamp _sessionStamp;
    private readonly ILogger<AdminCookieAuthenticationEvents> _logger;

    public AdminCookieAuthenticationEvents(
        AdminSessionStamp sessionStamp,
        ILogger<AdminCookieAuthenticationEvents> logger)
    {
        _sessionStamp = sessionStamp;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var versionClaims = context.Principal?
            .FindAll(AdminSessionPolicy.VersionClaimType)
            .Select(claim => claim.Value)
            .ToArray() ?? [];

        if (versionClaims.Length == 1 && _sessionStamp.Matches(versionClaims[0]))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
        _logger.LogWarning(
            "Rejected an admin authentication ticket with a missing or stale session version.");
    }
}
