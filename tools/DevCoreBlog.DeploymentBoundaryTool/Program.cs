using System.Net;
using System.Text.Json;
using DevCoreBlog.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

// Exercise the actual shared-framework middleware, not a reimplementation of header parsing.
var site = new SiteUrlOptions("https://blog.example.test", false);
var checks = new Dictionary<string, bool>();
DeploymentBoundary Create(string? profile = "nginx-loopback", string? listen = null,
    bool endpoints = false, string? automatic = null, bool development = false, SiteUrlOptions? origin = null)
    => new(development, origin ?? site, profile, listen, endpoints, automatic);
void Reject(string name, Func<DeploymentBoundary> action)
{
    try { action(); checks[name] = false; }
    catch (InvalidOperationException) { checks[name] = true; }
}
Reject("missing_production_profile", () => Create(profile: null));
Reject("unknown_profile", () => Create(profile: "invented"));
Reject("public_listener", () => Create(listen: "http://0.0.0.0:5000"));
Reject("wildcard_listener", () => Create(listen: "http://*:5000"));
Reject("multiple_listeners", () => Create(listen: "http://127.0.0.1:5000;http://0.0.0.0:5001"));
Reject("implicit_localhost_listener", () => Create(listen: "http://localhost:5000"));
Reject("overriding_kestrel_endpoints", () => Create(endpoints: true));
Reject("automatic_forwarding_bypass", () => Create(automatic: "true"));
Reject("malformed_automatic_forwarding", () => Create(automatic: "1"));
Reject("nonstandard_public_https_port", () => Create(origin: new SiteUrlOptions("https://blog.example.test:8443", false)));
Reject("production_loopback_origin", () => Create(origin: new SiteUrlOptions("https://localhost", false)));
Reject("listener_credentials", () => Create(listen: "http://placeholder:placeholder@127.0.0.1:5000"));
Reject("listener_path", () => Create(listen: "http://127.0.0.1:5000/path"));
AppContext.SetSwitch("Microsoft.AspNetCore.HttpOverrides.IgnoreUnknownProxiesWithoutFor", true);
Reject("legacy_trust_bypass", () => Create());
AppContext.SetSwitch("Microsoft.AspNetCore.HttpOverrides.IgnoreUnknownProxiesWithoutFor", false);
var production = Create();
checks["single_listener_and_exact_host"] = production.ListenUrl == "http://127.0.0.1:5000" &&
    production.AllowedHosts.SequenceEqual(["blog.example.test"]);
var developer = Create(profile: null, development: true);
checks["development_allows_local_connections"] = !developer.UsesNginx && developer.AllowedHosts.Contains("127.0.0.1");

using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
async Task<(string Ip, string Scheme, string Host)> Resolve(DeploymentBoundary boundary,
    string remote, string? ip, string? scheme, string? spoofHost = null)
{
    var options = new ForwardedHeadersOptions(); boundary.ConfigureForwarding(options);
    var app = new ApplicationBuilder(services); app.UseForwardedHeaders(options); app.Run(_ => Task.CompletedTask);
    var context = new DefaultHttpContext { RequestServices = services };
    context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
    context.Request.Scheme = "http"; context.Request.Host = new HostString("blog.example.test");
    if (ip is not null) context.Request.Headers["X-Forwarded-For"] = ip;
    if (scheme is not null) context.Request.Headers["X-Forwarded-Proto"] = scheme;
    context.Request.Headers["X-Forwarded-Host"] = spoofHost;
    await app.Build()(context);
    return (context.Connection.RemoteIpAddress?.ToString() ?? "", context.Request.Scheme, context.Request.Host.Value ?? "");
}
var trusted = await Resolve(production, "127.0.0.1", "198.51.100.12", "https", "evil.example");
checks["trusted_proxy_resolves_ip_and_https_but_never_host"] = trusted == ("198.51.100.12", "https", "blog.example.test");
var spoof = await Resolve(production, "192.0.2.44", "198.51.100.12", "https", "evil.example");
checks["unknown_proxy_cannot_change_ip_scheme_host"] = spoof == ("192.0.2.44", "http", "blog.example.test");
var chain = await Resolve(production, "127.0.0.1", "203.0.113.99, 198.51.100.12", "http, https");
checks["one_hop_ignores_client_supplied_chain_prefix"] = chain.Ip == "198.51.100.12" && chain.Scheme == "https";
var asymmetric = await Resolve(production, "127.0.0.1", "203.0.113.99, 198.51.100.12", "https");
checks["asymmetric_headers_are_not_consumed"] = asymmetric.Ip == "127.0.0.1" && asymmetric.Scheme == "http";
var noIp = await Resolve(production, "127.0.0.1", null, "https");
checks["scheme_only_is_not_trusted"] = noIp.Scheme == "http";
var malformed = await Resolve(production, "127.0.0.1", "malformed", "https");
checks["invalid_ip_does_not_upgrade_scheme"] = malformed.Scheme == "http";
var mapped = await Resolve(production, "::ffff:127.0.0.1", "198.51.100.12", "https");
checks["ipv4_mapped_loopback_is_supported"] = mapped.Scheme == "https" && mapped.Ip == "198.51.100.12";
var otherLoopback = await Resolve(production, "127.0.0.2", "198.51.100.12", "https");
checks["entire_loopback_network_is_not_trusted"] = otherLoopback.Scheme == "http";
var dev = await Resolve(developer, "127.0.0.1", "198.51.100.12", "https");
checks["ordinary_development_ignores_forwarded_headers"] = dev.Scheme == "http" && dev.Ip == "127.0.0.1";
Console.WriteLine(JsonSerializer.Serialize(new { checks, count = checks.Count }, new JsonSerializerOptions { WriteIndented = true }));
return checks.Values.All(value => value) ? 0 : 1;
