using System.Diagnostics;
using System.Security.Cryptography;

/// <summary>Exports a new private archive; this operator command never restores over an existing database.</summary>
internal static class TransitionArchive
{
    internal static async Task<object> BackupAsync(string connection, string path, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var s = TransitionDatabase.Settings(connection);
        if (s.Host is not ("localhost" or "127.0.0.1" or "::1") || File.Exists(path)) throw new InvalidOperationException();
        var start = new ProcessStartInfo("pg_dump") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "--format=custom", "--no-owner", "--no-acl", "--file", path }) start.ArgumentList.Add(argument);
        start.Environment["PGHOST"] = s.Host; start.Environment["PGPORT"] = s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = s.Database; start.Environment["PGUSER"] = s.Username;
        start.Environment["PGPASSWORD"] = s.Password; start.Environment["PGPASSFILE"] = "/dev/null"; start.Environment["PGCONNECT_TIMEOUT"] = "5";
        // Reserve the path at owner-only permissions before pg_dump truncates it.
        await using (var reserved = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite })) { }
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); await Task.WhenAll(output, error); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException();
        await using var archive = File.OpenRead(path); var sha = Convert.ToHexString(await SHA256.HashDataAsync(archive, token));
        return new { status = "backed_up", archiveSha256 = sha, targetFingerprint = TransitionDatabase.Fingerprint(s), privateFile = true };
    }
}
