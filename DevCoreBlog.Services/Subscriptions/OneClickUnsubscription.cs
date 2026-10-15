using DevCoreBlog.Core.Subscriptions;
using Microsoft.AspNetCore.DataProtection;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>RFC8058 HTTPS token identifies only the newsletter subscription period; it cannot sign in or confirm ownership.</summary>
public sealed class OneClickUnsubscriptionLinks
{
    public const string Path = "/abonelik/tek-tik";
    public static bool IsPath(string? path) => string.Equals(path?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase);
    private readonly IDataProtector protector;
    private readonly string origin;
    public bool HasHttpsOrigin { get; }
    public string Origin => origin;
    public OneClickUnsubscriptionLinks(IDataProtectionProvider protection, SubscriberVerificationLinks links)
    {
        protector = protection.CreateProtector("AetherNotes.NewsletterOneClickExit.v1");
        origin = links.Origin;
        HasHttpsOrigin = new Uri(origin).Scheme == "https";
    }
    public string Create(SubscriberExitProof proof)
    {
        if (!HasHttpsOrigin) throw new InvalidOperationException("HTTPS unsubscribe origin required.");
        return origin + Path + "?t=" + SubscriberExitProofCodec.Protect(protector, proof);
    }
    public bool TryRead(string? token, out SubscriberExitProof? proof) => SubscriberExitProofCodec.TryRead(protector, token, out proof);
}

/// <summary>Reuses the same atomic exit rule; configuration and provider availability never prevent a valid exit.</summary>
public sealed class OneClickUnsubscriptionService(OneClickUnsubscriptionLinks links, ISubscriberUnsubscriptionStore store, TimeProvider clock)
{
    public async Task<UnsubscriptionOutcome> ExitAsync(string? token, CancellationToken cancellationToken)
    {
        if (!links.TryRead(token, out var proof) || proof is null) return UnsubscriptionOutcome.Invalid;
        return await store.UnsubscribeAsync(proof, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
