using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
namespace DevCoreBlog.Models.Public;

// Presentation only: callers supply posts already selected by the public service.
public sealed record PostCardModel(PublicPostSummary Post, PostCardPresentation Presentation = PostCardPresentation.Standard)
{
    public PostCardModel(Post post, PostCardPresentation presentation = PostCardPresentation.Standard)
        : this(new PublicPostSummary(post.Id, post.Title, post.Slug, post.Summary, post.ThumbnailUrl,
            post.Category?.Name ?? "General", post.PublishDate, post.ViewCount), presentation) { }
}

// Finite layouts share markup without dynamic utility names.
public enum PostCardPresentation { Standard, Featured, Compact }
