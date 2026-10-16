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
                    new(nameof(Post.PublishDate), "Yazıyı planlamak için gelecekteki bir yayın zamanı seçin.")]);
                post.IsPublished = true; break;
            case PostSaveAction.Publish: post.IsPublished = true; post.PublishDate = utcNow; break;
            case PostSaveAction.Save:
                if (stored is null) return ContentValidationResult.FromErrors([new("SaveAction", "Değişiklikleri kaydetmek için kayıtlı bir yazı gereklidir.")]);
                post.IsPublished = stored.IsPublished; post.PublishDate = stored.PublishDate; break;
            default: return ContentValidationResult.FromErrors([new("SaveAction", "Geçerli bir kayıt eylemi seçin.")]);
        }
        return ContentValidationResult.Success();
    }
}
