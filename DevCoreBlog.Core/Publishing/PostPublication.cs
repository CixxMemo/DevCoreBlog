using System.Linq.Expressions;
using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Publishing;

/// <summary>Shares the public visibility boundary with administrative state presentation.</summary>
public static class PostPublication
{
    public static Expression<Func<Post, bool>> VisibleAt(DateTime utcNow) =>
        post => post.IsActive && post.IsPublished && post.PublishDate <= utcNow &&
            post.Category.IsActive;

    public static PostPublicationState StateAt(Post post, bool categoryIsActive, DateTime utcNow)
    {
        if (!post.IsActive || !categoryIsActive) return PostPublicationState.Inactive;
        if (!post.IsPublished) return PostPublicationState.Draft;
        return post.PublishDate > utcNow ? PostPublicationState.Scheduled : PostPublicationState.Published;
    }
}

/// <summary>Inactive includes posts whose category prevents public visibility.</summary>
public enum PostPublicationState { Draft, Scheduled, Published, Inactive }
