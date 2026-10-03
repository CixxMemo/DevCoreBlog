using DevCoreBlog.Models.Seo;
using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace DevCoreBlog.Controllers;

/// <summary>Serves fresh crawler documents; robots directives never grant access.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SeoController(
    ISitemapPostReader posts,
    ISitemapCategoryReader categories,
    PublicUrlBuilder publicUrls,
    ILogger<SeoController> logger) : Controller
{
    [HttpGet("sitemap.xml")]
    [HttpHead("sitemap.xml")]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        // Sequential reads share the request's scoped DbContext; no entities or HTML are loaded.
        var categorySlugs = await categories.GetSitemapCategorySlugsAsync(cancellationToken);
        var visiblePosts = await posts.GetSitemapPostsAsync(cancellationToken);
        if (1 + categorySlugs.Count + visiblePosts.Count > SitemapDocumentWriter.MaximumUrls)
            return SitemapOverflow();

        var entries = new List<(string Url, DateTime? LastModified)>
        {
            (publicUrls.AbsolutePath("/"), null)
        };
        entries.AddRange(categorySlugs.Select(slug => (publicUrls.CategoryUrl(slug), (DateTime?)null)));
        entries.AddRange(visiblePosts.Select(post =>
            (publicUrls.PostUrl(post.Slug), (DateTime?)post.LastModifiedUtc)));
        var bytes = SitemapDocumentWriter.Write(entries);
        if (bytes.Length > SitemapDocumentWriter.MaximumBytes || entries.Any(entry => entry.Url.Length >= 2048))
            return SitemapOverflow();
        return File(bytes, "application/xml; charset=utf-8");
    }

    [HttpGet("robots.txt")]
    [HttpHead("robots.txt")]
    public IActionResult Robots() => Content(
        $"User-agent: *\nAllow: /\nSitemap: {publicUrls.AbsolutePath("/sitemap.xml")}\n",
        "text/plain", Encoding.UTF8);

    private IActionResult SitemapOverflow()
    {
        // Fail visibly instead of publishing a silently truncated or invalid document.
        logger.LogWarning("Sitemap exceeded single-document protocol limits; partitioning is required.");
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
