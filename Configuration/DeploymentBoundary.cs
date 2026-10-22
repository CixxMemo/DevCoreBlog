using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace DevCoreBlog.Configuration;

/// <summary>Defines the supported single-server trust boundary without guessing an ingress network.</summary>
public sealed class DeploymentBoundary
{
    public const string LoopbackProfile = "nginx-loopback";
    public const string DockerProfile = "nginx-docker";

    public bool UsesNginx { get; }
    public string? ListenUrl { get; }
    // Exactly one private proxy address trusted by the docker-network profile.
    private readonly IPAddress? _dockerProxy;
    public IReadOnlyList<string> AllowedHosts { get; }

    public DeploymentBoundary(bool isDevelopment, SiteUrlOptions siteUrl, string? profile,
        string? listenUrl, bool hasKestrelEndpoints, string? automaticForwarding, string? dockerProxyAddress = null)
    {
        if (!string.IsNullOrEmpty(automaticForwarding) &&
            !string.Equals(automaticForwarding, "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ASPNETCORE_FORWARDEDHEADERS_ENABLED must be absent or false; unrestricted forwarding is prohibited.");
        if (AppContext.TryGetSwitch("Microsoft.AspNetCore.HttpOverrides.IgnoreUnknownProxiesWithoutFor", out var ignoresTrust) && ignoresTrust)
            throw new InvalidOperationException("The forwarded-header proxy trust bypass switch is prohibited.");

        var docker = profile == DockerProfile;
        UsesNginx = profile == LoopbackProfile || docker;
        if ((!isDevelopment && !UsesNginx) || (!string.IsNullOrEmpty(profile) && !UsesNginx))
            throw new InvalidOperationException("DEPLOYMENT_PROFILE must be nginx-loopback or nginx-docker outside Development; Development also allows an empty profile.");
        if (!docker && !string.IsNullOrEmpty(dockerProxyAddress))
            throw new InvalidOperationException("DEPLOYMENT_PROXY_IP is only valid with the nginx-docker profile.");
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
        if (docker)
        {
            ConfigureDocker(listenUrl, dockerProxyAddress, out var dockerListener, out _dockerProxy);
            ListenUrl = dockerListener;
            return;
        }
        var candidate = listenUrl ?? "http://127.0.0.1:5000";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var listener) ||
            listener.Scheme != Uri.UriSchemeHttp || listener.Host != "127.0.0.1" ||
            listener.UserInfo.Length != 0 || listener.AbsolutePath != "/" ||
            listener.Query.Length != 0 || listener.Fragment.Length != 0 ||
            candidate.Contains(';') || listener.Port is < 1 or > 65535)
            throw new InvalidOperationException("nginx-loopback requires exactly one http://127.0.0.1:<port> listener.");
        ListenUrl = listener.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Docker network variant: the proxy is another container, so loopback trust cannot apply. Both the
    /// listener and the single trusted proxy must be explicit private IPv4 addresses; nothing is published to the host.
    /// </summary>
    private static void ConfigureDocker(string? listenUrl, string? proxyAddress, out string listen, out IPAddress proxy)
    {
        if (listenUrl is null || proxyAddress is null)
            throw new InvalidOperationException("nginx-docker requires ASPNETCORE_URLS and DEPLOYMENT_PROXY_IP.");
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var listener) ||
            listener.Scheme != Uri.UriSchemeHttp || listener.HostNameType != UriHostNameType.IPv4 ||
            !IsPrivateIPv4(IPAddress.Parse(listener.Host)) || listener.UserInfo.Length != 0 ||
            listener.AbsolutePath != "/" || listener.Query.Length != 0 || listener.Fragment.Length != 0 ||
            listenUrl.Contains(';') || listener.Port is < 1 or > 65535)
            throw new InvalidOperationException("nginx-docker requires exactly one http://<private IPv4>:<port> listener.");
        if (!IPAddress.TryParse(proxyAddress, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !IsPrivateIPv4(parsed) || parsed.ToString() != proxyAddress || parsed.Equals(IPAddress.Parse(listener.Host)))
            throw new InvalidOperationException("DEPLOYMENT_PROXY_IP must be one canonical private IPv4 address different from the listener.");
        listen = listener.GetLeftPart(UriPartial.Authority);
        proxy = parsed;
    }

    // RFC 1918 only; loopback, link-local, wildcard and public addresses are rejected.
    private static bool IsPrivateIPv4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
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
        if (_dockerProxy is not null)
        {
            options.KnownProxies.Add(_dockerProxy);
            options.KnownProxies.Add(_dockerProxy.MapToIPv6());
        }
        else if (UsesNginx)
        {
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
        }
        // X-Forwarded-Host/Prefix are never consumed; public identity comes from SITE_URL.
    }
}
