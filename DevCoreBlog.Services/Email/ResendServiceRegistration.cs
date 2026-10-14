using DevCoreBlog.Core.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevCoreBlog.Services.Email;

/// <summary>Registers the fixed-origin, cookie-free transport with no redirects, retries or HTTP logging.</summary>
public static class ResendServiceRegistration
{
    public static IServiceCollection AddResendEmail(this IServiceCollection services, ResendOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IEmailSubmissionContext>(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // Adapter deadline covers headers and response stream together.
            client.MaxResponseContentBufferSize = ResendEmailSender.MaximumResponseBytes;
        })
        .RemoveAllLoggers()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        return services;
    }
}
