namespace DevCoreBlog.Core.Email;

/// <summary>Non-secret sender facts only; campaign snapshots must never persist an API key or credential binding.</summary>
public interface IEmailSenderProfile
{
    EmailSenderIdentity? Identity { get; }
    string Contract { get; }
}

public sealed record EmailSenderIdentity(string Email, string Name, string? ReplyTo)
{
    public override string ToString() => nameof(EmailSenderIdentity);
}
