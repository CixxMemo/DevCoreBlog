using DevCoreBlog.Services.Subscriptions;

namespace DevCoreBlog.Middlewares;

/// <summary>RFC8058 requires no POST redirects; refuse plaintext requests before the general HTTPS redirect middleware.</summary>
public sealed class OneClickTransportBoundary(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if(OneClickUnsubscriptionLinks.IsPath(context.Request.Path.Value) && !context.Request.IsHttps)
        {context.Response.StatusCode=400;return Task.CompletedTask;}
        return next(context);
    }
}
