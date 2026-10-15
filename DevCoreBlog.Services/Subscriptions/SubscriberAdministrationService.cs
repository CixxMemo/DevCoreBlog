using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Services.Subscriptions;

public sealed record SubscriberDateRange(DateOnly From, DateOnly To, DateTime FromUtc, DateTime UntilUtc, DateTime ObservedUtc);
/// <summary>Converts bounded calendar dates in the site timezone; storage failures remain failures rather than empty metrics.</summary>
public sealed class SubscriberAdministrationService(ISubscriberAdministrationStore store, PublicationTimeZone zone, TimeProvider clock)
{
    public SubscriberDateRange? Dates(DateOnly? from, DateOnly? to)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(zone.ToSiteTime(now));
        var end = to ?? today;
        if (end.Year < 1900 || end > today) return null;
        var start = from ?? end.AddDays(-29);
        if (start.Year < 1900 || end > today || start > end || end.DayNumber - start.DayNumber > 365) return null;
        if (!zone.TryConvertToUtc(start.ToDateTime(TimeOnly.MinValue), out var utcFrom, out _)
            || !zone.TryConvertToUtc(end.AddDays(1).ToDateTime(TimeOnly.MinValue), out var utcUntil, out _)) return null;
        return new(start, end, utcFrom, utcUntil, now);
    }
    public async Task<SubscriberListPage> ReadAsync(int page, SubscriberListStatus status, SubscriberDateRange dates, CancellationToken ct)
    {
        var query = new SubscriberListQuery(page, status, dates.FromUtc, dates.UntilUtc, dates.ObservedUtc);
        if (!query.IsValid) throw new ArgumentException("Invalid subscriber list query.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return await store.ReadAsync(query, deadline.Token);
    }
}
