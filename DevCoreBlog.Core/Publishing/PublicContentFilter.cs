using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Publishing;

/// <summary>Finite discovery keys map only to public article kinds; no key can grant subscriber access.</summary>
public static class PublicContentFilter
{
    public static IReadOnlyList<PostContentKind> Choices { get; } = Array.AsReadOnly(new[] {
        PostContentKind.AiNews, PostContentKind.Experience, PostContentKind.Philosophy });
    public static string? Key(PostContentKind? kind) => kind switch {
        PostContentKind.AiNews => "news", PostContentKind.Experience => "experience",
        PostContentKind.Philosophy => "philosophy", _ => null };
    public static bool TryParse(string? key, out PostContentKind? kind)
    {
        kind = key switch { "news" => PostContentKind.AiNews, "experience" => PostContentKind.Experience,
            "philosophy" => PostContentKind.Philosophy, _ => null };
        return string.IsNullOrEmpty(key) || kind is not null;
    }
    public static void Validate(PostContentKind? kind)
    {
        if (kind is not null && !Choices.Contains(kind.Value)) throw new ArgumentOutOfRangeException(nameof(kind));
    }
}
