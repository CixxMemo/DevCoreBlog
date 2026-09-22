namespace DevCoreBlog.Security;

public sealed class AdminSessionPolicy
{
    public const string ApplicationName = "DevCoreBlog";
    public const string CookieName = "DevCoreBlog.Admin";
    public const string VersionClaimType = "devcoreblog:admin-session-version";
    public const int DefaultLifetimeSeconds = 1800;
    public const int MaximumLifetimeSeconds = 28800;

    public AdminSessionPolicy(TimeSpan lifetime)
    {
        Lifetime = lifetime;
    }

    public TimeSpan Lifetime { get; }
}
