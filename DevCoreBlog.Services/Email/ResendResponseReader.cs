using System.Text.Json;
using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Services.Email;

/// <summary>Parses only the documented ID/error name; provider body and arbitrary strings never escape.</summary>
internal static class ResendResponseReader
{
    public static EmailSendResult Read(int status, byte[] bytes, TimeSpan? retryAfter)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(p => p.Name).Distinct().Count() != root.EnumerateObject().Count())
                return Protocol(status);
            if (status is 200 or 201)
            {
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && Guid.TryParseExact(id.GetString(), "D", out var messageId) && messageId != Guid.Empty)
                    return new(EmailSendOutcome.Accepted, EmailSendReason.None, messageId, status);
                return Protocol(status);
            }
            var name = root.TryGetProperty("name", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
            if (status == 429)
                return name switch
                {
                    "daily_quota_exceeded" => new(EmailSendOutcome.Retryable, EmailSendReason.DailyQuota, HttpStatus: status, RetryAfter: retryAfter),
                    "monthly_quota_exceeded" => new(EmailSendOutcome.Retryable, EmailSendReason.MonthlyQuota, HttpStatus: status, RetryAfter: retryAfter),
                    "rate_limit_exceeded" => new(EmailSendOutcome.Retryable, EmailSendReason.RateLimit, HttpStatus: status, RetryAfter: retryAfter),
                    _ => Protocol(status)
                };
            if (status == 409 && name == "concurrent_idempotent_requests")
                return new(EmailSendOutcome.Retryable, EmailSendReason.ConcurrentRequest, HttpStatus: status, RetryAfter: retryAfter);
            if (status == 409 && name == "invalid_idempotent_request")
                return new(EmailSendOutcome.Uncertain, EmailSendReason.IdempotencyConflict, HttpStatus: status);
            if ((status is 400 or 422) && (name is "validation_error" or "invalid_idempotency_key" or "invalid_parameter" or "missing_required_field" or "missing_required_parameter"))
                return new(EmailSendOutcome.Rejected, EmailSendReason.Validation, HttpStatus: status);
            return Protocol(status);
        }
        catch (JsonException) { return Protocol(status); }
    }

    private static EmailSendResult Protocol(int status) => new(EmailSendOutcome.Uncertain, EmailSendReason.Protocol, HttpStatus: status);
}
