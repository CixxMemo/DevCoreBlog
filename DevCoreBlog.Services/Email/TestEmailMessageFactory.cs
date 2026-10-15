using System.Security.Cryptography;
using System.Text;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Services.Email;

/// <summary>Only server environment selects the single recipient; account login and HTTP fields cannot select it.</summary>
public sealed class TestEmailOptions
{
    public bool Enabled { get; }
    public SubscriberAddress? Recipient { get; }
    public string Origin { get; }
    public bool HasHttpsOrigin { get; }
    public TestEmailOptions(bool enabled, string? recipient, string origin)
    {
        Enabled = enabled;
        if (SubscriberAddress.TryCreate(recipient, out var mailbox)) Recipient = mailbox;
        Origin = new Subscriptions.SubscriberVerificationLinks(origin).Origin;
        HasHttpsOrigin = new Uri(Origin).Scheme == "https";
    }
    public override string ToString() => nameof(TestEmailOptions);
}

public enum TestEmailCompositionStatus { Valid, Invalid, TooLarge, OriginUnavailable }
public sealed record TestEmailMessage(string Subject, string Html, string Text, string DocumentHash)
{ public override string ToString() => nameof(TestEmailMessage); }
public sealed record TestEmailComposition(TestEmailCompositionStatus Status, TestEmailMessage? Message);

/// <summary>Validates and renders the exact unsaved snapshot using the existing C# email renderer, with an explicit test label.</summary>
public sealed class TestEmailMessageFactory(ContentDocumentValidator validator, DocumentEmailRenderer renderer, TestEmailOptions options)
{
    public const int MaximumSubjectLength = 200;
    public const string DefaultSubject = "Aether Notes — deneme e-postası";
    public TestEmailComposition Compose(string? documentJson, string? subject)
    {
        if (string.IsNullOrWhiteSpace(documentJson) || string.IsNullOrWhiteSpace(subject) || subject.Length > MaximumSubjectLength || subject.Any(char.IsControl))
            return new(TestEmailCompositionStatus.Invalid, null);
        try { _ = new UTF8Encoding(false, true).GetByteCount(subject); }
        catch (EncoderFallbackException) { return new(TestEmailCompositionStatus.Invalid, null); }
        var validation = validator.Validate(documentJson);
        if (validation.Document is not { } document)
            return new(validation.Error?.Kind == DocumentFailureKind.Limit ? TestEmailCompositionStatus.TooLarge : TestEmailCompositionStatus.Invalid, null);
        if (!options.HasHttpsOrigin) return new(TestEmailCompositionStatus.OriginUnavailable, null);
        var rendered = renderer.Render(document, EmailRenderContext.Create(options.Origin + "/"));
        const string footer = "Bu bir test e-postasıdır. Abonelere gönderilmedi.";
        return new(TestEmailCompositionStatus.Valid, new("[TEST] " + subject.Trim(), rendered.Html + "<p>" + footer + "</p>",
            rendered.PlainText + "\n\n" + footer, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)))));
    }
}
