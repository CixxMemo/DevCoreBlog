namespace DevCoreBlog.Middlewares;

/// <summary>Correlates safe application events with a server-assigned request identifier.</summary>
public sealed class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, DevCoreBlog.Configuration.AdminLoginOptions login)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = context.TraceIdentifier });
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        await next(context);
        if (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by client.");
            return;
        }
        var area = context.Request.Path.StartsWithSegments("/api/webhooks") ? "Webhook" :
            context.Request.Path.StartsWithSegments("/Account") || context.Request.Path.Equals(login.Path, StringComparison.OrdinalIgnoreCase) ? "Authentication" :
            context.Request.Path.StartsWithSegments("/AdminPost/UploadEditorImage") ||
            context.Request.Path.StartsWithSegments("/AdminPost/UploadImage") ? "Upload" : null;
        if (area is not null && context.Response.StatusCode >= 400)
            logger.LogWarning("{Operation} request rejected; StatusCode: {StatusCode}.", area, context.Response.StatusCode);
    }
}
