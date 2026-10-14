using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Infrastructure;

/// <summary>Global admission precedes IP partitions; registration and reader POST boundaries share the global budget.</summary>
public static class SubscriptionRegistrationRateLimits
{
    public const string Policy = "RegistrationLimiter";
    public static void Configure(RateLimiterOptions options)
    {
        var requests = PartitionedRateLimiter.Create<HttpContext, string>(context => IsRegistration(context)
            ? RateLimitPartition.GetFixedWindowLimiter("registration-global", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })
            : RateLimitPartition.GetNoLimiter("registration-other"));
        var concurrency = PartitionedRateLimiter.Create<HttpContext, string>(context => IsRegistration(context)
            ? RateLimitPartition.GetConcurrencyLimiter("registration-active", _ => new ConcurrencyLimiterOptions
            { PermitLimit = 8, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })
            : RateLimitPartition.GetNoLimiter("registration-other"));
        var providerConcurrency = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerName == "ResendWebhook"
            ? RateLimitPartition.GetConcurrencyLimiter("resend-active", _ => new ConcurrencyLimiterOptions
            { PermitLimit = 4, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })
            : RateLimitPartition.GetNoLimiter("resend-other"));
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(requests, concurrency, providerConcurrency);
        options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown-registration-client", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
    }
    private static bool IsRegistration(HttpContext context) => HttpMethods.IsPost(context.Request.Method)
        && context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerName is "SubscriptionRegistration" or "ReaderAccount";
}
