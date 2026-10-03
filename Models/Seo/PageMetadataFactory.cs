using System.Globalization;
using System.Text.Json;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Models.Seo;

/// <summary>Creates public SEO data from stored facts, trusted routes and plain description text.</summary>
public sealed class PageMetadataFactory(PublicUrlBuilder urls, ISafeMarkdownRenderer renderer)
{
    public PageMetadata Home(int page, int size) => new(
        $"DevCoreBlog — Articles{PageLabel(page)}",
        $"Browse articles on DevCoreBlog{PageLabel(page)}.",
        ListUrl("/", page, size));

    public PageMetadata Category(Category category, int page, int size) => new(
        $"{category.Name} — Articles{PageLabel(page)} — DevCoreBlog",
        Description($"Browse articles in {category.Name} on DevCoreBlog{PageLabel(page)}."),
        ListUrl(urls.CategoryPath(category.Slug), page, size));

    public PageMetadata Article(Post post)
    {
        var url = urls.PostUrl(post.Slug);
        var description = Description(post.Summary);
        if (description.Length == 0) description = Description(post.Excerpt);
        if (description.Length == 0) description = Description($"Read {post.Title} on DevCoreBlog.");
        var image = SafeImage(post.ThumbnailUrl);
        var schema = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org", ["@type"] = "BlogPosting",
            ["headline"] = post.Title, ["description"] = description,
            ["url"] = url, ["mainEntityOfPage"] = url,
            ["datePublished"] = post.PublishDate.ToString("O", CultureInfo.InvariantCulture)
        };
        if (post.UpdatedDate is { } updated)
            schema["dateModified"] = updated.ToString("O", CultureInfo.InvariantCulture);
        if (image is not null) schema["image"] = image;
        // Default System.Text.Json escapes HTML-sensitive characters, including script terminators.
        return new($"{post.Title} — DevCoreBlog", description, url, "article", image,
            JsonSerializer.Serialize(schema));
    }

    public PageMetadata Search(string term, string? category, int page, int size)
    {
        var fields = new List<KeyValuePair<string, string?>> { new("query", term) };
        if (category is not null) fields.Add(new("category", category));
        if (page > 1) fields.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        if (size != 9) fields.Add(new("pageSize", size.ToString(CultureInfo.InvariantCulture)));
        return new($"Search — DevCoreBlog", "Search articles on DevCoreBlog.",
            urls.AbsolutePath("/ara" + QueryString.Create(fields).ToUriComponent()), NoIndex: true);
    }

    public PageMetadata Private(string title, string path) => new(
        $"{title} — DevCoreBlog", "Private DevCoreBlog page.", urls.AbsolutePath(path), NoIndex: true);

    private string Description(string value) => string.Concat(
        renderer.ToPlainText(value).EnumerateRunes().Take(160).Select(rune => rune.ToString()));

    private string ListUrl(string path, int page, int size)
    {
        var fields = new List<KeyValuePair<string, string?>>();
        if (page > 1) fields.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        if (size != 9) fields.Add(new("pageSize", size.ToString(CultureInfo.InvariantCulture)));
        return urls.AbsolutePath(path + QueryString.Create(fields).ToUriComponent());
    }

    private static string PageLabel(int page) => page > 1 ? $" — Page {page}" : string.Empty;

    // Existing valid HTTPS covers are used; missing or legacy unsafe URLs add no invented fallback.
    private static string? SafeImage(string value) =>
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
