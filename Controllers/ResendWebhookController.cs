using System.Text;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Cookie-independent provider ingress; this endpoint alone uses authenticated webhook signatures instead of CSRF.</summary>
[AllowAnonymous]
public sealed class ResendWebhookController(ResendWebhookService service, ResendWebhookOptions options) : Controller
{
    public const string Path = "/api/webhooks/resend";
    [HttpPost(Path)]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("ResendWebhookLimiter")]
    [Consumes("application/json")]
    [RequestSizeLimit(ResendWebhookOptions.MaximumBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (!options.IsConfigured) return StatusCode(503);
        if (Request.ContentLength > ResendWebhookOptions.MaximumBodyBytes) return StatusCode(413);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            foreach (var header in new[] { "svix-id", "svix-timestamp", "svix-signature" })
                if (Request.Headers[header].Count != 1) return StatusCode(401);
            var buffer = new byte[ResendWebhookOptions.MaximumBodyBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await Request.Body.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            if (count > ResendWebhookOptions.MaximumBodyBytes) return StatusCode(413);
            var raw = new UTF8Encoding(false, true).GetString(buffer, 0, count);
            var outcome = await service.ReceiveAsync(raw, Request.Headers["svix-id"].ToString(),
                Request.Headers["svix-timestamp"].ToString(), Request.Headers["svix-signature"].ToString(), deadline.Token);
            return StatusCode(outcome switch
            {
                WebhookResponse.Accepted => 204, WebhookResponse.InvalidSignature => 401,
                WebhookResponse.InvalidPayload => 400, WebhookResponse.Conflict => 409, _ => 503
            });
        }
        catch (DecoderFallbackException) { return StatusCode(400); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return StatusCode(413); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return StatusCode(503); }
    }
}
