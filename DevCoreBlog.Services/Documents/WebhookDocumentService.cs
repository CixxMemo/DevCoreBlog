using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services.Documents;

/// <summary>Creates one canonical document and its v2 receipt atomically, without sending email.</summary>
public sealed class WebhookDocumentService(IWebhookPostRepository receipts, IActiveCategoryLookup categories,
    ContentDocumentValidator validator, DocumentTextProducer text, TimeProvider clock, PublicListCacheInvalidator cache)
{
    public const int ContractVersion = 2;

    public async Task<WebhookDocumentResult> CreateAsync(WebhookDocumentInput input, string key,
        string payloadHash, bool allowPublish, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await receipts.FindAsync(key, cancellationToken, ContractVersion);
        if (stored is not null) return Replay(stored, payloadHash);
        if (input.DocumentJson is null) return Invalid("DocumentJson", "İçerik belgesi gereklidir.");
        var validation = validator.Validate(input.DocumentJson);
        if (validation.Document is not { } document)
            return new(validation.Error?.Kind == DocumentFailureKind.Limit ? WebhookDocumentStatus.Limit : WebhookDocumentStatus.Invalid,
                Errors: [new("DocumentJson", "İçerik belgesinin sürümünü, biçimini, bağlantılarını ve sınırlarını kontrol edin.")]);
        var now = clock.GetUtcNow().UtcDateTime;
        var post = new Post {
            Title = input.Title ?? string.Empty, Summary = input.Summary ?? string.Empty, Excerpt = input.Excerpt ?? string.Empty,
            CategoryId = input.CategoryId, ContentKind = input.ContentKind, AccessScope = input.AccessScope,
            IsActive = true, IsPublished = input.IsPublished && allowPublish,
            PublishDate = input.PublishDate?.UtcDateTime ?? now, CreatedDate = now
        };
        PostContentRules.Normalize(post);
        var errors = PostContentRules.ValidateMetadata(post).Errors.Concat(PostAccessRules.Validate(post).Errors)
            .Select(e => new ContentValidationError(e.Field, "Alan değerini ve sınırlarını kontrol edin.")).ToList();
        if (post.ContentKind == PostContentKind.Unclassified) errors.Add(new("ContentKind", "Bir içerik türü seçin."));
        if (post.CategoryId > 0 && !await categories.IsActiveCategoryAsync(post.CategoryId, cancellationToken))
            errors.Add(new("CategoryId", "Etkin bir kategori seçin."));
        if (errors.Count > 0) return new(WebhookDocumentStatus.Invalid, Errors: errors);
        var reading = text.Produce(document);
        post.DocumentVersion = document.Document.Version;
        post.DocumentJson = input.DocumentJson;
        post.DocumentPlainText = reading.PlainText;
        post.DocumentWordCount = reading.WordCount;
        post.DocumentReadingMinutes = reading.ReadingMinutes;
        var baseSlug = SlugGenerator.GenerateBase(post.Title, "post");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            post.Slug = SlugGenerator.Candidate(baseSlug, attempt);
            var result = await receipts.TryCreateAsync(post, key, payloadHash, cancellationToken, ContractVersion);
            if (result is null) continue;
            if (result.Created) await cache.InvalidateAsync(cancellationToken);
            return Replay(result.Receipt, payloadHash);
        }
        // A competing request can win while this request runs through occupied slug candidates.
        stored = await receipts.FindAsync(key, cancellationToken, ContractVersion);
        return stored is not null ? Replay(stored, payloadHash) : Invalid("Title", "Benzersiz adres ayrılamadı.");
    }

    private static WebhookDocumentResult Replay(WebhookReceipt receipt, string hash) => receipt.PayloadHash == hash
        ? new(WebhookDocumentStatus.Saved, WebhookPostSnapshot.FromReceipt(receipt)) : new(WebhookDocumentStatus.Conflict);
    private static WebhookDocumentResult Invalid(string field, string message) =>
        new(WebhookDocumentStatus.Invalid, Errors: [new(field, message)]);
}

/// <summary>Client intent contains neither persisted identity nor server-derived document facts.</summary>
public sealed record WebhookDocumentInput(string? Title, string? DocumentJson, int CategoryId,
    PostContentKind ContentKind, PostAccessScope AccessScope, string? Summary, string? Excerpt,
    bool IsPublished, DateTimeOffset? PublishDate);
public enum WebhookDocumentStatus { Saved, Invalid, Limit, Conflict }
public sealed record WebhookDocumentResult(WebhookDocumentStatus Status, WebhookPostSnapshot? Post = null,
    IReadOnlyList<ContentValidationError>? Errors = null);
