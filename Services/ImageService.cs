using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace DevCoreBlog.Services;

public class ImageService : IImageService
{
    private readonly Cloudinary _cloudinary;

    public ImageService()
    {
        // Environment.GetEnvironmentVariable ile .env dosyasındaki (veya sistemdeki) değişkenleri okuyoruz.
        var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME");
        var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY");
        var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET");

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new InvalidOperationException("Cloudinary kimlik bilgileri ortam değişkenlerinde (Environment Variables) bulunamadı.");
        }

        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    public async Task<string> UploadImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return string.Empty;

        using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = "DevCoreBlog" // Opsiyonel: Cloudinary'de düzenli durması için klasör adı
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
        {
            throw new Exception($"Görsel yüklenirken Cloudinary hatası: {uploadResult.Error.Message}");
        }

        return uploadResult.SecureUrl.ToString();
    }
}
