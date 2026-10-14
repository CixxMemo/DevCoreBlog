using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Unique-key creation and revision predicates serialize lifecycle writes without replacing unrelated data.</summary>
public sealed class SubscriberRepository(ApplicationDbContext context) : ISubscriberRepository
{
    public async Task<SubscriberSnapshot> GetOrCreatePendingAsync(SubscriberAddress address, DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var pending = Subscriber.CreatePending(address, utcNow);
        // The unique index resolves races. A duplicate must not change an existing subscription.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Subscribers" ("Id", "PeriodId", "EmailAddress", "AddressKey", "Status", "Suppression", "CreatedUtc", "UpdatedUtc", "Version")
            VALUES ({pending.Id}, {pending.PeriodId}, {pending.EmailAddress}, {pending.AddressKey}, 0, 0, {utcNow}, {utcNow}, 1)
            ON CONFLICT ("AddressKey") DO NOTHING
            """, cancellationToken);
        // Separate Read Committed statement sees a concurrent winning insert after conflict resolution.
        var stored = await context.Subscribers.AsNoTracking().SingleAsync(item => item.AddressKey == address.Key, cancellationToken);
        return Snapshot(stored);
    }

    public async Task<SubscriberSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await context.Subscribers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return stored is null ? null : Snapshot(stored);
    }

    public async Task<SubscriberWriteOutcome> TryApplyAsync(Guid id, long expectedVersion, SubscriberChange change,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identity is required.", nameof(id));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        var stored = await context.Subscribers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (stored is null) return SubscriberWriteOutcome.NotFound;
        if (stored.Version != expectedVersion) return SubscriberWriteOutcome.Conflict;
        var transition = stored.Apply(change, utcNow);
        if (transition == SubscriberTransition.InvalidTime) return SubscriberWriteOutcome.InvalidTime;
        if (transition == SubscriberTransition.InvalidTransition) return SubscriberWriteOutcome.InvalidTransition;
        if (transition == SubscriberTransition.Unchanged)
        {
            // Recheck a no-op against concurrent unsubscribe/suppression without physically rewriting the row.
            return await context.Subscribers.AnyAsync(item => item.Id == id && item.Version == expectedVersion, cancellationToken)
                ? SubscriberWriteOutcome.Unchanged : SubscriberWriteOutcome.Conflict;
        }
        var affected = await context.Subscribers.Where(item => item.Id == id && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, stored.Status)
                .SetProperty(item => item.Suppression, stored.Suppression)
                .SetProperty(item => item.VerifiedUtc, stored.VerifiedUtc)
                .SetProperty(item => item.UnsubscribedUtc, stored.UnsubscribedUtc)
                .SetProperty(item => item.UpdatedUtc, stored.UpdatedUtc)
                .SetProperty(item => item.PeriodId, stored.PeriodId)
                .SetProperty(item => item.Version, stored.Version), cancellationToken);
        return affected == 1 ? SubscriberWriteOutcome.Changed : SubscriberWriteOutcome.Conflict;
    }

    internal static SubscriberSnapshot Snapshot(Subscriber item) => new(item.Id, item.EmailAddress, item.AddressKey,
        item.Status, item.Suppression, item.CreatedUtc, item.UpdatedUtc, item.VerifiedUtc, item.UnsubscribedUtc, item.Version, item.PeriodId);
}
