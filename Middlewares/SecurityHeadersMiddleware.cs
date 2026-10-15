using Microsoft.AspNetCore.Mvc.Controllers;
using DevCoreBlog.Services.Documents;
using System.Security.Cryptography;
using System.Text;

namespace DevCoreBlog.Middlewares;

/// <summary>Enforces the proven HTML policy and restores response headers after error handling.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string CommonPolicy =
        "default-src 'none'; script-src 'self'; script-src-attr 'none'; " +
        "style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        // HTTPS images preserve the existing Markdown/legacy-cover contract; data icons come from editor CSS.
        "img-src 'self' https: data:; connect-src 'self'; " +
        "frame-src https://www.youtube-nocookie.com; object-src 'none'; " +
        "base-uri 'none'; form-action 'self'; frame-ancestors 'none'; ";

    private static readonly string EmailStylePolicy = "style-src-attr 'unsafe-hashes' " +
        string.Join(' ', EmailDocumentStyles.All.Select(style => "'sha256-" +
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style))) + "'"));

    public async Task InvokeAsync(HttpContext context)
    {
        // Error-page re-execution changes endpoint metadata before OnStarting; retain archive privacy for that response.
        var privateArchivePath = context.Request.Path.Equals("/bultenler", StringComparison.OrdinalIgnoreCase)
            || (context.Request.Path.StartsWithSegments("/bulten", StringComparison.OrdinalIgnoreCase, out var remainder) && remainder.HasValue)
            || context.Request.Path.Equals(Controllers.ResendWebhookController.Path, StringComparison.OrdinalIgnoreCase)
            || DevCoreBlog.Services.Subscriptions.OneClickUnsubscriptionLinks.IsPath(context.Request.Path.Value)
            || context.Request.Path.StartsWithSegments("/AdminTestEmail", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/AdminCampaign", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/AdminSubscriber", StringComparison.OrdinalIgnoreCase);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (privateArchivePath || action?.ControllerName is "SubscriberVerification" or "SubscriptionRegistration" or "SubscriberUnsubscription" or "OneClickUnsubscription" or "ReaderAccount" or "NewsletterArchive")
            {
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            }
            if (action?.ControllerName is "AdminEmail" or "AdminTestEmail" or "AdminNewsletter" or "AdminWriting")
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            }
            if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                // The pinned editor sets dynamic sizing/position style attributes; other pages need none.
                var isEditor = context.Response.StatusCode == StatusCodes.Status200OK &&
                    action?.ControllerName == "AdminPost" && action.ActionName is "Create" or "Edit";
                context.Response.Headers["Content-Security-Policy"] = CommonPolicy +
                    (isEditor ? "style-src-attr 'unsafe-inline'" :
                        context.Response.StatusCode == StatusCodes.Status200OK &&
                        action?.ControllerName == "AdminEmail" && action.ActionName == "Preview"
                            ? EmailStylePolicy : "style-src-attr 'none'");
            }
            return Task.CompletedTask;
        });
        await next(context);
    }
}
