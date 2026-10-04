namespace DevCoreBlog.Services.Images;

/// <summary>Fails uploads closed when media credentials are incomplete, while admin diagnostics remain available.</summary>
public sealed class UnavailableImageStorage : IImageStorage
{
    public Task<ImageStorageOutcome> UploadAsync(Stream stream, string providerFormat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ImageStorageOutcome.Failure());
    }
}
