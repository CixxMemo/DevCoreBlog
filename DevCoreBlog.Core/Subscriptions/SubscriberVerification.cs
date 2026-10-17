using System.Security.Cryptography;
using System.Text;
using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Core.Subscriptions;

public enum VerificationStageOutcome { Queued, AlreadyPending, Ineligible }
public enum VerificationConsumeOutcome { Confirmed, Invalid }

/// <summary>Purpose separation and address binding do not grant a reader session or admin privileges.</summary>
public static class SubscriberVerificationRules
{
    public const int Purpose = 1;
    public const int LifetimeSeconds = 86_400;
    public const int TokenBytes = 32;
    public const int TokenLength = 43;
    public static string Digest(ReadOnlySpan<byte> bytes, int purpose = Purpose) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AetherNotes.Token.v1/" + purpose.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/")
            .Concat(bytes.ToArray()).ToArray()));
    public static string AddressBinding(string addressKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(addressKey)));
}

/// <summary>Only digests and captured lifecycle facts persist; plaintext token exists only in the encrypted mail.</summary>
public sealed class SubscriberVerificationToken
{
    public Guid Id { get; private set; }
    public Guid SubscriberId { get; private set; }
    public long SubscriberVersion { get; private set; }
    public int Purpose { get; private set; }
    public string? Digest { get; private set; }
    public DateTime? RedactedUtc { get; private set; }
    public string AddressBinding { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
}

/// <summary>Atomic token and outbox staging value; contains no raw token.</summary>
public sealed class SubscriberVerificationDraft
{
    public Guid Id { get; }
    public Guid SubscriberId { get; }
    public long SubscriberVersion { get; }
    public string Digest { get; }
    public string AddressBinding { get; }
    public DateTime CreatedUtc { get; }
    public DateTime ExpiresUtc { get; }
    public SubscriberVerificationDraft(Guid id, Guid subscriberId, long version, string digest, string addressBinding, DateTime utcNow)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        if (id == Guid.Empty || subscriberId == Guid.Empty || version < 1 || !Hash(digest) || !Hash(addressBinding))
            throw new ArgumentException("Invalid verification draft.");
        Id = id; SubscriberId = subscriberId; SubscriberVersion = version; Digest = digest; AddressBinding = addressBinding;
        CreatedUtc = utcNow; ExpiresUtc = utcNow.AddSeconds(SubscriberVerificationRules.LifetimeSeconds);
    }
    private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>Staging enlists in a caller transaction when present; consumption atomically preserves single use.</summary>
public interface ISubscriberVerificationStore
{
    Task<VerificationStageOutcome> StageAsync(SubscriberVerificationDraft token, PreparedTransactionalEmail email, CancellationToken cancellationToken);
    Task<VerificationConsumeOutcome> ConsumeAsync(string digest, int purpose, DateTime utcNow, CancellationToken cancellationToken);
}
