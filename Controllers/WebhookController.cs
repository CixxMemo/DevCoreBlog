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
    private readonly WebhookIngressOptions _options;
    private readonly TimeProvider _timeProvider;

    public WebhookController(
        IPostService postService,
        WebhookIngressOptions options,
        TimeProvider timeProvider)
    {
        _postService = postService;
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

        var validationResult = await _postService.CreatePostAsync(
            post, cancellationToken);
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

        return Ok(new
        {
            success = true,
            postId = post.Id,
            title = post.Title,
            slug = post.Slug,
            isPublished = post.IsPublished,
            publishDate = post.PublishDate,
            message = post.IsPublished ? "Post created and published." : "Post saved as draft."
        });
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
