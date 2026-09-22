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
    string Message)
{
    public static ImageUploadOutcome Success(string url) =>
        new(true, url, ImageUploadFailureKind.None, string.Empty);

    public static ImageUploadOutcome Failure(
        ImageUploadFailureKind failureKind,
        string message) =>
        new(false, null, failureKind, message);
}
