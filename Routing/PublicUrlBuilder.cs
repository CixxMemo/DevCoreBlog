using DevCoreBlog.Configuration;

namespace DevCoreBlog.Routing;

/// <summary>Builds escaped public paths from named routes and absolute links from trusted configuration.</summary>
public sealed class PublicUrlBuilder(SiteUrlOptions siteUrl, LinkGenerator links)
{
    public string PostPath(string slug) => RoutePath("post-en", slug);
    public string CategoryPath(string slug) => RoutePath("category-en", slug);
    public string PostUrl(string slug) => AbsolutePath(PostPath(slug));
    public string CategoryUrl(string slug) => AbsolutePath(CategoryPath(slug));

    public string AbsolutePath(string path)
    {
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Any(char.IsControl))
            throw new ArgumentException("An escaped local absolute path is required.", nameof(path));
        return siteUrl.Origin + path;
    }

    private string RoutePath(string route, string slug) =>
        links.GetPathByRouteValues(route, new { slug })
        ?? throw new InvalidOperationException("The named public route is required.");
}
