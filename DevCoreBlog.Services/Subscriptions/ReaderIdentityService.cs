using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.WebUtilities;

namespace DevCoreBlog.Services.Subscriptions;

/// <summary>Login ingress has its own explicit switch; session validation/logout never depend on mail readiness.</summary>
public sealed class ReaderLoginAvailability(bool enabled, bool workerEnabled, IEmailSubmissionContext submission)
{
    public bool CanRequest => enabled && workerEnabled && submission.IsConfigured;
}

/// <summary>Purpose-separated random proof lives only in the encrypted outbox; links never contain an address or return URL.</summary>
public sealed class ReaderLoginMessageFactory(TransactionalEmailProtector protection, SubscriberVerificationLinks links)
{
    public const string Path = "/abonelik/giris";
    public PreparedReaderLogin Prepare(SubscriberSnapshot subscriber, DateTime now)
    {
        now = ReaderIdentityRules.Utc(now);
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = WebEncoders.Base64UrlEncode(bytes);
        var id = Guid.NewGuid();
        var expires = now + ReaderIdentityRules.LinkLifetime;
        var draft = new ReaderLoginDraft(id, subscriber.Id, subscriber.Version, subscriber.PeriodId,
            SubscriberVerificationRules.Digest(bytes, ReaderIdentityRules.TokenPurpose),
            SubscriberVerificationRules.AddressBinding(subscriber.AddressKey), now, expires);
        var url = links.Origin + Path + "#t=" + token;
        var html = "<p>Aether Notes okuyucu girişi için bağlantıyı açıp girişinizi onaylayın.</p><p><a href=\"" +
            HtmlEncoder.Default.Encode(url) + "\">Okuyucu girişi</a></p><p>Bağlantı 15 dakika geçerlidir ve yalnız bir kez kullanılabilir. " +
            "Talep size ait değilse bu e-postayı yok sayın. Giriş bağlantısını kimseyle paylaşmayın.</p>";
        var text = "Aether Notes okuyucu girişi:\n" + url + "\nBağlantı 15 dakika geçerlidir ve yalnız bir kez kullanılabilir. " +
            "Talep size ait değilse bu e-postayı yok sayın. Giriş bağlantısını kimseyle paylaşmayın.";
        return new(draft, protection.Prepare(id, subscriber, "Aether Notes — Okuyucu girişi", html, text, now, expires,
            TransactionalEmailPurpose.ReaderLogin));
    }
}

public enum ReaderLoginRequestOutcome { Accepted, InvalidAddress, Unavailable }

/// <summary>Generic request outcomes avoid subscriber enumeration; durable store owns admission, single use and revocation.</summary>
public sealed class ReaderIdentityService(IReaderIdentityStore store, ReaderLoginMessageFactory messages,
    ReaderLoginAvailability availability, TimeProvider clock)
{
    public async Task<ReaderLoginRequestOutcome> RequestAsync(string? input, CancellationToken ct)
    {
        if (!SubscriberAddress.TryCreate(input, out var address) || address is null) return ReaderLoginRequestOutcome.InvalidAddress;
        if (!availability.CanRequest) return ReaderLoginRequestOutcome.Unavailable;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var now = ReaderIdentityRules.Utc(clock.GetUtcNow().UtcDateTime);
            await store.RequestAsync(address, now, s => messages.Prepare(s, now), ct);
            return ReaderLoginRequestOutcome.Accepted;
        }
        finally
        {
            var delay = TimeSpan.FromMilliseconds(250) - Stopwatch.GetElapsedTime(started);
            if (delay > TimeSpan.Zero && !ct.IsCancellationRequested) await Task.Delay(delay, ct);
        }
    }
    public Task<ReaderSessionGrant?> ConsumeAsync(string? token, CancellationToken ct)
    {
        if (!TryDigest(token, out var digest) || digest is null) return Task.FromResult<ReaderSessionGrant?>(null);
        return store.ConsumeAsync(digest, clock.GetUtcNow().UtcDateTime, ct);
    }
    public Task<bool> IsValidAsync(Guid id, CancellationToken ct) => store.IsValidAsync(id, clock.GetUtcNow().UtcDateTime, ct);
    public Task RevokeAsync(Guid id, CancellationToken ct) => store.RevokeAsync(id, clock.GetUtcNow().UtcDateTime, ct);
    public static bool TryDigest(string? token, out string? digest)
    {
        digest = null;
        if (token is not { Length: 43 } || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            if (bytes.Length != 32 || WebEncoders.Base64UrlEncode(bytes) != token) return false;
            digest = SubscriberVerificationRules.Digest(bytes, ReaderIdentityRules.TokenPurpose);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
