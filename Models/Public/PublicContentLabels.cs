using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Models.Public;

/// <summary>Reader labels belong to presentation, separate from domain discovery keys.</summary>
public static class PublicContentLabels
{
    public static string Label(PostContentKind kind) => kind switch {
        PostContentKind.AiNews => "AI haberleri", PostContentKind.Experience => "Deney ve deneyim",
        PostContentKind.Philosophy => "Felsefe", _ => "" };
}
