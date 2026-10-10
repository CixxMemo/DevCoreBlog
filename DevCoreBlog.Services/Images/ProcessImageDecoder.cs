using System.Diagnostics;
using System.Reflection;

namespace DevCoreBlog.Services.Images;

/// <summary>Contains native decoding in at most two short-lived children; cancellation kills actual work.</summary>
public sealed class ProcessImageDecoder : IImageDecoder, IDisposable
{
    public const int MaximumConcurrentDecodes = 2;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(10);
    public const long MaximumObservedResidentBytes = 256L * 1024 * 1024;
    private readonly SemaphoreSlim _slots = new(MaximumConcurrentDecodes);

    public async Task<ImageDecodeResult> ValidateAsync(ReadOnlyMemory<byte> content, string format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length == 0 || content.Length > ImageUploadPolicy.MaximumFileBytes)
            return ImageDecodeResult.Invalid;
        // Do not accumulate waiting uploads or free a slot while native work is still running.
        if (!await _slots.WaitAsync(0, cancellationToken)) return ImageDecodeResult.Unavailable;
        try { return await RunAsync(content, format, cancellationToken); }
        finally { _slots.Release(); }
    }

    private static async Task<ImageDecodeResult> RunAsync(ReadOnlyMemory<byte> content, string format,
        CancellationToken cancellationToken)
    {
        var host = Environment.ProcessPath;
        var entry = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(entry)) return ImageDecodeResult.Unavailable;
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(entry);
        start.ArgumentList.Add(ImageDecodeWorker.Argument);
        start.ArgumentList.Add(format);
        // The worker must never inherit database, provider, authentication or dotenv secrets.
        start.Environment.Clear();
        start.Environment["DOTNET_GCHeapHardLimit"] = "4000000"; // 64 MiB, hexadecimal runtime setting.
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
        start.Environment["DOTNET_gcServer"] = "0";
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } runtimeRoot)
            start.Environment["DOTNET_ROOT"] = runtimeRoot;
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(MaximumDuration);
        Task monitor = Task.CompletedTask;
        Task discardOutput = Task.CompletedTask;
        try
        {
            if (!process.Start()) return ImageDecodeResult.Unavailable;
            monitor = MonitorAsync(process, deadline);
            discardOutput = Task.WhenAll(process.StandardError.BaseStream.CopyToAsync(Stream.Null),
                process.StandardOutput.BaseStream.CopyToAsync(Stream.Null));
            await process.StandardInput.BaseStream.WriteAsync(content, deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            if (deadline.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImageDecodeResult.Unavailable;
            }
            deadline.Cancel();
            await monitor;
            await discardOutput;
            // No native message or input is returned to HTTP or logs.
            return process.ExitCode switch
            {
                0 => ImageDecodeResult.Valid,
                2 => ImageDecodeResult.Invalid,
                _ => ImageDecodeResult.Unavailable
            };
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            cancellationToken.ThrowIfCancellationRequested();
            return ImageDecodeResult.Unavailable;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await StopAsync(process);
            return ImageDecodeResult.Unavailable;
        }
        finally
        {
            deadline.Cancel();
            await StopAsync(process);
            await monitor;
            await discardOutput;
        }
    }

    private static async Task MonitorAsync(Process process, CancellationTokenSource deadline)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(deadline.Token))
            {
                process.Refresh();
                if (process.HasExited) return;
                if (process.WorkingSet64 > MaximumObservedResidentBytes)
                {
                    deadline.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        catch (InvalidOperationException) { /* Child exited during resource sampling. */ }
        catch (System.ComponentModel.Win32Exception) { deadline.Cancel(); }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException) { /* Not started, or already exited during cleanup. */ }
    }

    public void Dispose() => _slots.Dispose();
}
