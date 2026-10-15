using System.Security.Cryptography;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;

namespace DevCoreBlog.Services.Email;

public enum TestEmailResultStatus { Accepted, Rejected, Uncertain, InProgress, Disabled, Invalid, TooLarge, Suppressed, Limited, Conflict, Unavailable }
public sealed record TestEmailResult(TestEmailResultStatus Status, bool Replayed = false, TimeSpan? RetryAfter = null);

/// <summary>One authenticated test action creates at most one durable start; uncertainty and refresh never trigger another call.</summary>
public sealed class TestEmailSubmissionService(TestEmailMessageFactory factory, TestEmailOptions options, IEmailSubmissionContext context,
    ITestEmailSubmissionStore store, IEmailSender sender, TimeProvider clock)
{
    public bool IsReady => options.Enabled && options.Recipient is not null && options.HasHttpsOrigin && context.IsConfigured;

    public async Task<TestEmailResult> SubmitAsync(Guid id, string? documentJson, string? subject, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var composed = factory.Compose(documentJson, subject);
        if (composed.Status != TestEmailCompositionStatus.Valid || composed.Message is not { } message)
            return new(composed.Status == TestEmailCompositionStatus.TooLarge ? TestEmailResultStatus.TooLarge :
                composed.Status == TestEmailCompositionStatus.OriginUnavailable ? TestEmailResultStatus.Disabled : TestEmailResultStatus.Invalid);
        if (id == Guid.Empty) return new(TestEmailResultStatus.Invalid);
        if (!IsReady || options.Recipient is not { } recipient || context.Binding is not { Length: 64 } binding) return new(TestEmailResultStatus.Disabled);
        var now = ReaderIdentityRules.Utc(clock.GetUtcNow().UtcDateTime);
        EmailSendRequest request;
        try { request = new(recipient.Address, message.Subject, message.Html, message.Text, "test/" + id.ToString("N"), new DateTimeOffset(now)); }
        catch (ArgumentException) { return new(TestEmailResultStatus.TooLarge); }
        // Binding is only an input to this intent digest, never persisted or logged as a credential fingerprint on its own.
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
            { "test-email/v1", message.DocumentHash, message.Subject, recipient.Key, options.Origin, binding })));
        TestEmailReservation reserved;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            reserved = await store.ReserveAsync(new(id, hash, recipient.Key, now), deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(TestEmailResultStatus.Unavailable); }
        if (reserved.Admission == TestEmailAdmission.Existing && reserved.Submission is { } existing)
            return new(Classify(existing, now), true);
        if (reserved.Admission != TestEmailAdmission.Reserved)
            return new(reserved.Admission switch
            {
                TestEmailAdmission.Conflict => TestEmailResultStatus.Conflict,
                TestEmailAdmission.Suppressed => TestEmailResultStatus.Suppressed,
                TestEmailAdmission.Limited => TestEmailResultStatus.Limited,
                _ => TestEmailResultStatus.Unavailable
            }, RetryAfter: reserved.RetryAfter);
        EmailSendResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await sender.SendAsync(request, cancellationToken);
            if (result.Outcome == EmailSendOutcome.Accepted && (result.ProviderMessageId is null || result.ProviderMessageId == Guid.Empty))
                result = new(EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        }
        catch (OperationCanceledException) { result = new(EmailSendOutcome.Uncertain, EmailSendReason.Cancelled); }
        catch (Exception) { result = new(EmailSendOutcome.Uncertain, EmailSendReason.Transport); }
        try
        {
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (!await store.CompleteAsync(id, hash, result, clock.GetUtcNow().UtcDateTime, finish.Token)) return new(TestEmailResultStatus.Uncertain);
        }
        catch (Exception) { return new(TestEmailResultStatus.Uncertain); }
        return new(result.Outcome == EmailSendOutcome.Accepted ? TestEmailResultStatus.Accepted :
            result.Outcome is EmailSendOutcome.Uncertain ? TestEmailResultStatus.Uncertain : TestEmailResultStatus.Rejected);
    }

    private static TestEmailResultStatus Classify(TestEmailSubmission stored, DateTime now) => stored.Outcome switch
    {
        EmailSendOutcome.Accepted => TestEmailResultStatus.Accepted,
        EmailSendOutcome.Uncertain => TestEmailResultStatus.Uncertain,
        null => now - stored.CreatedUtc >= TimeSpan.FromMinutes(1) ? TestEmailResultStatus.Uncertain : TestEmailResultStatus.InProgress,
        _ => TestEmailResultStatus.Rejected
    };
}
