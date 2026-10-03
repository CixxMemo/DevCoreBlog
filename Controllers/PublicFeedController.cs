using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Serves the public portfolio JSON contract with trusted links and bounded reads.</summary>
[ApiController]
[Route("api/public")]
[EnableCors("PortfolioPolicy")]
[EnableRateLimiting("PortfolioLimiter")]
public sealed class PublicFeedController(
    IPublicFeedService feedService,
    PublicUrlBuilder publicUrls) : ControllerBase
{
    [HttpGet("posts/latest")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetLatestPosts(CancellationToken cancellationToken)
    {
        var posts = await feedService.GetLatestPublicPostsAsync(cancellationToken);
        var latestPosts = posts.Select(post => new
        {
            id = post.Id,
            title = post.Title,
            slug = post.Slug,
            summary = post.Summary,
            excerpt = post.Excerpt,
            coverImageUrl = post.CoverImageUrl,
            publishDate = post.PublishDate,
            url = publicUrls.PostUrl(post.Slug),
            categoryName = post.CategoryName
        }).ToList();
        return Ok(latestPosts);
    }
}
