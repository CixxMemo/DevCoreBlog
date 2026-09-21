// =============================================================================
// WebhookController.cs — Hardened Inbound Webhook Receiver
// =============================================================================
// This controller exposes an authenticated, rate-limited webhook endpoint
// for workflow automation platforms like n8n, Make.com, or custom AI agents.
//
// Security Standards:
//   1. Rate Limiting: Protected by ASP.NET Core WebhookLimiter (Max 5 req/min).
//   2. Timing-Attack Resistance: Uses CryptographicOperations.FixedTimeEquals.
//   3. Environment-Based Auth: Compares against WEBHOOK_API_SECRET env variable.
//   4. Fail-Safe Default: New articles default to Draft (IsPublished = false).
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

[ApiController]
[Route("api/webhooks")]
[EnableRateLimiting("WebhookLimiter")]
public class WebhookController : ControllerBase
{
    private readonly IPostService _postService;
    private readonly ICategoryService _categoryService;
    private readonly IConfiguration _configuration;

    public WebhookController(
        IPostService postService, 
        ICategoryService categoryService, 
        IConfiguration configuration)
    {
        _postService = postService;
        _categoryService = categoryService;
        _configuration = configuration;
    }

    // -------------------------------------------------------------------------
    // POST /api/webhooks/posts
    // Ingests incoming JSON payload from n8n / Make.com and creates a blog draft.
    // -------------------------------------------------------------------------
    [HttpPost("posts")]
    public async Task<IActionResult> IngestPost([FromBody] WebhookPostPayload payload)
    {
        // 1. Validate the secret auth header (X-DevCore-Secret)
        if (!Request.Headers.TryGetValue("X-DevCore-Secret", out var providedSecretHeader) || 
            string.IsNullOrWhiteSpace(providedSecretHeader))
        {
            return Unauthorized(new { success = false, message = "Missing or empty authentication header." });
        }

        // 2. Load configured server secret from environment variable or appsettings
        var expectedSecret = Environment.GetEnvironmentVariable("WEBHOOK_API_SECRET")
            ?? _configuration["DevCoreBlog:WebhookApiKey"];

        if (string.IsNullOrEmpty(expectedSecret))
        {
            return StatusCode(500, new { success = false, message = "Webhook API secret is not configured on the server." });
        }

        // 3. Constant-time comparison to protect against timing side-channel attacks
        var providedBytes = Encoding.UTF8.GetBytes(providedSecretHeader.ToString());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);

        if (providedBytes.Length != expectedBytes.Length || 
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Unauthorized(new { success = false, message = "Invalid webhook secret." });
        }

        // 4. Validate payload integrity
        if (payload == null)
        {
            return BadRequest(new { success = false, message = "Empty JSON payload." });
        }

        if (string.IsNullOrWhiteSpace(payload.Title))
        {
            return BadRequest(new { success = false, message = "Field 'Title' is required." });
        }

        if (string.IsNullOrWhiteSpace(payload.Content))
        {
            return BadRequest(new { success = false, message = "Field 'Content' is required." });
        }

        if (payload.CategoryId <= 0)
        {
            return BadRequest(new { success = false, message = "Valid 'CategoryId' is required." });
        }

        // Verify category exists in database
        var category = await _categoryService.GetCategoryByIdAsync(payload.CategoryId);
        if (category == null)
        {
            return BadRequest(new { success = false, message = $"Category with Id {payload.CategoryId} does not exist." });
        }

        // 5. Map payload to Domain Entity with Fail-Safe Draft mode
        var post = new Post
        {
            Title = payload.Title.Trim(),
            Content = payload.Content,
            Summary = payload.Summary?.Trim() ?? string.Empty,
            Excerpt = payload.Excerpt?.Trim() ?? string.Empty,
            ThumbnailUrl = payload.CoverImageUrl?.Trim() ?? string.Empty,
            CategoryId = payload.CategoryId,
            // Fail-safe: Default to Draft (false) unless explicitly flagged true by automation
            IsPublished = payload.IsPublished,
            IsActive = true,
            PublishDate = payload.PublishDate ?? DateTime.UtcNow
        };

        // 6. Save via PostService (handles automatic slug generation and date tagging)
        await _postService.CreatePostAsync(post);

        // 7. Return structured JSON response
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
}

// -----------------------------------------------------------------------------
// WebhookPostPayload — Minimalist incoming DTO for n8n / Make.com nodes
// -----------------------------------------------------------------------------
public class WebhookPostPayload
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Excerpt { get; set; }
    public string? CoverImageUrl { get; set; }
    public int CategoryId { get; set; }
    public bool IsPublished { get; set; } = false;
    public DateTime? PublishDate { get; set; }
}
