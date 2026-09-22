using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace DevCoreBlog.Services.Images;

public sealed class CloudinaryImageUploadRequestFactory
{
    public ImageUploadParams Create(Stream stream, string providerFormat)
    {
        var generatedFileName = $"{Guid.NewGuid():N}.{providerFormat}";
        return new ImageUploadParams
        {
            File = new FileDescription(generatedFileName, stream),
            Folder = "DevCoreBlog",
            AllowedFormats = [.. ImageUploadPolicy.ProviderFormats],
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false,
            DiscardOriginalFilename = true,
            Transformation = new Transformation()
                .Width(ImageUploadPolicy.MaximumDimension)
                .Height(ImageUploadPolicy.MaximumDimension)
                .Crop("limit")
        };
    }
}
