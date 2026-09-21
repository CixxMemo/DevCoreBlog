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
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)] // Cache for 5 minutes
    public async Task<IActionResult> GetLatestPosts()
    {
        // 1. Fetch all posts from the service layer
        var allPosts = await _postService.GetAllPostsAsync();

        // 2. Filter for active and published posts, order by PublishDate descending, take top 3
        var latestPosts = allPosts
            .Where(p => p.IsActive && p.IsPublished)
            .OrderByDescending(p => p.PublishDate)
            .Take(3)
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

        // 3. Return JSON array directly
        return Ok(latestPosts);
    }
}
