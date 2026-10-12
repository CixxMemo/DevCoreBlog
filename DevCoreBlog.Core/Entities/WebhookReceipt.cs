namespace DevCoreBlog.Core.Entities;

/// <summary>Retains a successful keyed submission and its original result across retries.</summary>
public sealed class WebhookReceipt
{
    // Version 1 retains every legacy key; version 2 occupies a separate durable namespace.
    public int ContractVersion { get; set; } = 1;
    public string Key { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int CreatedPostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime PublishDate { get; set; }

    // Deleting a post keeps the receipt and its original response, preventing recreation.
    public int? PostId { get; set; }
    public Post? Post { get; set; }
}
