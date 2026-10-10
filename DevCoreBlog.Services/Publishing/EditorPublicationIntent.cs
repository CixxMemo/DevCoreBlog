using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Validation;

namespace DevCoreBlog.Services.Publishing;

/// <summary>Shares explicit site publication intent across the legacy and JSON writers.</summary>
public static class EditorPublicationIntent
{
    public static ContentValidationResult Apply(Post post, Post? stored, PostSaveAction action, DateTime utcNow)
    {
        switch (action)
        {
            case PostSaveAction.SaveDraft: post.IsPublished = false; break;
            case PostSaveAction.Schedule:
                if (post.PublishDate <= utcNow) return ContentValidationResult.FromErrors([
                    new(nameof(Post.PublishDate), "Choose a future publish date to schedule this post.")]);
                post.IsPublished = true; break;
            case PostSaveAction.Publish: post.IsPublished = true; post.PublishDate = utcNow; break;
            case PostSaveAction.Save:
                if (stored is null) return ContentValidationResult.FromErrors([new("SaveAction", "Save changes requires an existing post.")]);
                post.IsPublished = stored.IsPublished; post.PublishDate = stored.PublishDate; break;
            default: return ContentValidationResult.FromErrors([new("SaveAction", "Choose a valid save action.")]);
        }
        return ContentValidationResult.Success();
    }
}
