namespace DevCoreBlog.Core.Email;

/// <summary>
/// Newsletter delivery, new subscriptions and email sign-in were shelved by the 23 October 2026 product decision.
/// This is deliberately code rather than configuration, so no environment value can reopen these paths.
/// Reopening requires a new product decision and a code change. Stored records and exit links stay intact.
/// </summary>
public static class EmailPaths
{
    public static bool Suspended => true;
}
