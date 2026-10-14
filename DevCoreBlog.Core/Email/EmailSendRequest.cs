using System.Text;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Core.Email;

public enum EmailPurpose { Transactional, Newsletter }

/// <summary>Bounded immutable transport input. HTML must already come from the trusted renderer, never an HTTP form.</summary>
public sealed class EmailSendRequest
{
    public const int MaximumSubjectLength = 300;
    public const int MaximumHtmlBytes = 1_048_576;
    public const int MaximumTextBytes = 262_144;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string Recipient { get; }
    public string Subject { get; }
    public string Html { get; }
    public string Text { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset FirstAttemptUtc { get; }
    public EmailPurpose Purpose { get; }
    public Uri? Unsubscribe { get; }

    public EmailSendRequest(string recipient, string subject, string html, string text,
        string idempotencyKey, DateTimeOffset firstAttemptUtc,
        EmailPurpose purpose = EmailPurpose.Transactional, string? unsubscribeUrl = null)
    {
        if (!SubscriberAddress.TryCreate(recipient, out var mailbox) || mailbox is null)
            throw new ArgumentException("Invalid single email recipient.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaximumSubjectLength
            || subject.Any(char.IsControl))
            throw new ArgumentException("Invalid email subject.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256
            || idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '/' and not '.'))
            throw new ArgumentException("Invalid email idempotency key.");
        if (firstAttemptUtc.Offset != TimeSpan.Zero || firstAttemptUtc == DateTimeOffset.MinValue)
            throw new ArgumentException("Email attempt time must be UTC.");
        if (!Enum.IsDefined(purpose)) throw new ArgumentException("Invalid email purpose.");
        ValidateBody(html, MaximumHtmlBytes);
        ValidateBody(text, MaximumTextBytes);
        // Validate the subject's Unicode too; serialization must not silently replace invalid surrogates.
        ValidateBody(subject, MaximumSubjectLength * 4);
        if (unsubscribeUrl is not null)
        {
            if (unsubscribeUrl.Length > 2048 || unsubscribeUrl.Any(char.IsWhiteSpace)
                || unsubscribeUrl.Any(char.IsControl)
                || !Uri.TryCreate(unsubscribeUrl, UriKind.Absolute, out var exit)
                || exit.Scheme != Uri.UriSchemeHttps || exit.UserInfo.Length != 0 || exit.Fragment.Length != 0)
                throw new ArgumentException("Invalid email unsubscribe URL.");
            Unsubscribe = exit;
        }
        if (purpose == EmailPurpose.Newsletter && Unsubscribe is null)
            throw new ArgumentException("Newsletter requires an unsubscribe URL.");
        Recipient = mailbox.Address;
        Subject = subject;
        Html = html;
        Text = text;
        IdempotencyKey = idempotencyKey;
        FirstAttemptUtc = firstAttemptUtc;
        Purpose = purpose;
    }

    private static void ValidateBody(string value, int maximum)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || StrictUtf8.GetByteCount(value) > maximum)
                throw new ArgumentException("Invalid or oversized email content.");
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Invalid email Unicode content.");
        }
    }
}
