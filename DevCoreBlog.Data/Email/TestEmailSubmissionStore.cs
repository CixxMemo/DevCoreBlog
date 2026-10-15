using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Email;

/// <summary>Single test start, rolling budget and current suppression are serialized before transport; no subscribers are created.</summary>
public sealed class TestEmailSubmissionStore(ApplicationDbContext db) : ITestEmailSubmissionStore
{
    public async Task<TestEmailReservation> ReserveAsync(TestEmailIntent intent, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(intent.CreatedUtc);
        if (intent.Id == Guid.Empty || intent.InputHash.Length != 64 || intent.InputHash.Any(c => !char.IsAsciiHexDigit(c))
            || !SubscriberAddress.TryCreate(intent.AddressKey, out var address) || address?.Key != intent.AddressKey)
            throw new ArgumentException("Invalid bounded test intent.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SELECT pg_advisory_xact_lock(383826)", cancellationToken);
        var existing = await db.TestEmailSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == intent.Id, cancellationToken);
        if (existing is not null) return new(existing.InputHash == intent.InputHash && existing.AddressKey == intent.AddressKey
            ? TestEmailAdmission.Existing : TestEmailAdmission.Conflict, existing);
        var recent = await db.TestEmailSubmissions.AsNoTracking().Where(s => s.CreatedUtc > intent.CreatedUtc.AddDays(-1))
            .OrderByDescending(s => s.CreatedUtc).Select(s => s.CreatedUtc).Take(3).ToListAsync(cancellationToken);
        if (recent.Count > 0 && recent[0] > intent.CreatedUtc) return new(TestEmailAdmission.InvalidTime, null);
        var next = recent.Count > 0 ? recent[0].AddMinutes(1) : intent.CreatedUtc;
        if (recent.Count == 3 && recent[^1].AddDays(1) > next) next = recent[^1].AddDays(1);
        if (next > intent.CreatedUtc) return new(TestEmailAdmission.Limited, null, next - intent.CreatedUtc);
        // Identical address advisory namespace/order to the provider ledger and subscriber INSERT trigger.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({intent.AddressKey},362026))", cancellationToken);
        var source = (await db.Database.SqlQuery<Source>($"SELECT \"Status\",\"Suppression\",\"UpdatedUtc\" FROM \"Subscribers\" WHERE \"AddressKey\"={intent.AddressKey} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (source is not null && (source.Status == SubscriptionStatus.Unsubscribed || source.Suppression != SendingSuppression.None || source.UpdatedUtc > intent.CreatedUtc)
            || await db.ProviderAddressSuppressions.AnyAsync(s => s.AddressKey == intent.AddressKey, cancellationToken))
            return new(TestEmailAdmission.Suppressed, null);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TestEmailSubmissions" ("Id","InputHash","AddressKey","CreatedUtc","Version")
            VALUES ({intent.Id},{intent.InputHash},{intent.AddressKey},{intent.CreatedUtc},1)
            """, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(TestEmailAdmission.Reserved, null);
    }

    public async Task<bool> CompleteAsync(Guid id, string inputHash, EmailSendResult result, DateTime completedUtc, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(completedUtc);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", cancellationToken);
        var changed = await db.TestEmailSubmissions.Where(s => s.Id == id && s.InputHash == inputHash && s.Outcome == null && s.Version == 1
                && s.CreatedUtc <= completedUtc && s.CreatedUtc > completedUtc.AddMinutes(-1))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Outcome, result.Outcome).SetProperty(x => x.Reason, result.Reason)
                .SetProperty(x => x.HttpStatus, result.HttpStatus).SetProperty(x => x.ProviderMessageId, result.ProviderMessageId)
                .SetProperty(x => x.CompletedUtc, completedUtc).SetProperty(x => x.Version, 2L), cancellationToken);
        await tx.CommitAsync(cancellationToken); return changed == 1;
    }
    private sealed record Source(SubscriptionStatus Status, SendingSuppression Suppression, DateTime UpdatedUtc);
}
