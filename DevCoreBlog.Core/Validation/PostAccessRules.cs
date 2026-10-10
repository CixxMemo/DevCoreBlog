using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Validation;

/// <summary>Rejects unknown classifications and any public newsletter body.</summary>
public static class PostAccessRules
{
    public static ContentValidationResult Validate(Post post)
    {
        ArgumentNullException.ThrowIfNull(post);
        var errors = new List<ContentValidationError>();
        if (!Enum.IsDefined(post.ContentKind))
            errors.Add(new(nameof(Post.ContentKind), "Choose a valid content kind."));
        if (!Enum.IsDefined(post.AccessScope))
            errors.Add(new(nameof(Post.AccessScope), "Choose a valid access scope."));
        if (post.ContentKind == PostContentKind.Newsletter && post.AccessScope != PostAccessScope.Subscribers)
            errors.Add(new(nameof(Post.AccessScope), "Newsletter bodies require subscriber access."));
        return ContentValidationResult.FromErrors(errors);
    }
}
