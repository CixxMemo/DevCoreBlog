namespace DevCoreBlog.Core.Subscriptions;

/// <summary>A bounded single ASCII mailbox; identity folds case but preserves dots and plus tags.</summary>
public sealed class SubscriberAddress
{
    private const string AtomCharacters = "!#$%&'*+-/=?^_`{|}~";
    public string Address { get; }
    public string Key { get; }

    private SubscriberAddress(string address)
    {
        Address = address;
        Key = address.ToLowerInvariant();
    }

    public static bool TryCreate(string? input, out SubscriberAddress? address)
    {
        address = null;
        // Reject whitespace rather than silently repairing headers or ambiguous mailboxes.
        if (input is null || input.Length is < 3 or > 254 || input.Any(c => c > 127 || char.IsWhiteSpace(c)))
            return false;
        var separator = input.IndexOf('@');
        if (separator is < 1 or > 64 || separator != input.LastIndexOf('@')) return false;
        var local = input[..separator];
        if (local[0] == '.' || local[^1] == '.' || local.Contains("..", StringComparison.Ordinal)
            || local.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && !AtomCharacters.Contains(c)))
            return false;
        var labels = input[(separator + 1)..].Split('.');
        if (labels.Length < 2 || labels.Any(label => label.Length is < 1 or > 63
            || label[0] == '-' || label[^1] == '-'
            || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            return false;
        address = new SubscriberAddress(input);
        return true;
    }
}
