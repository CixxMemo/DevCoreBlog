using System.Data;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Repeatable-read SQL counts and a 25-row address projection share the same current-state observation.</summary>
public sealed class SubscriberAdministrationStore(ApplicationDbContext db) : ISubscriberAdministrationStore
{
    public async Task<SubscriberListPage> ReadAsync(SubscriberListQuery query, CancellationToken ct)
    {
        if (!query.IsValid) throw new ArgumentException("A bounded UTC query is required.", nameof(query));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SET TRANSACTION READ ONLY", ct);
        var facts = db.Subscribers.AsNoTracking().TagWith("F40 subscriber list").Select(x => new
        {
            x.Id, x.EmailAddress, x.Status, x.CreatedUtc,
            Blocked = x.Suppression != SendingSuppression.None || db.ProviderAddressSuppressions.Any(p => p.AddressKey == x.AddressKey),
            OwnedActive = x.Status == SubscriptionStatus.Active && x.VerifiedUtc != null
        });
        var totals = await facts.GroupBy(x => 1).Select(g => new SubscriberCurrentTotals(g.LongCount(),
            g.LongCount(x => x.Status == SubscriptionStatus.PendingVerification), g.LongCount(x => x.Status == SubscriptionStatus.Active),
            g.LongCount(x => x.Status == SubscriptionStatus.Unsubscribed), g.LongCount(x => x.Blocked),
            g.LongCount(x => x.OwnedActive && !x.Blocked))).SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0, 0);
        var added = await db.Subscribers.AsNoTracking().TagWith("F40 subscriber growth").LongCountAsync(x => x.CreatedUtc >= query.FromUtc
            && x.CreatedUtc < query.UntilUtc && x.CreatedUtc <= query.ObservedUtc, ct);
        var wanted = query.Status switch
        {
            SubscriberListStatus.Pending => SubscriptionStatus.PendingVerification,
            SubscriberListStatus.Active => SubscriptionStatus.Active,
            _ => SubscriptionStatus.Unsubscribed
        };
        var filtered = facts.Where(x => query.Status == SubscriberListStatus.All || x.Status == wanted);
        var count = await filtered.LongCountAsync(ct);
        var rows = await filtered.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * SubscriberListQuery.PageSize).Take(SubscriberListQuery.PageSize)
            .Select(x => new SubscriberListRow(x.EmailAddress, x.Status, x.CreatedUtc, x.Blocked, x.OwnedActive && !x.Blocked)).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(rows, count, totals, added);
    }
}
