using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.WebUtilities;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Accepts a trusted configured origin only; never derives ownership links from request Host.</summary>
public sealed class SubscriberVerificationLinks
{
    public const string Path = "/abonelik/dogrula";
    private readonly string origin;
    public SubscriberVerificationLinks(string trustedOrigin)
    {
        if (!Uri.TryCreate(trustedOrigin, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("Trusted verification origin required.");
        origin = uri.GetLeftPart(UriPartial.Authority);
    }
    public string Create(string token) => origin + Path + "#t=" + token;
}

/// <summary>Creates one purpose-bound proof and encrypted mail without network or database access.</summary>
public sealed class SubscriberVerificationMessageFactory(TransactionalEmailProtector protection, SubscriberVerificationLinks links)
{
    public PreparedSubscriberVerification Prepare(SubscriberSnapshot subscriber, DateTime now)
    {
        now = now.AddTicks(-(now.Ticks % 10));
        var bytes = RandomNumberGenerator.GetBytes(SubscriberVerificationRules.TokenBytes);
        var token = WebEncoders.Base64UrlEncode(bytes);
        var id = Guid.NewGuid();
        var draft = new SubscriberVerificationDraft(id, subscriber.Id, subscriber.Version,
            SubscriberVerificationRules.Digest(bytes), SubscriberVerificationRules.AddressBinding(subscriber.AddressKey), now);
        var url = links.Create(token);
        var html = "<p>Aether Notes bülten aboneliği için e-posta adresinizi doğrulayın.</p><p><a href=\"" +
            HtmlEncoder.Default.Encode(url) + "\">E-posta adresimi doğrula</a></p><p>Bu bağlantı 24 saat geçerlidir. Talep size ait değilse bu e-postayı yok sayabilirsiniz.</p>";
        var text = "Aether Notes bülten aboneliği için e-posta adresinizi doğrulayın:\n" + url +
            "\nBu bağlantı 24 saat geçerlidir. Talep size ait değilse bu e-postayı yok sayabilirsiniz.";
        var email = protection.Prepare(id, subscriber, "Aether Notes — E-posta adresinizi doğrulayın", html, text, now, draft.ExpiresUtc);
        return new(draft, email);
    }
}

/// <summary>Trusted internal issuance; public registration additionally enforces durable admission limits.</summary>
public sealed class SubscriberVerificationIssuer(ISubscriberRepository subscribers, ISubscriberVerificationStore tokens,
    SubscriberVerificationMessageFactory messages, IEmailSubmissionContext submission, TimeProvider clock)
{
    public async Task<VerificationStageOutcome> IssueAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        if (!submission.IsConfigured) return VerificationStageOutcome.Ineligible;
        var subscriber = await subscribers.FindAsync(subscriberId, cancellationToken);
        if (subscriber is null || subscriber.Status != SubscriptionStatus.PendingVerification || subscriber.Suppression != SendingSuppression.None)
            return VerificationStageOutcome.Ineligible;
        var prepared = messages.Prepare(subscriber, clock.GetUtcNow().UtcDateTime);
        return await tokens.StageAsync(prepared.Token, prepared.Email, cancellationToken);
    }
}

/// <summary>Canonical base64url proof is hashed before persistence access; invalid and used links share one result.</summary>
public sealed class SubscriberVerificationConsumer(ISubscriberVerificationStore tokens, TimeProvider clock)
{
    public async Task<VerificationConsumeOutcome> ConfirmAsync(string? token, CancellationToken cancellationToken)
    {
        if (!TryDigest(token, out var digest) || digest is null) return VerificationConsumeOutcome.Invalid;
        return await tokens.ConsumeAsync(digest, SubscriberVerificationRules.Purpose, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
    public static bool TryDigest(string? token, out string? digest)
    {
        digest = null;
        if (token is not { Length: SubscriberVerificationRules.TokenLength } || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            if (bytes.Length != SubscriberVerificationRules.TokenBytes || WebEncoders.Base64UrlEncode(bytes) != token) return false;
            digest = SubscriberVerificationRules.Digest(bytes); return true;
        }
        catch (FormatException) { return false; }
    }
}
