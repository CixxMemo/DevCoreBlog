namespace DevCoreBlog.Core.Email;

/// <summary>Submits one immutable message once; persistence, suppression and retries belong to the caller.</summary>
public interface IEmailSender
{
    /// <summary>Pre-cancelled calls throw without I/O; cancellation after submission returns an uncertain result.</summary>
    Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken = default);
}

public enum EmailSendOutcome { Accepted, Rejected, Retryable, Uncertain, NotConfigured }
public enum EmailSendReason
{
    None, Configuration, PayloadLimit, Authentication, Validation, RateLimit, DailyQuota,
    MonthlyQuota, ConcurrentRequest, IdempotencyConflict, IdempotencyWindow,
    ServerError, Protocol, Transport, Timeout, Cancelled
}

/// <summary>Contains only safe classifications; acceptance never asserts delivery or reading.</summary>
public sealed record EmailSendResult(EmailSendOutcome Outcome, EmailSendReason Reason,
    Guid? ProviderMessageId = null, int? HttpStatus = null, TimeSpan? RetryAfter = null);
