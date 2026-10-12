using System.Text.Json;

/// <summary>Private operator artifacts are exclusive files; input size and Unicode/schema remain bounded.</summary>
internal static class PrivateFiles
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    internal static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        await using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        await JsonSerializer.SerializeAsync(stream, value, Options, token);
    }
    internal static async Task<T> ReadAsync<T>(string path, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Length > 16_777_216) throw new InvalidOperationException();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, token) ?? throw new InvalidOperationException();
    }
}
