namespace DevCoreBlog.Core.Email;

public enum TestEmailAdmission { Reserved, Existing, Conflict, Suppressed, Limited, InvalidTime }

/// <summary>Minimal immutable test intent and one irreversible result; no body, credential or automatic retry is stored.</summary>
public sealed class TestEmailSubmission
{
    private TestEmailSubmission() { }
    public Guid Id { get; private set; }
    public string InputHash { get; private set; } = string.Empty;
    public string AddressKey { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public EmailSendOutcome? Outcome { get; private set; }
    public EmailSendReason? Reason { get; private set; }
    public int? HttpStatus { get; private set; }
    public Guid? ProviderMessageId { get; private set; }
    public long Version { get; private set; }
    public override string ToString() => nameof(TestEmailSubmission);
}

public sealed record TestEmailIntent(Guid Id, string InputHash, string AddressKey, DateTime CreatedUtc)
{ public override string ToString() => nameof(TestEmailIntent); }
public sealed record TestEmailReservation(TestEmailAdmission Admission, TestEmailSubmission? Submission, TimeSpan? RetryAfter = null);

/// <summary>Serializes a single fixed-recipient test start, durable rolling budget and immutable completion.</summary>
public interface ITestEmailSubmissionStore
{
    Task<TestEmailReservation> ReserveAsync(TestEmailIntent intent, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid id, string inputHash, EmailSendResult result, DateTime completedUtc, CancellationToken cancellationToken);
}
