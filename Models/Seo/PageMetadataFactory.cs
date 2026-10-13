using System.Globalization;
using DevCoreBlog.Models.Public;
using System.Text.Json;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Documents;
using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Models.Seo;

/// <summary>Creates public SEO data from stored facts, trusted routes and plain description text.</summary>
public sealed class PageMetadataFactory(PublicUrlBuilder urls, ISafeMarkdownRenderer renderer)
{
    public PageMetadata Home(int page, int size, PostContentKind? kind = null) => new(
        $"{SiteIdentity.SiteName} — Yazılar{PageLabel(page)}",
        $"{SiteIdentity.SiteName} yazılarını keşfedin{PageLabel(page)}.",
        ListUrl("/", page, size, kind));

    public PageMetadata Category(Category category, int page, int size, PostContentKind? kind = null) => new(
        $"{category.Name} — Yazılar{PageLabel(page)} — {SiteIdentity.SiteName}",
        Description($"{category.Name} kategorisindeki {SiteIdentity.SiteName} yazıları{PageLabel(page)}."),
        ListUrl(urls.CategoryPath(category.Slug), page, size, kind));

    public PageMetadata Article(Post post, DocumentReading? reading = null)
    {
        var url = urls.PostUrl(post.Slug);
        var description = Description(post.Summary, reading is not null);
        if (description.Length == 0) description = Description(post.Excerpt, reading is not null);
        if (description.Length == 0 && reading is not null) description = Description(reading.PlainText, true);
        if (description.Length == 0) description = Description($"{post.Title} — {SiteIdentity.SiteName} yazısı.");
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
        if (reading is not null) schema["wordCount"] = reading.WordCount;
        // Default System.Text.Json escapes HTML-sensitive characters, including script terminators.
        return new($"{post.Title} — {SiteIdentity.SiteName}", description, url, "article", image,
            JsonSerializer.Serialize(schema));
    }

    public PageMetadata Search(string term, string? category, int page, int size, PostContentKind? kind = null)
    {
        var fields = new List<KeyValuePair<string, string?>> { new("query", term) };
        if (category is not null) fields.Add(new("category", category));
        if (page > 1) fields.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        if (size != 9) fields.Add(new("pageSize", size.ToString(CultureInfo.InvariantCulture)));
        if (PublicContentFilter.Key(kind) is { } key) fields.Add(new("kind", key));
        return new($"Arama — {SiteIdentity.SiteName}", $"{SiteIdentity.SiteName} yazılarında arayın.",
            urls.AbsolutePath("/ara" + QueryString.Create(fields).ToUriComponent()), NoIndex: true);
    }

    // Information pages use the same trusted origin as articles and feeds.
    public PageMetadata Information(string title, string description, string path) => new(
        $"{title} — {SiteIdentity.SiteName}", Description(description), urls.AbsolutePath(path));

    public PageMetadata Private(string title, string path) => new(
        $"{title} — {SiteIdentity.SiteName}", "Özel sayfa.", urls.AbsolutePath(path), NoIndex: true);

    private string Description(string value, bool plainText = false) => string.Concat(
        (plainText ? value : renderer.ToPlainText(value)).EnumerateRunes().Take(160).Select(rune => rune.ToString()));

    private string ListUrl(string path, int page, int size, PostContentKind? kind)
    {
        var fields = new List<KeyValuePair<string, string?>>();
        if (page > 1) fields.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        if (size != 9) fields.Add(new("pageSize", size.ToString(CultureInfo.InvariantCulture)));
        if (PublicContentFilter.Key(kind) is { } key) fields.Add(new("kind", key));
        return urls.AbsolutePath(path + QueryString.Create(fields).ToUriComponent());
    }

    private static string PageLabel(int page) => page > 1 ? $" — Sayfa {page}" : string.Empty;

    // Existing valid HTTPS covers are used; missing or legacy unsafe URLs add no invented fallback.
    private static string? SafeImage(string value) =>
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
