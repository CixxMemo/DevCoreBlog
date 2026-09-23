using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Validation;

/// <summary>
/// Defines and evaluates the provider-independent rules for editable post content.
/// </summary>
public static class PostContentRules
{
    public const int MaximumTitleLength = 200;
    public const int MaximumSummaryLength = 500;
    public const int MaximumExcerptLength = 1_000;
    public const int MaximumContentLength = 200_000;
    public const int MaximumThumbnailUrlLength = 2_048;

    public static void Normalize(Post post)
    {
        ArgumentNullException.ThrowIfNull(post);

        post.Title = post.Title?.Trim() ?? string.Empty;
        post.Content ??= string.Empty;
        post.Summary = post.Summary?.Trim() ?? string.Empty;
        post.Excerpt = post.Excerpt?.Trim() ?? string.Empty;
        post.ThumbnailUrl = post.ThumbnailUrl?.Trim() ?? string.Empty;
    }

    public static ContentValidationResult Validate(Post post)
    {
        ArgumentNullException.ThrowIfNull(post);

        var errors = new List<ContentValidationError>();
        ValidateRequiredText(
            errors,
            nameof(Post.Title),
            post.Title,
            MaximumTitleLength,
            "Title");
        ValidateRequiredText(
            errors,
            nameof(Post.Content),
            post.Content,
            MaximumContentLength,
            "Content");
        ValidateOptionalText(
            errors,
            nameof(Post.Summary),
            post.Summary,
            MaximumSummaryLength,
            "Summary");
        ValidateOptionalText(
            errors,
            nameof(Post.Excerpt),
            post.Excerpt,
            MaximumExcerptLength,
            "Excerpt");

        if (post.CategoryId <= 0)
        {
            errors.Add(new(nameof(Post.CategoryId), "Select an active category."));
        }

        ValidateThumbnailUrl(errors, post.ThumbnailUrl);
        return ContentValidationResult.FromErrors(errors);
    }

    private static void ValidateRequiredText(
        ICollection<ContentValidationError> errors,
        string field,
        string? value,
        int maximumLength,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, $"{displayName} is required."));
            return;
        }

        ValidateOptionalText(errors, field, value, maximumLength, displayName);
    }

    private static void ValidateOptionalText(
        ICollection<ContentValidationError> errors,
        string field,
        string? value,
        int maximumLength,
        string displayName)
    {
        if (value is not null && value.Length > maximumLength)
        {
            errors.Add(new(
                field,
                $"{displayName} cannot exceed {maximumLength} characters."));
        }
    }

    private static void ValidateThumbnailUrl(
        ICollection<ContentValidationError> errors,
        string? thumbnailUrl)
    {
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            return;
        }

        if (thumbnailUrl.Length > MaximumThumbnailUrlLength)
        {
            errors.Add(new(
                nameof(Post.ThumbnailUrl),
                $"Cover image URL cannot exceed {MaximumThumbnailUrlLength} characters."));
            return;
        }

        if (!Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            errors.Add(new(
                nameof(Post.ThumbnailUrl),
                "Cover image URL must be an absolute HTTPS URL."));
        }
    }
}
