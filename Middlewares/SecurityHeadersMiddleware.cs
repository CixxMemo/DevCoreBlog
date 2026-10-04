using Microsoft.AspNetCore.Mvc.Controllers;

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

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                // The pinned editor sets dynamic sizing/position style attributes; other pages need none.
                var isEditor = context.Response.StatusCode == StatusCodes.Status200OK &&
                    action?.ControllerName == "AdminPost" && action.ActionName is "Create" or "Edit";
                context.Response.Headers["Content-Security-Policy"] = CommonPolicy +
                    (isEditor ? "style-src-attr 'unsafe-inline'" : "style-src-attr 'none'");
            }
            return Task.CompletedTask;
        });
        await next(context);
    }
}
