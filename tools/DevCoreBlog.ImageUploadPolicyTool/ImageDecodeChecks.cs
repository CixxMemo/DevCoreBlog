using System.Buffers.Binary;
using System.Diagnostics;
using DevCoreBlog.Services;
using DevCoreBlog.Services.Images;
using DevCoreBlog.Services.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

/// <summary>Uses actual native codecs and owned child processes; storage remains a network-free stub.</summary>
internal static class ImageDecodeChecks
{
    public static async Task<Dictionary<string, bool>> RunAsync(ImageUploadPolicy policy, ProcessImageDecoder decoder)
    {
        var checks = new Dictionary<string, bool>();
        foreach (var (extension, mime, encoded) in new[]
        {
            ("jpg", "image/jpeg", Encode(SKEncodedImageFormat.Jpeg)),
            ("png", "image/png", Encode(SKEncodedImageFormat.Png)),
            ("webp", "image/webp", Encode(SKEncodedImageFormat.Webp))
        })
        {
            checks["native_" + extension + "_full_decode"] = (await policy.ValidateAsync(File(encoded, extension, mime))).IsValid;
            checks["native_" + extension + "_truncated_body_rejected"] = !(await policy.ValidateAsync(File(encoded[..(encoded.Length / 2)], extension, mime))).IsValid;
            var forged = encoded[..(encoded.Length / 2)].ToList();
            if (extension == "jpg") forged.AddRange(new byte[] { 0xff, 0xd9 });
            if (extension == "png") forged.AddRange(encoded[^12..]);
            var cutWithEnding = forged.ToArray();
            if (extension == "webp") BinaryPrimitives.WriteUInt32LittleEndian(cutWithEnding.AsSpan(4), (uint)cutWithEnding.Length - 8);
            checks["native_" + extension + "_forged_ending_does_not_bypass_decode"] = !(await policy.ValidateAsync(File(cutWithEnding, extension, mime))).IsValid;
        }
        checks["native_gif_100_frames_accepted"] = (await policy.ValidateAsync(File(Gif(100), "gif", "image/gif"))).IsValid;
        checks["native_gif_100_million_canvas_pixels_accepted"] = (await policy.ValidateAsync(File(Gif(100, 1000, 1000), "gif", "image/gif"))).IsValid;
        checks["native_gif_above_100_million_pixels_rejected"] = !(await policy.ValidateAsync(File(Gif(100, 1001, 1000), "gif", "image/gif"))).IsValid;
        checks["native_gif_restore_previous_frames_decoded"] = (await policy.ValidateAsync(File(Gif(5, restorePrevious: true), "gif", "image/gif"))).IsValid;
        checks["native_gif_101_frames_rejected"] = !(await policy.ValidateAsync(File(Gif(101), "gif", "image/gif"))).IsValid;
        checks["native_gif_canvas_4096_accepted"] = (await policy.ValidateAsync(File(Gif(1, 4096, 4096), "gif", "image/gif"))).IsValid;
        checks["native_gif_canvas_4097_rejected"] = !(await policy.ValidateAsync(File(Gif(1, 4097), "gif", "image/gif"))).IsValid;
        checks["native_png_4097_rejected"] = !(await policy.ValidateAsync(File(Encode(SKEncodedImageFormat.Png, 4097), "png", "image/png"))).IsValid;
        checks["native_gif_total_canvas_pixels_rejected"] = !(await policy.ValidateAsync(File(Gif(6, 4096, 4096), "gif", "image/gif"))).IsValid;
        var damaged = Gif(2);
        damaged[^3] = 0xff; // Invalid LZW code in the later frame; its first frame remains valid.
        checks["native_gif_later_frame_corruption_rejected"] = !(await policy.ValidateAsync(File(damaged, "gif", "image/gif"))).IsValid;
        var missingTrailer = Gif(2)[..^1];
        checks["native_gif_missing_trailer_rejected"] = !(await policy.ValidateAsync(File(missingTrailer, "gif", "image/gif"))).IsValid;
        var storage = new StubImageStorage(ImageStorageOutcome.Failure());
        var service = new ImageService(policy, storage, NullLogger<ImageService>.Instance, new MediaOperationStatus(true, TimeProvider.System));
        await service.UploadImageAsync(File(damaged, "gif", "image/gif"));
        await service.UploadImageAsync(File(Gif(101), "gif", "image/gif"));
        checks["invalid_native_input_never_reaches_storage"] = storage.CallCount == 0;
        var png = Encode(SKEncodedImageFormat.Png);
        var changing = new ChangingFile(png);
        var capture = new CaptureStorage();
        var immutableService = new ImageService(policy, capture, NullLogger<ImageService>.Instance, new MediaOperationStatus(true, TimeProvider.System));
        await immutableService.UploadImageAsync(changing);
        checks["storage_receives_exact_validated_snapshot"] = changing.Opens == 1 && capture.Bytes.SequenceEqual(png);
        var lying = new ChangingFile(new byte[ImageUploadPolicy.MaximumFileBytes + 1], declaredLength: 10);
        checks["actual_stream_size_is_bounded"] = (await policy.ValidateAsync(lying)).FailureKind == ImageUploadFailureKind.FileTooLarge;
        using var cancellation = new CancellationTokenSource();
        var first = decoder.ValidateAsync(new byte[] { 1 }, "f11-stall", cancellation.Token);
        var second = decoder.ValidateAsync(new byte[] { 1 }, "f11-stall", cancellation.Token);
        checks["two_workers_and_no_waiting_queue"] = await decoder.ValidateAsync(new byte[] { 1 }, "f11-stall") == ImageDecodeResult.Unavailable;
        var watch = Stopwatch.StartNew();
        cancellation.Cancel();
        checks["cancellation_reaps_actual_children"] = await WasCancelled(first) && await WasCancelled(second) && watch.Elapsed < TimeSpan.FromSeconds(3);
        checks["slots_released_after_cancellation"] = (await policy.ValidateAsync(File(png, "png", "image/png"))).IsValid;
        watch.Restart();
        checks["timeout_kills_stuck_worker"] = await decoder.ValidateAsync(new byte[] { 1 }, "f11-stall") == ImageDecodeResult.Unavailable &&
            watch.Elapsed >= TimeSpan.FromSeconds(9) && watch.Elapsed < TimeSpan.FromSeconds(15);
        watch.Restart();
        checks["resident_memory_monitor_kills_native_allocation"] = await decoder.ValidateAsync(new byte[] { 1 }, "f11-resident-memory") == ImageDecodeResult.Unavailable &&
            watch.Elapsed < TimeSpan.FromSeconds(5);
        checks["worker_failure_does_not_authorize_upload"] = await decoder.ValidateAsync(new byte[] { 1 }, "f11-failure") == ImageDecodeResult.Unavailable;
        var previous = Environment.GetEnvironmentVariable("F11_DECODE_PRIVATE_CANARY");
        try
        {
            Environment.SetEnvironmentVariable("F11_DECODE_PRIVATE_CANARY", "synthetic-private-canary");
            checks["child_has_no_secrets_and_bounded_runtime_heap"] = await decoder.ValidateAsync(new byte[] { 1 }, "f11-environment") == ImageDecodeResult.Valid;
        }
        finally { Environment.SetEnvironmentVariable("F11_DECODE_PRIVATE_CANARY", previous); }
        return checks;
    }

    private static async Task<bool> WasCancelled(Task<ImageDecodeResult> task)
    {
        try { await task; return false; }
        catch (OperationCanceledException) { return true; }
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width = 2)
    {
        using var bitmap = new SKBitmap(width, 2);
        bitmap.Erase(SKColors.Blue);
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] Gif(int frames, ushort width = 1, ushort height = 1, bool restorePrevious = false)
    {
        var original = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        using var stream = new MemoryStream();
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(8), height);
        stream.Write(original.AsSpan(0, 19));
        for (var i = 0; i < frames; i++)
        {
            if (restorePrevious) stream.Write(new byte[] { 0x21, 0xf9, 4, 12, 10, 0, 0, 0 });
            stream.Write(original.AsSpan(19, 14));
        }
        stream.WriteByte(0x3b);
        return stream.ToArray();
    }

    private static FormFile File(byte[] bytes, string extension, string mime) => new(new MemoryStream(bytes, false), 0, bytes.Length, "file", "fixture." + extension)
    { Headers = new HeaderDictionary(), ContentType = mime };

    private sealed class ChangingFile(byte[] bytes, long? declaredLength = null) : IFormFile
    {
        public int Opens { get; private set; }
        public string ContentType => "image/png";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length => declaredLength ?? bytes.Length;
        public string Name => "file";
        public string FileName => "fixture.png";
        public Stream OpenReadStream() => new MemoryStream(++Opens == 1 ? bytes : "not-an-image"u8.ToArray(), false);
        public void CopyTo(Stream target) => throw new InvalidOperationException("The policy must bound its own reads.");
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The policy must bound its own reads.");
    }

    private sealed class CaptureStorage : IImageStorage
    {
        public byte[] Bytes { get; private set; } = [];
        public async Task<ImageStorageOutcome> UploadAsync(Stream stream, string providerFormat, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            Bytes = buffer.ToArray();
            return ImageStorageOutcome.Success("https://images.example.test/fixture.png", "png", 2, 2, "fixture");
        }
    }
}
