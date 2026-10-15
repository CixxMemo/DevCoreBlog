namespace DevCoreBlog.Core.Subscriptions;

public enum SubscriberListStatus { All, Pending, Active, Unsubscribed }
/// <summary>Validated UTC range measures first address creation, never historical net active growth.</summary>
public sealed record SubscriberListQuery(int Page, SubscriberListStatus Status, DateTime FromUtc, DateTime UntilUtc, DateTime ObservedUtc)
{
    public const int PageSize = 25;
    public const int MaximumPage = 1000;
    public bool IsValid => Page is >= 1 and <= MaximumPage && Enum.IsDefined(Status)
        && FromUtc.Kind == DateTimeKind.Utc && UntilUtc.Kind == DateTimeKind.Utc && ObservedUtc.Kind == DateTimeKind.Utc
        && FromUtc >= new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc) && UntilUtc > FromUtc
        && UntilUtc - FromUtc <= TimeSpan.FromDays(368) && ObservedUtc >= FromUtc;
}
/// <summary>Only the private list receives addresses; printable diagnostics deliberately omit them.</summary>
public sealed record SubscriberListRow(string EmailAddress, SubscriptionStatus Status, DateTime CreatedUtc, bool Blocked, bool Eligible)
{
    public override string ToString() => nameof(SubscriberListRow);
}
public sealed record SubscriberCurrentTotals(long Total, long Pending, long Active, long Unsubscribed, long Blocked, long Eligible);
public sealed record SubscriberListPage(IReadOnlyList<SubscriberListRow> Items, long FilteredCount, SubscriberCurrentTotals Totals, long NewAddresses);
/// <summary>One bounded read-only observation; no lifecycle or sending mutation is available.</summary>
public interface ISubscriberAdministrationStore
{
    Task<SubscriberListPage> ReadAsync(SubscriberListQuery query, CancellationToken cancellationToken);
}
