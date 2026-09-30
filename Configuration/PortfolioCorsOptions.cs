namespace DevCoreBlog.Configuration;

/// <summary>Keeps the browser origin allowlist consistent between middleware and admin guidance.</summary>
public sealed class PortfolioCorsOptions
{
    public IReadOnlyList<string> Origins { get; }

    public PortfolioCorsOptions(string? value, bool isDevelopment)
    {
        var candidate = value ?? (isDevelopment ? "http://localhost:3000,http://localhost:5173" : string.Empty);
        var origins = new List<string>();
        foreach (var item in candidate.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!HttpOrigin.TryParse(item, out var parsed) || parsed is null)
            {
                throw new InvalidOperationException(
                    "PORTFOLIO_CORS_ORIGIN must contain only comma-separated HTTP or HTTPS origins.");
            }
            origins.Add(parsed.GetLeftPart(UriPartial.Authority));
        }
        Origins = Array.AsReadOnly(origins.Distinct(StringComparer.Ordinal).ToArray());
    }
}
