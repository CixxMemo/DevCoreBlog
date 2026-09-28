using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Middlewares;

/// <summary>Keeps HTML and JSON errors on the original request and status code.</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
            if (context.Response.StatusCode == StatusCodes.Status404NotFound &&
                !context.Response.HasStarted && context.Response.ContentType is null &&
                !IsJsonEndpoint(context.Request.Path) &&
                !context.Request.Path.Equals("/Home/NotFoundPage", StringComparison.OrdinalIgnoreCase))
            {
                await RenderPageAsync(context, "/Home/NotFoundPage", StatusCodes.Status404NotFound);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // A disconnected client is not an unexpected server failure.
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            _logger.LogError(
                "Request failed. TraceId: {TraceId}; ExceptionType: {ExceptionType}",
                context.TraceIdentifier, exception.GetType().Name);
            context.Response.Clear();
            context.Response.Headers.CacheControl = "no-store";
            if (IsJsonEndpoint(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(
                    new { success = false, message = "An unexpected error occurred.", traceId = context.TraceIdentifier });
                return;
            }

            try
            {
                await RenderPageAsync(context, "/Home/Error", StatusCodes.Status500InternalServerError);
            }
            catch (Exception renderException) when (!context.Response.HasStarted)
            {
                _logger.LogError(
                    "Error page failed. TraceId: {TraceId}; ExceptionType: {ExceptionType}",
                    context.TraceIdentifier, renderException.GetType().Name);
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(
                    "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Server error</title>" +
                    "<main><h1>Something went wrong</h1><p>Please try again later.</p><p>Request ID: " +
                    HtmlEncoder.Default.Encode(context.TraceIdentifier) + "</p></main></html>");
            }
        }
    }

    private async Task RenderPageAsync(HttpContext context, PathString path, int statusCode)
    {
        var originalPath = context.Request.Path;
        var originalEndpoint = context.GetEndpoint();
        var originalRouteValues = context.Request.RouteValues;
        try
        {
            context.Request.Path = path;
            context.SetEndpoint(null);
            context.Request.RouteValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary();
            context.Response.StatusCode = statusCode;
            await _next(context);
        }
        finally
        {
            context.Request.Path = originalPath;
            context.SetEndpoint(originalEndpoint);
            context.Request.RouteValues = originalRouteValues;
        }
    }

    private static bool IsJsonEndpoint(PathString path)
    {
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWithSegments("/AdminPost/TogglePublish", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/AdminPost/UploadEditorImage", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/AdminPost/UploadImage", StringComparison.OrdinalIgnoreCase);
    }
}
