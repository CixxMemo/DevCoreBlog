using DevCoreBlog.Core.Subscriptions;
using Microsoft.AspNetCore.DataProtection;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Purpose-isolated, authenticated encrypted proof; no address, identity or revision appears in cleartext links.</summary>
public sealed class SubscriberExitLinks
{
    public const string Path = "/abonelik/cik";
    public const int MaximumTokenLength = 512;
    private readonly IDataProtector protector;
    private readonly string origin;

    public SubscriberExitLinks(IDataProtectionProvider protection, SubscriberVerificationLinks trustedLinks)
    {
        protector = protection.CreateProtector("AetherNotes.SubscriptionExit.v1");
        origin = trustedLinks.Origin;
    }

    // Issuance is internal to mail composition. There is no public address-to-link endpoint.
    public string Create(SubscriberSnapshot subscriber)
    {
        if (subscriber.Id == Guid.Empty || subscriber.PeriodId == Guid.Empty || subscriber.Status == SubscriptionStatus.Unsubscribed)
            throw new ArgumentException("A current subscription period is required.");
        return Create(new SubscriberExitProof(subscriber.Id, subscriber.PeriodId, SubscriberVerificationRules.AddressBinding(subscriber.AddressKey)));
    }

    internal string Create(SubscriberExitProof proof) => origin + Path + "#t=" + SubscriberExitProofCodec.Protect(protector, proof);

    public bool TryRead(string? token, out SubscriberExitProof? proof) => SubscriberExitProofCodec.TryRead(protector, token, out proof);
}

/// <summary>Validates proof before database access; exit remains available regardless of provider or worker configuration.</summary>
public sealed class SubscriberUnsubscriptionService(SubscriberExitLinks links, ISubscriberUnsubscriptionStore store, TimeProvider clock)
{
    public async Task<UnsubscriptionOutcome> UnsubscribeAsync(string? token, CancellationToken cancellationToken)
    {
        if (!links.TryRead(token, out var proof) || proof is null) return UnsubscriptionOutcome.Invalid;
        return await store.UnsubscribeAsync(proof, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
