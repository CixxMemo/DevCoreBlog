using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using DevCoreBlog.Services.Security;

internal static class PasswordHashDiagnostics
{
    public static int RunSelfTest(Pbkdf2PasswordHasher hasher)
    {
        const string password = "F07 synthetic password for isolated verification";
        var firstHash = hasher.HashPassword(password);
        var secondHash = hasher.HashPassword(password);

        var firstSegments = firstHash.Split(':');
        var belowMinimum = string.Join(
            ':',
            firstSegments[0],
            Pbkdf2PasswordHasher.MinimumIterations - 1,
            firstSegments[2],
            firstSegments[3]);
        var aboveMaximum = string.Join(
            ':',
            firstSegments[0],
            Pbkdf2PasswordHasher.MaximumIterations + 1,
            firstSegments[2],
            firstSegments[3]);

        var checks = new Dictionary<string, bool>
        {
            ["same_password_uses_unique_salts"] = firstHash != secondHash,
            ["correct_password_is_accepted"] = hasher.VerifyPassword(password, firstHash),
            ["wrong_password_is_rejected"] = !hasher.VerifyPassword("wrong password", firstHash),
            ["empty_password_is_rejected"] = !hasher.VerifyPassword(string.Empty, firstHash),
            ["whitespace_password_is_rejected"] = !hasher.VerifyPassword("   ", firstHash),
            ["malformed_hash_is_rejected"] = !hasher.VerifyPassword(password, "not-a-password-hash"),
            ["low_cost_hash_is_rejected"] = !hasher.VerifyPassword(password, belowMinimum),
            ["excessive_cost_hash_is_rejected"] = !hasher.VerifyPassword(password, aboveMaximum),
            ["encoded_hash_format_is_valid"] = Pbkdf2PasswordHasher.IsValidEncodedHash(firstHash)
        };

        foreach (var check in checks)
        {
            Console.Out.WriteLine($"{check.Key}={check.Value.ToString().ToLowerInvariant()}");
        }

        return checks.Values.All(static passed => passed) ? 0 : 1;
    }

    public static int RunBenchmark(Pbkdf2PasswordHasher hasher)
    {
        var syntheticPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var stopwatch = Stopwatch.StartNew();
        var encodedHash = hasher.HashPassword(syntheticPassword);
        stopwatch.Stop();
        var hashMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        var verified = hasher.VerifyPassword(syntheticPassword, encodedHash);
        stopwatch.Stop();

        Console.Out.WriteLine(
            $"pbkdf2_sha256_iterations={Pbkdf2PasswordHasher.RecommendedIterations}");
        Console.Out.WriteLine(
            $"hash_milliseconds={hashMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine(
            $"verify_milliseconds={stopwatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"verification_passed={verified.ToString().ToLowerInvariant()}");
        return verified ? 0 : 1;
    }
}
