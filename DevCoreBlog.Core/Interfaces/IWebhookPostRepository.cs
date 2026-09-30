using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Interfaces;

/// <summary>Atomically persists a post with its durable receipt; null means a slug collision.</summary>
public interface IWebhookPostRepository
{
    Task<WebhookReceipt?> FindAsync(string key, CancellationToken cancellationToken);
    Task<WebhookInsertResult?> TryCreateAsync(
        Post post, string key, string payloadHash, CancellationToken cancellationToken);
}

/// <summary>Distinguishes a committed insert from a receipt won by another request.</summary>
public sealed record WebhookInsertResult(WebhookReceipt Receipt, bool Created);
