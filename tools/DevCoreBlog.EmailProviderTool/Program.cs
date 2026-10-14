using System.Net;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Default mode is isolated contract acceptance. Live mode uses only the provider's fixed simulator addresses.
var checks = new SortedDictionary<string, bool>();
var diagnostics = new Dictionary<string, string>();
var clock = new FixedClock(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
var config = new ResendOptions("re_synthetic_key", "sender@example.test", "Aether Notes", "reply@example.test");
var messageId = Guid.NewGuid();
var success = "{\"id\":\"" + messageId.ToString("D") + "\"}";
void Check(string name, bool condition) => checks.Add(name, condition);
bool Rejects(Func<EmailSendRequest> create)
{
    try { create(); return false; }
    catch (ArgumentException) { return true; }
}
EmailSendRequest Mail(string subject = "Türkçe AI deneyi 🦥", string key = "synthetic/recipient-1",
    DateTimeOffset? attempt = null, EmailPurpose purpose = EmailPurpose.Transactional, string? exit = null,
    string recipient = "recipient@example.test", string html = "<p>Türkçe güvenli içerik</p>", string text = "Türkçe içerik")
    => new(recipient, subject, html, text, key, attempt ?? clock.GetUtcNow(), purpose, exit);
HttpResponseMessage Response(int status, string body, string? retry = null)
{
    var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    if (retry is not null) response.Headers.TryAddWithoutValidation("Retry-After", retry);
    return response;
}
async Task Scenario(string name, int status, string body, EmailSendOutcome outcome, EmailSendReason reason, string? retry = null)
{
    using var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(status, body, retry)));
    using var client = new HttpClient(handler);
    var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail());
    Check(name, result.Outcome == outcome && result.Reason == reason && handler.Calls == 1 && result.HttpStatus == status);
}

try
{
    if (args.Contains("--live"))
    {
        var key = Environment.GetEnvironmentVariable("RESEND_API_KEY");
        // This sender is permitted solely by the provider's simulator, never configured for the application.
        var liveOptions = new ResendOptions(key, "onboarding@resend.dev", "Aether Notes Test");
        Check("credential-present", liveOptions.IsConfigured);
        if (liveOptions.IsConfigured)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddResendEmail(liveOptions);
            using var scope = services.BuildServiceProvider();
            var sender = scope.GetRequiredService<IEmailSender>();
            var first = DateTimeOffset.UtcNow;
            var idempotency = "f24-test/" + Guid.NewGuid().ToString("N");
            EmailSendRequest Live(string recipient, string subject = "F24 Türkçe bağlantı testi 🦥", string? token = null)
                => new(recipient, subject, "<p>Yalnız sağlayıcı test mekanizması.</p>", "Yalnız sağlayıcı test mekanizması.", token ?? idempotency, first);
            var accepted = await sender.SendAsync(Live("delivered@resend.dev"));
            Check("provider-accepts-simulator", accepted.Outcome == EmailSendOutcome.Accepted && accepted.ProviderMessageId is not null);
            diagnostics["initial"] = accepted.Outcome + "/" + accepted.Reason + "/" + accepted.HttpStatus;
            Console.Error.WriteLine("live_initial_outcome=" + accepted.Outcome + ";reason=" + accepted.Reason + ";http=" + accepted.HttpStatus);
            if (accepted.Outcome == EmailSendOutcome.Accepted)
            {
                await Task.Delay(1100);
                var replay = await sender.SendAsync(Live("delivered@resend.dev"));
                Check("provider-identical-replay-same-id", replay.Outcome == EmailSendOutcome.Accepted && replay.ProviderMessageId == accepted.ProviderMessageId);
                await Task.Delay(1100);
                var conflict = await sender.SendAsync(Live("delivered@resend.dev", "Farklı test konusu"));
                diagnostics["conflict"] = conflict.Outcome + "/" + conflict.Reason + "/" + conflict.HttpStatus;
                Check("provider-different-payload-conflict", conflict.Reason == EmailSendReason.IdempotencyConflict && conflict.HttpStatus == 409);
                foreach (var recipient in new[] { "bounced@resend.dev", "complained@resend.dev", "suppressed@resend.dev" })
                {
                    await Task.Delay(1100);
                    var result = await sender.SendAsync(Live(recipient, token: "f24-test/" + Guid.NewGuid().ToString("N")));
                    // Acceptance of a simulated bounce/complaint is not a delivery event assertion.
                    Check("provider-accepts-" + recipient.Split('@')[0] + "-simulation", result.Outcome == EmailSendOutcome.Accepted);
                }
            }
        }
    }
    else
    {
        Check("input-rejects-multiple-recipient", Rejects(() => Mail(recipient: "a@example.test,b@example.test")));
        Check("input-rejects-display-name", Rejects(() => Mail(recipient: "Name <a@example.test>")));
        Check("input-rejects-recipient-crlf", Rejects(() => Mail(recipient: "a@example.test\r\nBcc:b@example.test")));
        Check("input-rejects-subject-crlf", Rejects(() => Mail(subject: "safe\r\nBcc:evil")));
        Check("subject-at-boundary", Mail(subject: new string('x', 300)).Subject.Length == 300);
        Check("subject-over-boundary", Rejects(() => Mail(subject: new string('x', 301))));
        Check("html-over-byte-limit", Rejects(() => Mail(html: new string('ü', 524289))));
        Check("text-over-byte-limit", Rejects(() => Mail(text: new string('ü', 131073))));
        Check("invalid-unicode", Rejects(() => Mail(html: "\ud800")));
        Check("empty-text-rejected", Rejects(() => Mail(text: "")));
        Check("invalid-purpose", Rejects(() => Mail(purpose: (EmailPurpose)99)));
        Check("key-over-boundary", Rejects(() => Mail(key: new string('x', 257))));
        Check("key-at-boundary", Mail(key: new string('x', 256)).IdempotencyKey.Length == 256);
        Check("key-crlf-rejected", Rejects(() => Mail(key: "key\r\n")));
        Check("empty-reply-to-is-optional", new ResendOptions("re_test", "a@example.test", "Safe", "").IsConfigured);
        Check("invalid-sender-unicode-disables", !new ResendOptions("re_test", "a@example.test", "\ud800").IsConfigured);
        Check("UTC-required", Rejects(() => Mail(attempt: clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)))));
        Check("newsletter-needs-exit", Rejects(() => Mail(purpose: EmailPurpose.Newsletter)));
        foreach (var exit in new[] { "http://example.test/exit", "https://u:p@example.test/exit", "https://example.test/exit#fragment", "https://example.test/exit\r\n" })
            Check("exit-rejected-" + checks.Count, Rejects(() => Mail(purpose: EmailPurpose.Newsletter, exit: exit)));
        foreach (var options in new[] { new ResendOptions(null, null, null), new ResendOptions("re_bad\r\n", "a@example.test", "Safe"),
            new ResendOptions("re_test", "a@example.test", "Name\r\n"), new ResendOptions("re_test", "a@example.test", "Bad <name>"),
            new ResendOptions("re_test", "a@example.test", "Safe", "bad,address") })
        {
            using var handler = new ScriptedHandler((_, _) => throw new InvalidOperationException());
            using var client = new HttpClient(handler);
            var result = await new ResendEmailSender(client, options, clock).SendAsync(Mail());
            Check("config-fail-closed-" + checks.Count, !options.IsConfigured && result.Outcome == EmailSendOutcome.NotConfigured && handler.Calls == 0);
        }
        using (var handler = new ScriptedHandler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            var root = body.RootElement;
            Check("fixed-https-endpoint", request.Method == HttpMethod.Post && request.RequestUri?.AbsoluteUri == "https://api.resend.com/emails");
            Check("single-recipient-only", root.GetProperty("to").GetArrayLength() == 1 && !root.TryGetProperty("cc", out _) && !root.TryGetProperty("bcc", out _));
            Check("unicode-and-both-bodies", root.GetProperty("subject").GetString() == Mail().Subject && root.GetProperty("html").GetString() == Mail().Html && root.GetProperty("text").GetString() == Mail().Text);
            Check("sender-and-reply-to", root.GetProperty("from").GetString() == "Aether Notes <sender@example.test>" && root.GetProperty("reply_to").GetString() == "reply@example.test");
            Check("key-and-authorization", request.Headers.GetValues("Idempotency-Key").Single() == "synthetic/recipient-1" && request.Headers.Authorization?.Parameter == "re_synthetic_key");
            Check("closed-payload", !root.TryGetProperty("attachments", out _) && !root.TryGetProperty("scheduled_at", out _) && !root.TryGetProperty("tags", out _) && !root.TryGetProperty("headers", out _));
            return Response(200, success);
        }))
        using (var client = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.invalid/") })
        {
            var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail());
            Check("accepted-not-delivered", result.Outcome == EmailSendOutcome.Accepted && result.ProviderMessageId == messageId && handler.Calls == 1);
        }
        using (var handler = new ScriptedHandler(async (request, token) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var headers = json.RootElement.GetProperty("headers");
            Check("one-click-headers", headers.GetProperty("List-Unsubscribe").GetString() == "<https://example.test/exit?t=opaque>"
                && headers.GetProperty("List-Unsubscribe-Post").GetString() == "List-Unsubscribe=One-Click");
            return Response(200, success);
        }))
        using (var client = new HttpClient(handler))
            await new ResendEmailSender(client, config, clock).SendAsync(Mail(purpose: EmailPurpose.Newsletter, exit: "https://example.test/exit?t=opaque"));
        await Scenario("definite-auth-rejection", 401, "private vendor text", EmailSendOutcome.Rejected, EmailSendReason.Authentication);
        await Scenario("definite-domain-rejection", 403, "private vendor text", EmailSendOutcome.Rejected, EmailSendReason.Authentication);
        await Scenario("definite-validation-rejection", 422, "{\"name\":\"validation_error\",\"message\":\"private@example.test\"}", EmailSendOutcome.Rejected, EmailSendReason.Validation);
        await Scenario("invalid-key-rejection", 400, "{\"name\":\"invalid_idempotency_key\"}", EmailSendOutcome.Rejected, EmailSendReason.Validation);
        await Scenario("rate-limit", 429, "{\"name\":\"rate_limit_exceeded\"}", EmailSendOutcome.Retryable, EmailSendReason.RateLimit, "7");
        await Scenario("daily-quota", 429, "{\"name\":\"daily_quota_exceeded\"}", EmailSendOutcome.Retryable, EmailSendReason.DailyQuota);
        await Scenario("monthly-quota", 429, "{\"name\":\"monthly_quota_exceeded\"}", EmailSendOutcome.Retryable, EmailSendReason.MonthlyQuota);
        await Scenario("concurrent-key", 409, "{\"name\":\"concurrent_idempotent_requests\"}", EmailSendOutcome.Retryable, EmailSendReason.ConcurrentRequest);
        await Scenario("conflict-never-new-key", 409, "{\"name\":\"invalid_idempotent_request\"}", EmailSendOutcome.Uncertain, EmailSendReason.IdempotencyConflict);
        foreach (var status in new[] { 500, 502, 503 }) await Scenario("uncertain-server-" + status, status, "not logged", EmailSendOutcome.Uncertain, EmailSendReason.ServerError);
        foreach (var body in new[] { "{}", "not json", "[]", "{\"id\":7}", "{\"id\":\"private@example.test\"}", "{\"id\":\"" + messageId + "\",\"id\":\"" + messageId + "\"}" })
            await Scenario("unknown-success-" + checks.Count, 200, body, EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        await Scenario("redirect-is-not-success", 302, "{}", EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        await Scenario("unknown-rejection-conservative", 409, "{\"name\":\"future_error\"}", EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        await Scenario("oversized-response", 200, new string('x', ResendEmailSender.MaximumResponseBytes + 1), EmailSendOutcome.Uncertain, EmailSendReason.Protocol);
        using (var stream = new CountingStream(new byte[100_000]))
        using (var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })))
        using (var client = new HttpClient(handler))
        {
            var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail());
            Check("chunked-response-bounded-without-length", result.Reason == EmailSendReason.Protocol
                && stream.BytesRead <= ResendEmailSender.MaximumResponseBytes + 4096 && stream.BytesRead < 100_000);
        }
        using (var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) })))
        using (var client = new HttpClient(handler))
        {
            var result = await new ResendEmailSender(client, config, new FastDeadlineClock(clock.GetUtcNow())).SendAsync(Mail());
            Check("deadline-covers-stream-after-headers", result.Reason == EmailSendReason.Timeout && handler.Calls == 1);
        }
        foreach (var hint in new[] { "7", "-1", "99999999999", "tomorrow", clock.GetUtcNow().AddSeconds(10).ToString("r") })
        {
            using var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(429, "{\"name\":\"rate_limit_exceeded\"}", hint)));
            using var client = new HttpClient(handler);
            var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail());
            var expected = hint == "7" ? TimeSpan.FromSeconds(7) : hint.EndsWith("GMT") ? TimeSpan.FromSeconds(10) : (TimeSpan?)null;
            Check("bounded-retry-hint-" + checks.Count, result.RetryAfter == expected);
        }
        using (var handler = new ScriptedHandler((_, _) => throw new HttpRequestException("PRIVATE BODY")))
        using (var client = new HttpClient(handler))
        {
            var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail());
            Check("transport-no-secret-no-retry", result.Reason == EmailSendReason.Transport && handler.Calls == 1 && !result.ToString().Contains("PRIVATE"));
        }
        using (var handler = new ScriptedHandler((_, _) => Task.FromResult(Response(200, success))))
        using (var client = new HttpClient(handler))
        {
            var sender = new ResendEmailSender(client, config, clock);
            var expired = await sender.SendAsync(Mail(attempt: clock.GetUtcNow() - ResendEmailSender.SafeIdempotencyWindow));
            var future = await sender.SendAsync(Mail(attempt: clock.GetUtcNow().AddSeconds(1)));
            Check("expired-key-no-IO", expired.Reason == EmailSendReason.IdempotencyWindow && handler.Calls == 0);
            Check("future-attempt-no-IO", future.Reason == EmailSendReason.Validation && handler.Calls == 0);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await sender.SendAsync(Mail(), cancelled.Token); Check("pre-cancel-no-IO", false); }
            catch (OperationCanceledException) { Check("pre-cancel-no-IO", handler.Calls == 0); }
            var huge = await sender.SendAsync(Mail(html: new string('<', EmailSendRequest.MaximumHtmlBytes)));
            Check("serialized-limit-before-IO", huge.Reason == EmailSendReason.PayloadLimit && handler.Calls == 0);
        }
        using (var cancelled = new CancellationTokenSource())
        using (var handler = new ScriptedHandler(async (_, token) => { cancelled.Cancel(); await Task.Delay(10000, token); return Response(200, success); }))
        using (var client = new HttpClient(handler))
        {
            var result = await new ResendEmailSender(client, config, clock).SendAsync(Mail(), cancelled.Token);
            Check("post-submit-cancel-uncertain", result.Outcome == EmailSendOutcome.Uncertain && result.Reason == EmailSendReason.Cancelled && handler.Calls == 1);
        }
        using (var handler = new ScriptedHandler(async (_, token) => { await Task.Delay(10000, token); return Response(200, success); }))
        using (var client = new HttpClient(handler))
        {
            var result = await new ResendEmailSender(client, config, new FastDeadlineClock(clock.GetUtcNow())).SendAsync(Mail());
            Check("deadline-uncertain-no-retry", result.Reason == EmailSendReason.Timeout && handler.Calls == 1);
        }
        var logs = new LogCollector();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton<TimeProvider>(clock);
        services.AddResendEmail(config);
        services.AddHttpClient<IEmailSender, ResendEmailSender>().AddHttpMessageHandler(() => new SecurityInspectionHandler(handler =>
        {
            Check("transport-no-redirects-or-cookies", !handler.AllowAutoRedirect && !handler.UseCookies);
            Check("transport-header-connect-bounds", handler.ConnectTimeout == TimeSpan.FromSeconds(5) && handler.MaxResponseHeadersLength == 16);
            return Response(200, success);
        }));
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IEmailSender>();
        Check("DI-resolves-provider", (await resolved.SendAsync(Mail())).Outcome == EmailSendOutcome.Accepted);
        Check("HTTP-logs-disabled-even-trace", logs.Messages.Count == 0);
        Check("options-string-no-credential", !config.ToString()!.Contains("re_synthetic_key"));
        Check("input-string-no-content", !Mail().ToString()!.Contains(Mail().Recipient));
    }
}
catch (Exception error)
{
    checks["probe-completed"] = false;
    Console.Error.WriteLine("Probe failure type: " + error.GetType().Name); // Never export exception text or request/response bodies.
}
var passed = checks.Count > 0 && checks.Values.All(v => v);
Console.WriteLine(JsonSerializer.Serialize(new { passed, count = checks.Count, checks, diagnostics,
    mode = args.Contains("--live") ? "real Resend simulator API; no real mailbox delivery assertion" : "synthetic HTTP contract; no network or credentials" }));
return passed ? 0 : 1;

sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return action(request, cancellationToken); }
}
class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
sealed class FastDeadlineClock(DateTimeOffset now) : FixedClock(now)
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new ScaledTimer(callback, state);
    private sealed class ScaledTimer(TimerCallback callback, object? state) : ITimer
    {
        private readonly Timer timer = new(callback, state, TimeSpan.FromMilliseconds(25), Timeout.InfiniteTimeSpan);
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);
        public void Dispose() => timer.Dispose();
        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}
sealed class LogCollector : ILoggerProvider
{
    public List<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new Sink(Messages);
    public void Dispose() { }
    private sealed class Sink(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Add(formatter(state, exception));
    }
}
sealed class SecurityInspectionHandler(Func<SocketsHttpHandler, HttpResponseMessage> inspect) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var inner = InnerHandler;
        while (inner is DelegatingHandler handler) inner = handler.InnerHandler;
        return Task.FromResult(inspect(inner as SocketsHttpHandler ?? throw new InvalidOperationException("Unexpected handler type.")));
    }
}
class CountingStream(byte[] data) : Stream
{
    public int BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var size = Math.Min(count, data.Length - BytesRead);
        data.AsSpan(BytesRead, size).CopyTo(buffer.AsSpan(offset, size)); BytesRead += size; return size;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = Math.Min(buffer.Length, data.Length - BytesRead);
        data.AsMemory(BytesRead, size).CopyTo(buffer); BytesRead += size; return ValueTask.FromResult(size);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
sealed class StallingStream() : CountingStream([])
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
}
