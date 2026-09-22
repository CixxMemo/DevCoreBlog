namespace DevCoreBlog.Services.Interfaces;

/// <summary>
/// Verifies a submitted administrator password against a versioned encoded hash.
/// </summary>
public interface IAdminPasswordVerifier
{
    bool VerifyPassword(string? password, string? encodedHash);
}
