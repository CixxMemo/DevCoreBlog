using System.Net.Http.Headers;
using System.Text.Json;
using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Services.Email;

/// <summary>Single-call Resend REST adapter; never retries automatically or exports provider messages.</summary>
public sealed class ResendEmailSender(HttpClient client, ResendOptions options, TimeProvider clock) : IEmailSender
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    // Leave a margin inside the provider's documented 24-hour key retention window.
    public static readonly TimeSpan SafeIdempotencyWindow = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5);
    public const int MaximumRequestBytes = 2_097_152;
    public const int MaximumResponseBytes = 16_384;
    private static readonly Uri Endpoint = new("https://api.resend.com/emails");

    public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.IsConfigured) return new(EmailSendOutcome.NotConfigured, EmailSendReason.Configuration);
        var elapsed = clock.GetUtcNow() - request.FirstAttemptUtc;
        if (elapsed < TimeSpan.Zero) return new(EmailSendOutcome.Rejected, EmailSendReason.Validation);
        if (elapsed >= SafeIdempotencyWindow - RequestTimeout)
            return new(EmailSendOutcome.Uncertain, EmailSendReason.IdempotencyWindow);
        var payload = CreatePayload(request);
        if (payload.Length > MaximumRequestBytes)
            return new(EmailSendOutcome.Rejected, EmailSendReason.PayloadLimit);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey);
        message.Content = new ByteArrayContent(payload);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var deadline = new CancellationTokenSource(RequestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        // Check again immediately before I/O. After SendAsync starts, cancellation cannot prove non-acceptance.
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var status = (int)response.StatusCode;
            if (status >= 500) return new(EmailSendOutcome.Uncertain, EmailSendReason.ServerError, HttpStatus: status);
            if (status is 401 or 403) return new(EmailSendOutcome.Rejected, EmailSendReason.Authentication, HttpStatus: status);
            var bytes = await ReadBoundedAsync(response.Content, linked.Token);
            if (bytes is null) return new(EmailSendOutcome.Uncertain, EmailSendReason.Protocol, HttpStatus: status);
            return ResendResponseReader.Read(status, bytes, ReadRetryAfter(response));
        }
        catch (OperationCanceledException)
        {
            return new(EmailSendOutcome.Uncertain,
                cancellationToken.IsCancellationRequested ? EmailSendReason.Cancelled : EmailSendReason.Timeout);
        }
        catch (HttpRequestException)
        {
            return new(EmailSendOutcome.Uncertain, EmailSendReason.Transport);
        }
        catch (IOException)
        {
            return new(EmailSendOutcome.Uncertain, EmailSendReason.Transport);
        }
    }

    private byte[] CreatePayload(EmailSendRequest request)
    {
        // Typed, closed payload: no cc/bcc, arbitrary headers, remote attachments or provider templates.
        var headers = request.Unsubscribe is null ? null : new Dictionary<string, string>
        {
            ["List-Unsubscribe"] = "<" + request.Unsubscribe.AbsoluteUri + ">",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click"
        };
        return JsonSerializer.SerializeToUtf8Bytes(new ResendPayload(options.From,
            [request.Recipient], request.Subject, request.Html, request.Text, options.ReplyTo, headers),
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

    private async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > MaximumResponseBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        // Invalid/huge hints remain unknown; the worker must still apply its own bounded retry policy.
        if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
        var raw = values.Take(2).ToArray();
        if (raw.Length != 1 || raw[0].Length > 128 || !RetryConditionHeaderValue.TryParse(raw[0], out var hint)) return null;
        var delay = hint.Delta ?? hint.Date - clock.GetUtcNow();
        return delay >= TimeSpan.Zero && delay <= TimeSpan.FromDays(1) ? delay : null;
    }

    private sealed record ResendPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("from")] string? From,
        [property: System.Text.Json.Serialization.JsonPropertyName("to")] string[] To,
        [property: System.Text.Json.Serialization.JsonPropertyName("subject")] string Subject,
        [property: System.Text.Json.Serialization.JsonPropertyName("html")] string Html,
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("reply_to")] string? ReplyTo,
        [property: System.Text.Json.Serialization.JsonPropertyName("headers")] Dictionary<string, string>? Headers);
}
