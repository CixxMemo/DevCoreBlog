using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace DevCoreBlog.Services.Images;

/// <summary>Runs before host/configuration startup, decoding every frame from stdin with no file or URL inputs.</summary>
public static class ImageDecodeWorker
{
    public const string Argument = "--image-decode-worker";
    public const int MaximumFrames = 100;
    public const long MaximumDecodedPixels = 100_000_000;
    public const ulong MaximumDataBytes = 512UL * 1024 * 1024;

    public static async Task<int> RunAsync(string format)
    {
        try
        {
            if (!ApplyResourceLimits()) return 3;
            using var input = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int count;
            while ((count = await input.ReadAsync(chunk)) != 0)
            {
                if (buffer.Length + count > ImageUploadPolicy.MaximumFileBytes) return 2;
                buffer.Write(chunk, 0, count);
            }
            return Validate(buffer.ToArray(), format) ? 0 : 2;
        }
        catch { return 3; } // Child crashes/native failures never authorize an upload or disclose diagnostics.
    }

    private static bool Validate(byte[] bytes, string format)
    {
        if (!HasCompleteContainer(bytes, format)) return false;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var expected = format switch
        {
            "jpg" => SKEncodedImageFormat.Jpeg, "png" => SKEncodedImageFormat.Png,
            "gif" => SKEncodedImageFormat.Gif, "webp" => SKEncodedImageFormat.Webp,
            _ => (SKEncodedImageFormat)(-1)
        };
        if (codec is null || codec.EncodedFormat != expected) return false;
        var source = codec.Info;
        if (source.Width is <= 0 or > ImageUploadPolicy.MaximumDimension ||
            source.Height is <= 0 or > ImageUploadPolicy.MaximumDimension) return false;
        var frames = Math.Max(1, codec.FrameCount);
        var canvasPixels = (long)source.Width * source.Height;
        if (frames > MaximumFrames || canvasPixels * frames > MaximumDecodedPixels) return false;
        var frameInfo = codec.FrameInfo;
        var output = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(output); // One reusable RGBA canvas, at most 64 MiB.
        long work = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            // Restore-previous frames cannot serve as a prior frame. Bound any dependency redecoding too.
            var prior = frame > 0 && frameInfo[frame - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                ? frame - 1 : -1;
            work += canvasPixels * (prior < 0 ? frame + 1L : 1L);
            if (work > MaximumDecodedPixels) return false;
            if (codec.GetPixels(output, bitmap.GetPixels(), new SKCodecOptions(frame, prior)) != SKCodecResult.Success)
                return false;
        }
        return true;
    }

    private static bool HasCompleteContainer(ReadOnlySpan<byte> bytes, string format) => format switch
    {
        "jpg" => bytes.Length >= 4 && bytes[^2..].SequenceEqual(new byte[] { 0xff, 0xd9 }),
        "png" => bytes.Length >= 12 && bytes[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }),
        "gif" => bytes.Length >= 14 && bytes[^1] == 0x3b,
        "webp" => bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) == bytes.Length - 8,
        _ => false
    };

    private static bool ApplyResourceLimits()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return false;
        // CPU/data/core resource IDs are shared by Darwin and Linux. Hard limits live only in this child.
        return SetResourceLimit(0, new ResourceLimit(5, 5)) == 0 &&
            (!OperatingSystem.IsLinux() || SetResourceLimit(2, new ResourceLimit(MaximumDataBytes, MaximumDataBytes)) == 0) &&
            SetResourceLimit(4, new ResourceLimit(0, 0)) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ResourceLimit(ulong Current, ulong Maximum);

    [DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
    private static extern int SetResourceLimit(int resource, in ResourceLimit limit);
}
