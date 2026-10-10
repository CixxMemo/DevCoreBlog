using DevCoreBlog.Core.Validation;
using DevCoreBlog.Services.Operations;
using DevCoreBlog.Services.Images;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Services;

/// <summary>Validates uploads and records only safe completed storage outcomes.</summary>
public sealed class ImageService : IImageService
{
    private const string StorageFailureMessage =
        "Image storage is temporarily unavailable. Try again.";

    private readonly MediaOperationStatus _status;
    private readonly ImageUploadPolicy _policy;
    private readonly IImageStorage _storage;
    private readonly ILogger<ImageService> _logger;

    public ImageService(
        ImageUploadPolicy policy,
        IImageStorage storage,
        ILogger<ImageService> logger,
        MediaOperationStatus status)
    {
        _status = status;
        _policy = policy;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Rejects invalid input before storage; operation observations do not imply a successful post save.</summary>
    public async Task<ImageUploadOutcome> UploadImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        var validation = await _policy.ValidateAsync(file, cancellationToken);
        if (!validation.IsValid || validation.Content is null || validation.ProviderFormat is null)
        {
            return ImageUploadOutcome.Failure(
                validation.FailureKind,
                validation.Message);
        }

        await using var stream = validation.Content.OpenRead();
        var storedImage = await _storage.UploadAsync(
            stream,
            validation.ProviderFormat,
            cancellationToken);

        if (!IsSafeStorageResult(storedImage))
        {
            _status.Record(false);
            _logger.LogWarning("Image storage returned an unsuccessful or invalid result.");
            return ImageUploadOutcome.Failure(
                ImageUploadFailureKind.StorageUnavailable,
                StorageFailureMessage);
        }

        _status.Record(true);
        return ImageUploadOutcome.Success(storedImage.Url!, storedImage.PublicId!,
            storedImage.Width, storedImage.Height);
    }

    private bool IsSafeStorageResult(ImageStorageOutcome result)
    {
        return result.Succeeded &&
            Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
            result.Url?.Length <= PostContentRules.MaximumThumbnailUrlLength &&
            !string.IsNullOrWhiteSpace(result.PublicId) &&
            result.PublicId.Length <= PostContentRules.MaximumThumbnailPublicIdLength &&
            !result.PublicId.Any(char.IsControl) &&
            _policy.IsAllowedProviderFormat(result.Format) &&
            result.Width is > 0 and <= ImageUploadPolicy.MaximumDimension &&
            result.Height is > 0 and <= ImageUploadPolicy.MaximumDimension;
    }
}
