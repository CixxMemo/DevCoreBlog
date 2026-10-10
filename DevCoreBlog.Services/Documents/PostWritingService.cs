using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Shared.Helpers;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services.Documents;

/// <summary>Coordinates one validated document/metadata save, with no upload or email side effects.</summary>
public sealed class PostWritingService(IEditorPostRepository repository, IActiveCategoryLookup categories,
    PostDocumentService documents, ContentDocumentValidator validator, DocumentTextProducer text,
    PublicationTimeZone zone, TimeProvider clock, PublicListCacheInvalidator cache)
{
    /// <summary>Exposes original JSON only when independently read facts share one consistent revision.</summary>
    public async Task<EditorPostRead?> ReadAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return null;
        var metadata = await repository.FindMetadataAsync(id, cancellationToken);
        if (metadata is null) return null;
        var document = await documents.ReadAsync(id, cancellationToken);
        return new(metadata, document.Status == PostDocumentReadStatus.Ready &&
            document.EditVersion == metadata.EditVersion ? document.Json : null);
    }

    /// <summary>Validates intent before the atomic write; failures never adopt a newer edit revision.</summary>
    public async Task<EditorPostSave> SaveAsync(EditorPostInput input, CancellationToken cancellationToken)
    {
        try { return await SaveValidatedAsync(input, cancellationToken); }
        catch (EditorPostStorageException)
        {
            return new(EditorPostSaveStatus.Unavailable,
                [new("", "Yazı kaydedilemedi. Metniniz korunuyor; biraz sonra yeniden deneyin.")]);
        }
    }

    private async Task<EditorPostSave> SaveValidatedAsync(EditorPostInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Id < 0 || (input.Id > 0 && (input.EditVersion <= 0 || input.EditVersion == long.MaxValue)))
            return Failed("", "Yazıyı yeniden açıp tekrar deneyin.");
        var stored = input.Id > 0 ? await repository.FindMetadataAsync(input.Id, cancellationToken) : null;
        if (input.Id > 0 && stored is null) return new(EditorPostSaveStatus.NotFound, []);
        if (stored is not null)
        {
            var oldDocument = await documents.ReadAsync(input.Id, cancellationToken);
            if (stored.EditVersion != input.EditVersion || oldDocument.EditVersion != input.EditVersion ||
                oldDocument.Status != PostDocumentReadStatus.Ready)
                return new(EditorPostSaveStatus.Conflict, [new("", "Yazı değişti veya belge güvenle açılamıyor. Değişiklikleriniz korunuyor; güncel yazıyı yeniden açın.")]);
        }
        if (string.IsNullOrWhiteSpace(input.DocumentJson)) return Failed("DocumentJson", "İçerik belgesi gereklidir.");
        var validation = validator.Validate(input.DocumentJson);
        if (validation.Document is not { } document)
            return new(validation.Error?.Kind == DocumentFailureKind.Limit ? EditorPostSaveStatus.Limit : EditorPostSaveStatus.Invalid,
                [new("DocumentJson", "İçerik belgesinin biçimini, bağlantılarını ve sınırlarını kontrol edin.")]);
        var post = new Post {
            Id = input.Id, Title = input.Title, Summary = input.Summary, Excerpt = input.Excerpt,
            CategoryId = input.CategoryId, IsActive = input.IsActive, PublishDate = input.PublishDate,
            ThumbnailAlt = input.ThumbnailAlt, ThumbnailUrl = stored?.ThumbnailUrl ?? string.Empty,
            ThumbnailPublicId = stored?.ThumbnailPublicId, ThumbnailWidth = stored?.ThumbnailWidth,
            ThumbnailHeight = stored?.ThumbnailHeight, ContentKind = stored?.ContentKind ?? PostContentKind.Unclassified,
            AccessScope = stored?.AccessScope ?? PostAccessScope.Public
        };
        PostContentRules.Normalize(post);
        if (stored is not null)
        {
            // A seconds-only form must not truncate a saved subsecond instant when unchanged.
            var displayed = zone.ToSiteTime(stored.PublishDate);
            var displayedSecond = new DateTime(displayed.Ticks - displayed.Ticks % TimeSpan.TicksPerSecond,
                DateTimeKind.Unspecified);
            if (input.PublishDate == displayedSecond) post.PublishDate = stored.PublishDate;
        }
        var errors = PostContentRules.ValidateMetadata(post).Errors.Select(error => new ContentValidationError(error.Field,
            error.Field switch {
                "Title" => "Başlık gereklidir ve en çok 200 karakter olabilir.",
                "Summary" => "Özet en çok 500 karakter olabilir.",
                "Excerpt" => "Kısa açıklama en çok 1000 karakter olabilir.",
                "CategoryId" => "Etkin bir kategori seçin.",
                "ThumbnailAlt" => "Kapak açıklaması en çok 300 karakter olabilir.",
                _ => "Kayıtlı kapak bilgileri geçersiz; kaydetme durduruldu."
            })).ToList();
        if (!zone.TryConvertToUtc(post.PublishDate, out var date, out _))
            errors.Add(new("PublishDate", "Geçerli bir site yayın zamanı seçin."));
        else post.PublishDate = date;
        if (post.CategoryId > 0 && !await categories.IsActiveCategoryAsync(post.CategoryId, cancellationToken))
            errors.Add(new("CategoryId", "Etkin bir kategori seçin."));
        if (errors.Count > 0) return new(EditorPostSaveStatus.Invalid, errors);
        var now = clock.GetUtcNow().UtcDateTime;
        var publication = EditorPublicationIntent.Apply(post, stored, input.SaveAction, now);
        if (!publication.IsValid) return new(EditorPostSaveStatus.Invalid, publication.Errors.Select(error =>
            new ContentValidationError(error.Field, error.Field == "PublishDate"
                ? "Planlamak için gelecekte bir yayın zamanı seçin." : "Geçerli bir kayıt eylemi seçin.")).ToArray());
        var reading = text.Produce(document);
        post.DocumentVersion = DocumentLimits.Version;
        post.DocumentJson = input.DocumentJson;
        post.DocumentPlainText = reading.PlainText;
        post.DocumentWordCount = reading.WordCount;
        post.DocumentReadingMinutes = reading.ReadingMinutes;
        if (stored is null)
        {
            post.CreatedDate = now;
            var slug = SlugGenerator.GenerateBase(post.Title, "post");
            var created = false;
            for (var attempt = 0; attempt < 100 && !created; attempt++)
            {
                post.Slug = SlugGenerator.Candidate(slug, attempt);
                created = await repository.TryCreateAsync(post, cancellationToken);
            }
            if (!created) return Failed("Title", "Benzersiz adres ayrılamadı. Başlığı değiştirip tekrar deneyin.");
        }
        else
        {
            post.UpdatedDate = now;
            if (!await repository.TryUpdateAsync(post, input.EditVersion, cancellationToken))
                return new(EditorPostSaveStatus.Conflict, [new("", "Yazı değişti. Metniniz korunuyor; güncel yazıyı yeniden açın.")]);
        }
        await cache.InvalidateAsync(cancellationToken);
        return new(EditorPostSaveStatus.Saved, [], post.Id);
    }

    private static EditorPostSave Failed(string field, string message) => new(EditorPostSaveStatus.Invalid, [new(field, message)]);
}

/// <summary>Only editable metadata and the canonical envelope cross the writing boundary.</summary>
public sealed record EditorPostInput(int Id, long EditVersion, string Title, string DocumentJson,
    int CategoryId, string Summary, string Excerpt, string? ThumbnailAlt, bool IsActive,
    DateTime PublishDate, PostSaveAction SaveAction);
public sealed record EditorPostRead(Post Metadata, string? Json);
public enum EditorPostSaveStatus { Saved, Invalid, Limit, NotFound, Conflict, Unavailable }
public sealed record EditorPostSave(EditorPostSaveStatus Status, IReadOnlyList<ContentValidationError> Errors, int? Id = null);
