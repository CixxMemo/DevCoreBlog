namespace DevCoreBlog.Services.Images;

public enum ImageUploadFailureKind
{
    None,
    MissingFile,
    FileTooLarge,
    InvalidFormat,
    StorageUnavailable
}

public sealed record ImageUploadOutcome(
    bool Succeeded,
    string? Url,
    ImageUploadFailureKind FailureKind,
    string Message,
    string? PublicId = null,
    int? Width = null,
    int? Height = null)
{
    public static ImageUploadOutcome Success(string url, string publicId, int width, int height) =>
        new(true, url, ImageUploadFailureKind.None, string.Empty, publicId, width, height);

    public static ImageUploadOutcome Failure(
        ImageUploadFailureKind failureKind,
        string message) =>
        new(false, null, failureKind, message);
}
