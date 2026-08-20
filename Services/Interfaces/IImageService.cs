using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Services.Interfaces;

public interface IImageService
{
    Task<string> UploadImageAsync(IFormFile file);
}
