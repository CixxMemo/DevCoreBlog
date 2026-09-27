using System.Globalization;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace DevCoreBlog.Caching;

/// <summary>Caches bounded anonymous list responses until a write or publication.</summary>
public sealed class PublicListCachePolicy : IOutputCachePolicy
{
    private const string RequestStateKey = "PublicListCachePolicy.State";
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(60);

    public async ValueTask CacheRequestAsync(
        OutputCacheContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) ||
            context.HttpContext.User.Identity?.IsAuthenticated == true ||
            request.Query.Count > 1 ||
            (request.Query.Count == 1 && !request.Query.ContainsKey("page")) ||
            (request.Query.TryGetValue("page", out var pageValue) &&
                (!int.TryParse(pageValue, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var page) || page is < 1 or > 100)) ||
            (request.RouteValues.TryGetValue("slug", out var slugValue) &&
                (slugValue?.ToString() is not { Length: > 0 and <= 200 })))
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var invalidator = services.GetRequiredService<PublicListCacheInvalidator>();
        var generation = invalidator.Generation;
        var now = clock.GetUtcNow().UtcDateTime;
        var next = await services.GetRequiredService<IPublicationSchedule>()
            .GetNextScheduledPublicationAsync(now, cancellationToken);
        if (invalidator.Generation != generation)
        {
            return;
        }

        context.HttpContext.Items[RequestStateKey] = new RequestState(generation, next);
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.CacheVaryByRules.QueryKeys = new StringValues("page");
        // Public list markup contains no host-derived absolute links.
        context.CacheVaryByRules.VaryByHost = false;
        context.CacheVaryByRules.VaryByValues["publication"] =
            $"{generation}:{next?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "none"}";
    }

    public ValueTask ServeFromCacheAsync(
        OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(
        OutputCacheContext context, CancellationToken cancellationToken)
    {
        if (context.HttpContext.Items[RequestStateKey] is not RequestState state ||
            context.HttpContext.Response.StatusCode != StatusCodes.Status200OK ||
            context.HttpContext.Response.Headers.ContainsKey("Set-Cookie") ||
            context.HttpContext.RequestServices
                .GetRequiredService<PublicListCacheInvalidator>().Generation != state.Generation)
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }

        var now = context.HttpContext.RequestServices
            .GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var lifetime = state.NextPublication is { } next
            ? TimeSpan.FromTicks(Math.Min(MaximumLifetime.Ticks, (next - now).Ticks))
            : MaximumLifetime;
        if (lifetime <= TimeSpan.Zero)
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }

        context.ResponseExpirationTimeSpan = lifetime;
        return ValueTask.CompletedTask;
    }

    private sealed record RequestState(long Generation, DateTime? NextPublication);
}
