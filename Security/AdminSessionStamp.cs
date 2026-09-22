using System.Security.Cryptography;
using System.Text;

namespace DevCoreBlog.Security;

public sealed class AdminSessionStamp
{
    private const int StampLength = 32;
    private readonly byte[] _stamp;

    public AdminSessionStamp(
        string username,
        string encodedPasswordHash,
        string sessionVersion)
    {
        var material = Encoding.UTF8.GetBytes(
            $"{username.Length}:{username}:{encodedPasswordHash.Length}:{encodedPasswordHash}:{sessionVersion.Length}:{sessionVersion}");
        try
        {
            _stamp = SHA256.HashData(material);
            ClaimValue = Convert.ToHexString(_stamp);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    public string ClaimValue { get; }

    public bool Matches(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length != StampLength * 2)
        {
            return false;
        }

        byte[] candidateBytes;
        try
        {
            candidateBytes = Convert.FromHexString(candidate);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            return candidateBytes.Length == StampLength &&
                CryptographicOperations.FixedTimeEquals(_stamp, candidateBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateBytes);
        }
    }
}
