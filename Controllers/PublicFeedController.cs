// =============================================================================
// PublicFeedController.cs — Public Read-Only Feed for Portfolio Showcase
// =============================================================================
// This controller exposes a lightweight, rate-limited public API endpoint
// designed specifically for the author's React portfolio site to display
// the latest 3 published blog articles with backlinks.
//
// Security Standards:
//   1. CORS Policy: Restricted to the portfolio origin via PortfolioPolicy.
//   2. Rate Limiting: Protected by PortfolioLimiter (Max 30 req/min).
//   3. Direct Projection: Returns a lightweight projection without bloated DTOs.
// =============================================================================

using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

[ApiController]
[Route("api/public")]
[EnableCors("PortfolioPolicy")]
[EnableRateLimiting("PortfolioLimiter")]
public class PublicFeedController : ControllerBase
{
    private readonly IPostService _postService;

    public PublicFeedController(IPostService postService)
    {
        _postService = postService;
    }

    // -------------------------------------------------------------------------
    // GET /api/public/posts/latest
    // Returns the 3 most recently published blog posts for external showcases.
    // -------------------------------------------------------------------------
    [HttpGet("posts/latest")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetLatestPosts()
    {
        // The service applies the shared visibility rule and database-side limit.
        var posts = await _postService.GetLatestPublicPostsAsync();
        var latestPosts = posts
            .Select(p => new
            {
                id = p.Id,
                title = p.Title,
                slug = p.Slug,
                summary = p.Summary,
                excerpt = p.Excerpt,
                coverImageUrl = p.ThumbnailUrl,
                publishDate = p.PublishDate,
                // Generate absolute public canonical URL
                url = $"{Request.Scheme}://{Request.Host}/post/{p.Slug}",
                categoryName = p.Category?.Name ?? "General"
            })
            .ToList();

        // Return the preserved JSON shape.
        return Ok(latestPosts);
    }
}
