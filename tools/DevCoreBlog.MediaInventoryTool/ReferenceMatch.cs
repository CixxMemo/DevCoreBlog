using System.Net;

/// <summary>Provider-export identity plus explicit delivery aliases; never inferred from stored URLs.</summary>
internal sealed record InventoryAsset(string PublicId, string[] Urls)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(PublicId) && PublicId.Length <= 255 &&
        !PublicId.Any(char.IsControl) && Urls is { Length: > 0 and <= 10 } &&
        Urls.All(url => url is { Length: <= 2048 } && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo));
}

/// <summary>Conservative reference classification; a missing match is never a safe-delete verdict.</summary>
internal sealed class ReferenceMatch(InventoryAsset asset)
{
    private readonly string[] _urls = asset.Urls.Select(Normalize).ToArray();
    private readonly List<int> _postIds = [];
    private int _references;
    private int _possibleReferences;

    public void Observe(int id, string? coverId, string[] texts)
    {
        // HTML entities and percent encoding are presentation variants, not identity inference.
        var confirmed = coverId == asset.PublicId ||
            _urls.Any(url => texts.Any(text => text.Contains(url, StringComparison.Ordinal)));
        var possible = !confirmed && texts.Any(text => text.Contains(asset.PublicId, StringComparison.Ordinal));
        if (!confirmed && !possible) return;
        if (confirmed) _references++; else _possibleReferences++;
        if (_postIds.Count < 20) _postIds.Add(id);
    }

    public object Report() => new
    {
        publicId = asset.PublicId,
        status = _references > 0 ? "Referenced" : _possibleReferences > 0 ? "Review required" : "No stored reference found",
        referencingPosts = _references, possibleReferencingPosts = _possibleReferences,
        samplePostIds = _postIds, samplesTruncated = _references + _possibleReferences > _postIds.Count
    };

    public static string Normalize(string text) => Uri.UnescapeDataString(WebUtility.HtmlDecode(text));
}
