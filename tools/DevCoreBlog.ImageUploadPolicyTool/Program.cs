using DevCoreBlog.Services;
using DevCoreBlog.Services.Images;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

var policy = new ImageUploadPolicy();
var pngBytes = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
var gifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

var validPng = CreateFile(pngBytes, "valid.png", "image/png");
var validGif = CreateFile(gifBytes, "valid.gif", "image/gif");
var oversizedBytes = new byte[ImageUploadPolicy.MaximumFileBytes + 1];
pngBytes.AsSpan(0, 12).CopyTo(oversizedBytes);

var missing = await policy.ValidateAsync(null);
var empty = await policy.ValidateAsync(CreateFile([], "empty.png", "image/png"));
var oversized = await policy.ValidateAsync(
    CreateFile(oversizedBytes, "oversized.png", "image/png"));
var fakeExtension = await policy.ValidateAsync(
    CreateFile(pngBytes, "fake.jpg", "image/jpeg"));
var svg = await policy.ValidateAsync(
    CreateFile("<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray(), "vector.svg", "image/svg+xml"));
var html = await policy.ValidateAsync(
    CreateFile("<html>not an image</html>"u8.ToArray(), "page.jpg", "image/jpeg"));
var corrupt = await policy.ValidateAsync(
    CreateFile("not-a-png"u8.ToArray(), "corrupt.png", "image/png"));
var wrongMime = await policy.ValidateAsync(
    CreateFile(pngBytes, "wrong-mime.png", "image/svg+xml"));
var png = await policy.ValidateAsync(validPng);
var gif = await policy.ValidateAsync(validGif);

var request = new CloudinaryImageUploadRequestFactory()
    .Create(new MemoryStream(pngBytes, writable: false), "png");
var transformation = request.Transformation?.ToString() ?? string.Empty;

var successfulStorage = new StubImageStorage(
    ImageStorageOutcome.Success(
        "https://res.cloudinary.com/test/image/upload/f09.png",
        "png",
        1,
        1));
var successfulService = new ImageService(
    policy,
    successfulStorage,
    NullLogger<ImageService>.Instance);
var successfulUpload = await successfulService.UploadImageAsync(validPng);

var failedStorage = new StubImageStorage(ImageStorageOutcome.Failure());
var failedService = new ImageService(
    policy,
    failedStorage,
    NullLogger<ImageService>.Instance);
var failedUpload = await failedService.UploadImageAsync(validPng);

var oversizedStorageResult = new StubImageStorage(
    ImageStorageOutcome.Success(
        "https://res.cloudinary.com/test/image/upload/f09.png",
        "png",
        ImageUploadPolicy.MaximumDimension + 1,
        1));
var guardedService = new ImageService(
    policy,
    oversizedStorageResult,
    NullLogger<ImageService>.Instance);
var guardedUpload = await guardedService.UploadImageAsync(validPng);

var checks = new Dictionary<string, bool>
{
    ["missing_file_is_rejected"] =
        !missing.IsValid && missing.FailureKind == ImageUploadFailureKind.MissingFile,
    ["empty_file_is_rejected"] =
        !empty.IsValid && empty.FailureKind == ImageUploadFailureKind.MissingFile,
    ["oversized_file_is_rejected"] =
        !oversized.IsValid && oversized.FailureKind == ImageUploadFailureKind.FileTooLarge,
    ["fake_extension_is_rejected"] =
        !fakeExtension.IsValid && fakeExtension.FailureKind == ImageUploadFailureKind.InvalidFormat,
    ["svg_is_rejected"] =
        !svg.IsValid && svg.FailureKind == ImageUploadFailureKind.InvalidFormat,
    ["html_is_rejected"] =
        !html.IsValid && html.FailureKind == ImageUploadFailureKind.InvalidFormat,
    ["corrupt_file_is_rejected"] =
        !corrupt.IsValid && corrupt.FailureKind == ImageUploadFailureKind.InvalidFormat,
    ["mime_mismatch_is_rejected"] =
        !wrongMime.IsValid && wrongMime.FailureKind == ImageUploadFailureKind.InvalidFormat,
    ["valid_png_and_gif_are_accepted"] =
        png.IsValid && png.ProviderFormat == "png" &&
        gif.IsValid && gif.ProviderFormat == "gif",
    ["provider_request_has_allowlist_and_dimension_limit"] =
        request.AllowedFormats.SequenceEqual(["jpg", "jpeg", "png", "gif", "webp"]) &&
        transformation.Contains("c_limit", StringComparison.Ordinal) &&
        transformation.Contains("h_4096", StringComparison.Ordinal) &&
        transformation.Contains("w_4096", StringComparison.Ordinal),
    ["provider_request_uses_generated_storage_name"] =
        request.UseFilename == false &&
        request.UniqueFilename == true &&
        request.Overwrite == false &&
        request.DiscardOriginalFilename == true &&
        request.File.FileName.EndsWith(".png", StringComparison.Ordinal) &&
        request.File.FileName.Length == 36 &&
        request.Folder == "DevCoreBlog",
    ["valid_file_reaches_storage_and_returns_safe_url"] =
        successfulStorage.CallCount == 1 &&
        successfulUpload.Succeeded &&
        successfulUpload.Url?.StartsWith("https://", StringComparison.Ordinal) == true,
    ["storage_failure_is_safe_and_explicit"] =
        failedStorage.CallCount == 1 &&
        !failedUpload.Succeeded &&
        failedUpload.FailureKind == ImageUploadFailureKind.StorageUnavailable &&
        failedUpload.Message == "Image storage is temporarily unavailable. Try again.",
    ["unsafe_storage_metadata_is_rejected"] =
        oversizedStorageResult.CallCount == 1 &&
        !guardedUpload.Succeeded &&
        guardedUpload.FailureKind == ImageUploadFailureKind.StorageUnavailable
};

foreach (var check in checks)
{
    Console.WriteLine($"{check.Key}={check.Value.ToString().ToLowerInvariant()}");
}

return checks.Values.All(passed => passed) ? 0 : 1;

static FormFile CreateFile(byte[] content, string fileName, string contentType)
{
    var stream = new MemoryStream(content, writable: false);
    return new FormFile(stream, 0, content.Length, "file", fileName)
    {
        Headers = new HeaderDictionary(),
        ContentType = contentType
    };
}

internal sealed class StubImageStorage : IImageStorage
{
    private readonly ImageStorageOutcome _outcome;

    public StubImageStorage(ImageStorageOutcome outcome)
    {
        _outcome = outcome;
    }

    public int CallCount { get; private set; }

    public Task<ImageStorageOutcome> UploadAsync(
        Stream stream,
        string providerFormat,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_outcome);
    }
}
