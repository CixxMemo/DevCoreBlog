namespace DevCoreBlog.Configuration;

/// <summary>Validates an environment-only login path without exposing its value in errors.</summary>
public sealed class AdminLoginOptions
{
    public string Path { get; }

    public AdminLoginOptions(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 160 || path[0] != '/' ||
            path.EndsWith('/') || path.Contains("//", StringComparison.Ordinal) ||
            path.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '/'))
            throw new InvalidOperationException("ADMIN_LOGIN_PATH must be a bounded local path using letters, digits, hyphens or underscores.");

        var segments = path[1..].Split('/');
        // Reserve public routes, static assets and other controllers; Account/Login remains
        // configurable for synthetic legacy fixtures, never as a missing-config fallback.
        string[] reserved = ["about", "contact", "sign-in", "ara", "post", "category", "yazi", "kategori",
            "api", "css", "js", "generated", "images", "Home", "Admin", "AdminPost", "AdminCategory",
            "Information", "ReaderAccount", "Seo", "PublicFeed", "Webhook"];
        if (segments.Length > 2 || reserved.Contains(segments[0], StringComparer.OrdinalIgnoreCase) ||
            (segments[0].Equals("Account", StringComparison.OrdinalIgnoreCase) &&
                (segments.Length != 2 || !segments[1].Equals("Login", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("ADMIN_LOGIN_PATH conflicts with a reserved route or exceeds two segments.");

        Path = path;
    }
}
