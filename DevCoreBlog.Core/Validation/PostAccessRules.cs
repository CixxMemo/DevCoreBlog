using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Validation;

/// <summary>Rejects unknown classifications and keeps the stored pairing the newsletter database constraint requires.</summary>
public static class PostAccessRules
{
    public static ContentValidationResult Validate(Post post)
    {
        ArgumentNullException.ThrowIfNull(post);
        var errors = new List<ContentValidationError>();
        if (!Enum.IsDefined(post.ContentKind))
            errors.Add(new(nameof(Post.ContentKind), "Geçerli bir içerik türü seçin."));
        if (!Enum.IsDefined(post.AccessScope))
            errors.Add(new(nameof(Post.AccessScope), "Geçerli bir erişim kapsamı seçin."));
        if (post.ContentKind == PostContentKind.Newsletter && post.AccessScope != PostAccessScope.Subscribers)
            errors.Add(new(nameof(Post.AccessScope), "Bülten türü yalnız kendi erişim kapsamıyla saklanır."));
        return ContentValidationResult.FromErrors(errors);
    }
}
