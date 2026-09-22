namespace DevCoreBlog.Configuration;

/// <summary>
/// Holds the single administrator credentials loaded and validated at startup.
/// </summary>
public sealed class AdminCredentialsOptions
{
    public string? Username { get; set; }

    public string? PasswordHash { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(PasswordHash);
}
