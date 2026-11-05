using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Controllers;

/// <summary>Old newsletter addresses keep working: one permanent redirect to the single public address, hidden posts stay 404.</summary>
public sealed class NewsletterController(IPostService posts, PublicUrlBuilder urls) : Controller
{
    private const int MaximumSlugLength = 200;

    [HttpGet("/bulten")]
    [HttpHead("/bulten")]
    [HttpGet("/bultenler")]
    [HttpHead("/bultenler")]
    public IActionResult Index() => RedirectPermanent("/");

    [HttpGet("/bulten/{slug}")]
    [HttpHead("/bulten/{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > MaximumSlugLength) return BadRequest();
        var post = await posts.GetPostBySlugAsync(slug);
        return post is null ? NotFound() : RedirectPermanent(urls.PostPath(post.Slug));
    }
}
