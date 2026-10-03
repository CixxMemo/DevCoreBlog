using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Routing;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml;

namespace DevCoreBlog.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SeoController : Controller
{
    private readonly IPostService _postService;
    private readonly ICategoryService _categoryService;
    private readonly PublicUrlBuilder _publicUrls;

    public SeoController(IPostService postService, ICategoryService categoryService, PublicUrlBuilder publicUrls)
    {
        _postService = postService;
        _categoryService = categoryService;
        _publicUrls = publicUrls;
    }

    [Route("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {

        var sb = new StringBuilder();
        var xmlSettings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true
        };

        using (var xml = XmlWriter.Create(sb, xmlSettings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            // 1. Home Page
            xml.WriteStartElement("url");
            xml.WriteElementString("loc", _publicUrls.AbsolutePath("/"));
            xml.WriteElementString("changefreq", "daily");
            xml.WriteElementString("priority", "1.0");
            xml.WriteEndElement();

            // 2. Categories
            var categories = await _categoryService.GetActiveCategoriesAsync();
            foreach (var category in categories)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", _publicUrls.CategoryUrl(category.Slug));
                xml.WriteElementString("changefreq", "weekly");
                xml.WriteElementString("priority", "0.8");
                xml.WriteEndElement();
            }

            // 3. Posts
            var posts = await _postService.GetPublishedPostsAsync();
            foreach (var post in posts)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", _publicUrls.PostUrl(post.Slug));
                xml.WriteElementString("lastmod", post.CreatedDate.ToString("yyyy-MM-dd"));
                xml.WriteElementString("changefreq", "monthly");
                xml.WriteElementString("priority", "0.6");
                xml.WriteEndElement();
            }

            xml.WriteEndElement(); // end urlset
            xml.WriteEndDocument();
        }

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
