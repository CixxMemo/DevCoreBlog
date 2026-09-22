using Microsoft.AspNetCore.Http;
using DevCoreBlog.Services.Images;

namespace DevCoreBlog.Services.Interfaces;

public interface IImageService
{
    Task<ImageUploadOutcome> UploadImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default);
}
