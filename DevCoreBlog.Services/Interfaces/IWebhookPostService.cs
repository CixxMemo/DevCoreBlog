using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Validation;

namespace DevCoreBlog.Services.Interfaces;

/// <summary>Creates a validated post or replays its durable submission result.</summary>
public interface IWebhookPostService
{
    Task<WebhookPostResult> CreateAsync(
        Post post, string key, string payloadHash, CancellationToken cancellationToken);
}

/// <summary>Contains only the original response fields, independent of later post edits.</summary>
public sealed record WebhookPostSnapshot(
    int Id, string Title, string Slug, bool IsPublished, DateTime PublishDate)
{
    public static WebhookPostSnapshot FromPost(Post post) =>
        new(post.Id, post.Title, post.Slug, post.IsPublished, post.PublishDate);

    public static WebhookPostSnapshot FromReceipt(WebhookReceipt receipt) =>
        new(receipt.CreatedPostId, receipt.Title, receipt.Slug, receipt.IsPublished, receipt.PublishDate);
}

/// <summary>Separates validation failure, key conflict and successful creation or replay.</summary>
public sealed record WebhookPostResult(
    ContentValidationResult Validation, WebhookPostSnapshot? Post = null, bool KeyConflict = false);
