namespace DevCoreBlog.Services.Images;

/// <summary>Checks bounded encoded bytes without storing or rewriting the uploaded image.</summary>
public interface IImageDecoder
{
    Task<ImageDecodeResult> ValidateAsync(ReadOnlyMemory<byte> content, string format,
        CancellationToken cancellationToken = default);
}

public enum ImageDecodeResult { Valid, Invalid, Unavailable }
