using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevCoreBlog.Services.Interfaces;

namespace DevCoreBlog.Services.Security;

/// <summary>
/// Creates and verifies bounded, versioned PBKDF2-HMAC-SHA256 password hashes.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IAdminPasswordVerifier
{
    public const string FormatPrefix = "dcb-pbkdf2-sha256-v1";
    public const int RecommendedIterations = 600_000;
    public const int MinimumIterations = 600_000;
    public const int MaximumIterations = 2_000_000;
    public const int MaximumPasswordLength = 1_024;

    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int MaximumEncodedHashLength = 256;

    public string HashPassword(string password) =>
        HashPassword(password, RecommendedIterations);

    public string HashPassword(string password, int iterations)
    {
        ValidatePassword(password);
        ValidateIterationCount(iterations);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derivedHash = null;

        try
        {
            derivedHash = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashLength);

            return string.Join(
                ':',
                FormatPrefix,
                iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(derivedHash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (derivedHash is not null)
            {
                CryptographicOperations.ZeroMemory(derivedHash);
            }
        }
    }

    public bool VerifyPassword(string? password, string? encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length > MaximumPasswordLength ||
            !TryParse(encodedHash, out var parsedHash))
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? candidateHash = null;

        try
        {
            candidateHash = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                parsedHash.Salt,
                parsedHash.Iterations,
                HashAlgorithmName.SHA256,
                HashLength);

            return CryptographicOperations.FixedTimeEquals(
                candidateHash,
                parsedHash.ExpectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (candidateHash is not null)
            {
                CryptographicOperations.ZeroMemory(candidateHash);
            }
        }
    }

    public static bool IsValidEncodedHash(string? encodedHash) =>
        TryParse(encodedHash, out _);

    private static bool TryParse(
        string? encodedHash,
        out ParsedPasswordHash parsedHash)
    {
        parsedHash = default;
        if (string.IsNullOrWhiteSpace(encodedHash) ||
            encodedHash.Length > MaximumEncodedHashLength)
        {
            return false;
        }

        var segments = encodedHash.Split(':');
        if (segments.Length != 4 ||
            !string.Equals(segments[0], FormatPrefix, StringComparison.Ordinal) ||
            !int.TryParse(
                segments[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var iterations) ||
            iterations is < MinimumIterations or > MaximumIterations ||
            segments[2].Length > 64 ||
            segments[3].Length > 128)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(segments[2]);
            var expectedHash = Convert.FromBase64String(segments[3]);
            if (salt.Length != SaltLength || expectedHash.Length != HashLength)
            {
                return false;
            }

            parsedHash = new ParsedPasswordHash(iterations, salt, expectedHash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException(
                "Password cannot be empty or whitespace.",
                nameof(password));
        }

        if (password.Length > MaximumPasswordLength)
        {
            throw new ArgumentException(
                $"Password cannot exceed {MaximumPasswordLength} characters.",
                nameof(password));
        }
    }

    private static void ValidateIterationCount(int iterations)
    {
        if (iterations is < MinimumIterations or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"Iteration count must be between {MinimumIterations} and {MaximumIterations}.");
        }
    }

    private readonly record struct ParsedPasswordHash(
        int Iterations,
        byte[] Salt,
        byte[] ExpectedHash);
}
