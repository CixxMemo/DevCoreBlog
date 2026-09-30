using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Configuration;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Accepts bounded post submissions from the secret-authenticated webhook.</summary>
[ApiController]
[Route("api/webhooks")]
[EnableRateLimiting("WebhookLimiter")]
public sealed class WebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IPostService _postService;
    private readonly IWebhookPostService _webhookPostService;
    private readonly WebhookIngressOptions _options;
    private readonly TimeProvider _timeProvider;

    public WebhookController(
        IPostService postService,
        IWebhookPostService webhookPostService,
        WebhookIngressOptions options,
        TimeProvider timeProvider)
    {
        _postService = postService;
        _webhookPostService = webhookPostService;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Authenticates before reading JSON, then maps allowed fields into a post.</summary>
    [HttpPost("posts")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(WebhookIngressOptions.MaximumRequestBodyBytes)]
    public async Task<IActionResult> IngestPost(CancellationToken cancellationToken)
    {
        if (!HasValidSecret())
        {
            return Unauthorized(new { success = false, message = "Unauthorized." });
        }

        if (!TryReadIdempotencyKey(out var key))
        {
            return BadRequest(new { success = false, message = "Idempotency-Key must contain 1 to 128 ASCII letters, digits, dots, underscores or hyphens." });
        }

        if (Request.ContentLength > WebhookIngressOptions.MaximumRequestBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { success = false, message = "Payload is too large." });
        }

        if (!Request.HasJsonContentType())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { success = false, message = "JSON content is required." });
        }

        WebhookPostPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<WebhookPostPayload>(
                Request.Body, JsonOptions, cancellationToken);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { success = false, message = "Payload is too large." });
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "Invalid JSON payload." });
        }

        if (payload is null)
        {
            return BadRequest(new { success = false, message = "Empty JSON payload." });
        }

        var post = new Post
        {
            Title = payload.Title?.Trim() ?? string.Empty,
            Content = payload.Content ?? string.Empty,
            Summary = payload.Summary?.Trim() ?? string.Empty,
            Excerpt = payload.Excerpt?.Trim() ?? string.Empty,
            ThumbnailUrl = payload.CoverImageUrl?.Trim() ?? string.Empty,
            CategoryId = payload.CategoryId,
            IsPublished = payload.IsPublished && _options.AllowPublish,
            IsActive = true,
            PublishDate = payload.PublishDate ?? _timeProvider.GetUtcNow().UtcDateTime
        };

        WebhookPostResult result;
        if (key is null)
        {
            // Existing clients may migrate independently; only keyed requests have retry protection.
            var validation = await _postService.CreatePostAsync(post, cancellationToken);
            result = new(validation, validation.IsValid ? WebhookPostSnapshot.FromPost(post) : null);
        }
        else
        {
            // Hash the typed client fields, before generated dates or server publication policy.
            var payloadHash = Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
            result = await _webhookPostService.CreateAsync(post, key, payloadHash, cancellationToken);
        }

        if (result.KeyConflict)
        {
            return Conflict(new { success = false, message = "Idempotency-Key was already used with a different payload." });
        }

        var validationResult = result.Validation;
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(error =>
                new WebhookFieldError(
                    error.Field == nameof(Post.ThumbnailUrl)
                        ? nameof(WebhookPostPayload.CoverImageUrl)
                        : error.Field,
                    error.Message));

            return BadRequest(new
            {
                success = false,
                message = "Payload validation failed.",
                errors
            });
        }

        var saved = result.Post
            ?? throw new InvalidOperationException("Successful webhook submission requires a post result.");
        return Ok(new
        {
            success = true,
            postId = saved.Id,
            title = saved.Title,
            slug = saved.Slug,
            isPublished = saved.IsPublished,
            publishDate = saved.PublishDate,
            message = saved.IsPublished ? "Post created and published." : "Post saved as draft."
        });
    }

    // Missing headers preserve the old contract; present headers must be unambiguous and bounded.
    private bool TryReadIdempotencyKey(out string? key)
    {
        key = null;
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            return true;
        }

        if (header.Count != 1 || header[0] is not { Length: >= 1 and <= 128 } value ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '_' and not '-'))
        {
            return false;
        }

        key = value;
        return true;
    }

    private bool HasValidSecret()
    {
        if (string.IsNullOrWhiteSpace(_options.Secret) ||
            !Request.Headers.TryGetValue("X-DevCore-Secret", out var providedHeader) ||
            providedHeader.Count != 1)
        {
            return false;
        }

        var providedSecret = providedHeader[0];
        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        var expectedBytes = Encoding.UTF8.GetBytes(_options.Secret);
        return providedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private sealed record WebhookFieldError(string Field, string Message);
}

/// <summary>Allows only client-editable post fields from the existing JSON contract.</summary>
public sealed class WebhookPostPayload
{
    public string? Title { get; init; }

    public string? Content { get; init; }

    public string? Summary { get; init; }

    public string? Excerpt { get; init; }

    public string? CoverImageUrl { get; init; }

    public int CategoryId { get; init; }

    public bool IsPublished { get; init; }

    public DateTime? PublishDate { get; init; }
}
