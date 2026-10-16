using System.Security.Cryptography;
using System.Text.Json;
using DevCoreBlog.Configuration;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Security;
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
        if (!WebhookRequestAuthentication.HasValidSecret(Request, _options))
        {
            return Unauthorized(new { success = false, message = "Bu işlem için yetki doğrulanamadı." });
        }

        if (!WebhookRequestAuthentication.TryReadKey(Request, out var key))
        {
            return BadRequest(new { success = false, message = "Idempotency-Key 1–128 ASCII harfi, rakam, nokta, alt çizgi veya kısa çizgi içermelidir." });
        }

        if (Request.ContentLength > WebhookIngressOptions.MaximumRequestBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { success = false, message = "İstek içeriği boyut sınırını aşıyor." });
        }

        if (!Request.HasJsonContentType())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { success = false, message = "JSON biçiminde içerik gereklidir." });
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
                new { success = false, message = "İstek içeriği boyut sınırını aşıyor." });
        }
        catch (JsonException)
        {
            return BadRequest(new { success = false, message = "JSON içeriği geçersiz." });
        }

        if (payload is null)
        {
            return BadRequest(new { success = false, message = "JSON içeriği boş." });
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
            return Conflict(new { success = false, message = "Idempotency-Key daha önce farklı bir içerikle kullanılmış." });
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
                message = "İstek içeriği doğrulanamadı.",
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
            message = saved.IsPublished ? "Yazı oluşturuldu ve yayımlandı." : "Yazı taslak olarak kaydedildi."
        });
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
