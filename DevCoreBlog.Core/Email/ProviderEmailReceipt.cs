namespace DevCoreBlog.Core.Email;

/// <summary>Permanent replay tombstone after detailed provider facts are reduced; contains no address or body.</summary>
public sealed class ProviderEmailReceipt
{
    public string EventId { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTime ReceivedUtc { get; private set; }
}
