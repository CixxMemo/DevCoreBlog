using System.Linq.Expressions;
using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Publishing;

/// <summary>One state rule supplies both in-memory presentation and translatable database filters.</summary>
public static class PostPublication
{
    private static readonly Expression<Func<bool, bool, bool, DateTime, DateTime, PostPublicationState>> StateRule =
        (active, categoryActive, published, date, now) => !active || !categoryActive
            ? PostPublicationState.Inactive : !published ? PostPublicationState.Draft
            : date > now ? PostPublicationState.Scheduled : PostPublicationState.Published;
    private static readonly Func<bool, bool, bool, DateTime, DateTime, PostPublicationState> EvaluateState = StateRule.Compile();

    public static PostPublicationState StateAt(Post post, bool categoryIsActive, DateTime utcNow) =>
        EvaluateState(post.IsActive, categoryIsActive, post.IsPublished, post.PublishDate, utcNow);

    public static PostPublicationState StateAt(bool active, bool categoryActive, bool published, DateTime date, DateTime utcNow) =>
        EvaluateState(active, categoryActive, published, date, utcNow);

    public static Expression<Func<Post, PostPublicationState>> StateExpressionAt(DateTime utcNow)
    {
        var post = Expression.Parameter(typeof(Post), "post");
        Expression[] values = [Expression.Property(post, nameof(Post.IsActive)),
            Expression.Property(Expression.Property(post, nameof(Post.Category)), nameof(Category.IsActive)),
            Expression.Property(post, nameof(Post.IsPublished)), Expression.Property(post, nameof(Post.PublishDate)),
            Expression.Constant(utcNow)];
        var replacements = StateRule.Parameters.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second);
        var body = new StateParameterBinding(replacements).Visit(StateRule.Body);
        return Expression.Lambda<Func<Post, PostPublicationState>>(body, post);
    }

    public static Expression<Func<Post, bool>> InStateAt(PostPublicationState state, DateTime utcNow)
    {
        var rule = StateExpressionAt(utcNow);
        return Expression.Lambda<Func<Post, bool>>(Expression.Equal(rule.Body, Expression.Constant(state)), rule.Parameters);
    }

    public static Expression<Func<Post, bool>> VisibleAt(DateTime utcNow) =>
        post => post.IsActive && post.IsPublished && post.PublishDate <= utcNow && post.Category.IsActive;

    // Inline scalar parameters so providers receive a plain expression, never Compile/Invoke in SQL.
    private sealed class StateParameterBinding(IReadOnlyDictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : node;
    }
}

/// <summary>Inactive includes posts whose category prevents public visibility.</summary>
public enum PostPublicationState { Draft, Scheduled, Published, Inactive }
