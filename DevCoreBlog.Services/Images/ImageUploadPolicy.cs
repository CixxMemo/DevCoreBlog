using DevCoreBlog.Core.Validation;
using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Services.Images;

/// <summary>Applies the common byte, format and actual decode gate before any storage call.</summary>
public sealed class ImageUploadPolicy
{
    public const long MaximumFileBytes = 8L * 1024 * 1024;
    public const long MaximumRequestBytes = MaximumFileBytes + (2L * 1024 * 1024);
    public const int MaximumDimension = PostContentRules.MaximumThumbnailDimension;

    private readonly IImageDecoder _decoder;

    public ImageUploadPolicy(IImageDecoder decoder) => _decoder = decoder;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly IReadOnlyDictionary<string, ImageFormatDefinition> FormatsByExtension =
        new Dictionary<string, ImageFormatDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ImageFormatDefinition.Jpeg,
            [".jpeg"] = ImageFormatDefinition.Jpeg,
            [".png"] = ImageFormatDefinition.Png,
            [".gif"] = ImageFormatDefinition.Gif,
            [".webp"] = ImageFormatDefinition.WebP
        };

    public static IReadOnlyList<string> ProviderFormats { get; } =
        Array.AsReadOnly(["jpg", "jpeg", "png", "gif", "webp"]);

    private static readonly HashSet<string> AllowedProviderFormats =
        new(ProviderFormats, StringComparer.OrdinalIgnoreCase);

    public async Task<ImageValidationOutcome> ValidateAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return ImageValidationOutcome.Failure(
                ImageUploadFailureKind.MissingFile,
                "No image file provided.");
        }

        if (file.Length > MaximumFileBytes)
        {
            return ImageValidationOutcome.Failure(
                ImageUploadFailureKind.FileTooLarge,
                "The image exceeds the 8 MB upload limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !FormatsByExtension.TryGetValue(extension, out var format) ||
            !format.ContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return InvalidFormat();
        }

        byte[] content;
        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + count > MaximumFileBytes)
                    return ImageValidationOutcome.Failure(ImageUploadFailureKind.FileTooLarge,
                        "The image exceeds the 8 MB upload limit.");
                buffer.Write(chunk, 0, count);
            }
            content = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return InvalidFormat();
        }
        catch (InvalidOperationException)
        {
            return InvalidFormat();
        }
        catch (NotSupportedException)
        {
            return InvalidFormat();
        }

        if (content.Length != file.Length || !format.Matches(content)) return InvalidFormat();
        var decoded = await _decoder.ValidateAsync(content, format.ProviderFormat, cancellationToken);
        return decoded switch
        {
            ImageDecodeResult.Valid => ImageValidationOutcome.Success(format.ProviderFormat, content),
            ImageDecodeResult.Unavailable => ImageValidationOutcome.Failure(ImageUploadFailureKind.StorageUnavailable,
                "Image validation is temporarily unavailable. Try again."),
            _ => InvalidFormat()
        };
    }

    public bool IsAllowedProviderFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && AllowedProviderFormats.Contains(format);

    private static ImageValidationOutcome InvalidFormat() =>
        ImageValidationOutcome.Failure(
            ImageUploadFailureKind.InvalidFormat,
            "Invalid image. Upload a valid JPG, PNG, GIF, or WEBP file.");

    private sealed record ImageFormatDefinition(
        ImageFormatKind Kind,
        string ProviderFormat,
        string[] ContentTypes)
    {
        public static readonly ImageFormatDefinition Jpeg = new(
            ImageFormatKind.Jpeg,
            "jpg",
            ["image/jpeg", "image/pjpeg"]);

        public static readonly ImageFormatDefinition Png = new(
            ImageFormatKind.Png,
            "png",
            ["image/png"]);

        public static readonly ImageFormatDefinition Gif = new(
            ImageFormatKind.Gif,
            "gif",
            ["image/gif"]);

        public static readonly ImageFormatDefinition WebP = new(
            ImageFormatKind.WebP,
            "webp",
            ["image/webp"]);

        public bool Matches(ReadOnlySpan<byte> bytes) => Kind switch
        {
            ImageFormatKind.Jpeg => bytes.Length >= 3 &&
                bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ImageFormatKind.Png => bytes.StartsWith(PngSignature),
            ImageFormatKind.Gif =>
                bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            ImageFormatKind.WebP => bytes.Length >= 12 &&
                bytes[..4].SequenceEqual("RIFF"u8) &&
                bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }

    private enum ImageFormatKind
    {
        Jpeg,
        Png,
        Gif,
        WebP
    }
}

/// <summary>Returns only a safe failure or the exact validated upload snapshot.</summary>
public sealed record ImageValidationOutcome(
    bool IsValid,
    string? ProviderFormat,
    ImageUploadFailureKind FailureKind,
    string Message,
    ValidatedImageContent? Content = null)
{
    public static ImageValidationOutcome Success(string providerFormat, byte[] content) =>
        new(true, providerFormat, ImageUploadFailureKind.None, string.Empty, new ValidatedImageContent(content));

    public static ImageValidationOutcome Failure(
        ImageUploadFailureKind failureKind,
        string message) =>
        new(false, null, failureKind, message);
}

/// <summary>Keeps the decoded snapshot immutable so storage receives the exact bytes that passed validation.</summary>
public sealed class ValidatedImageContent
{
    private readonly byte[] _bytes;
    internal ValidatedImageContent(byte[] bytes) => _bytes = bytes;
    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);
}
