namespace DevCoreBlog.Services.Images;

public interface IImageStorage
{
    Task<ImageStorageOutcome> UploadAsync(
        Stream stream,
        string providerFormat,
        CancellationToken cancellationToken = default);
}

public sealed record ImageStorageOutcome(
    bool Succeeded,
    string? Url,
    string? Format,
    int Width,
    int Height,
    string? PublicId)
{
    public static ImageStorageOutcome Success(
        string url,
        string format,
        int width,
        int height,
        string publicId) =>
        new(true, url, format, width, height, publicId);

    public static ImageStorageOutcome Failure() => new(false, null, null, 0, 0, null);
}
