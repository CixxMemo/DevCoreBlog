using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace DevCoreBlog.Configuration;

/// <summary>Defines the supported single-server trust boundary without guessing an ingress network.</summary>
public sealed class DeploymentBoundary
{
    public bool UsesNginx { get; }
    public string? ListenUrl { get; }
    public IReadOnlyList<string> AllowedHosts { get; }

    public DeploymentBoundary(bool isDevelopment, SiteUrlOptions siteUrl, string? profile,
        string? listenUrl, bool hasKestrelEndpoints, string? automaticForwarding)
    {
        if (!string.IsNullOrEmpty(automaticForwarding) &&
            !string.Equals(automaticForwarding, "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ASPNETCORE_FORWARDEDHEADERS_ENABLED must be absent or false; unrestricted forwarding is prohibited.");
        if (AppContext.TryGetSwitch("Microsoft.AspNetCore.HttpOverrides.IgnoreUnknownProxiesWithoutFor", out var ignoresTrust) && ignoresTrust)
            throw new InvalidOperationException("The forwarded-header proxy trust bypass switch is prohibited.");

        UsesNginx = profile == "nginx-loopback";
        if ((!isDevelopment && !UsesNginx) || (!string.IsNullOrEmpty(profile) && !UsesNginx))
            throw new InvalidOperationException("DEPLOYMENT_PROFILE must be nginx-loopback outside Development; Development also allows an empty profile.");
        var origin = new Uri(siteUrl.Origin);
        if (!isDevelopment && (!origin.IsDefaultPort || origin.IsLoopback || origin.HostNameType != UriHostNameType.Dns))
            throw new InvalidOperationException("The nginx-loopback production SITE_URL must be a public DNS HTTPS origin on port 443.");
        AllowedHosts = isDevelopment
            ? ["localhost", "127.0.0.1", "[::1]", origin.IdnHost]
            : [origin.IdnHost];

        if (!UsesNginx) return;
        // Endpoint configuration would override/extend UseUrls and could accidentally expose Kestrel.
        if (hasKestrelEndpoints)
            throw new InvalidOperationException("Kestrel:Endpoints is not supported by nginx-loopback; use one loopback HTTP URL.");
        var candidate = listenUrl ?? "http://127.0.0.1:5000";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var listener) ||
            listener.Scheme != Uri.UriSchemeHttp || listener.Host != "127.0.0.1" ||
            listener.UserInfo.Length != 0 || listener.AbsolutePath != "/" ||
            listener.Query.Length != 0 || listener.Fragment.Length != 0 ||
            candidate.Contains(';') || listener.Port is < 1 or > 65535)
            throw new InvalidOperationException("nginx-loopback requires exactly one http://127.0.0.1:<port> listener.");
        ListenUrl = listener.GetLeftPart(UriPartial.Authority);
    }

    public void ConfigureForwarding(ForwardedHeadersOptions options)
    {
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;
        options.ForwardedHeaders = UsesNginx
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;
        if (UsesNginx)
        {
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
        }
        // X-Forwarded-Host/Prefix are never consumed; public identity comes from SITE_URL.
    }
}
