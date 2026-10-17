using System.Data;
using System.Globalization;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Retention;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevCoreBlog.Data.Retention;

/// <summary>One bounded transaction; persistent clock and row guards survive restart and concurrent admission.</summary>
public sealed class RetentionStore(ApplicationDbContext db) : IRetentionStore
{
    public async Task<RetentionReport> RunAsync(DateTime utcNow, bool dryRun, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        utcNow = utcNow.AddTicks(-(utcNow.Ticks % 10));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RetentionPolicy.Deadline);
        var ct = deadline.Token;
        await using var tx = await db.Database.BeginTransactionAsync(dryRun ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'UTC'; SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", ct);
        if (dryRun) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        else await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(502026)", ct);
        var checkpoint = await db.Set<RetentionCheckpoint>().AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        if (utcNow < checkpoint.LastAppliedUtc)
            return new(utcNow, dryRun, true, new(0, 0, 0, 0, 0, 0, 0, 0));
        if (!dryRun)
        {
            var advanced = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"RetentionCheckpoints\" SET \"LastAppliedUtc\"={utcNow} WHERE \"Id\"=1 AND \"LastAppliedUtc\"<={utcNow}", ct);
            if (advanced != 1) throw new InvalidOperationException("Retention checkpoint could not advance.");
            var stamp = utcNow.ToString("O", CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('aether.retention_utc',{stamp},true)", ct);
        }
        var verification = await Process("SubscriberVerificationTokens", RetentionSelectors.Verification, "\"Digest\"=NULL,\"RedactedUtc\"=@now", utcNow, dryRun, ct);
        var login = await Process("ReaderLoginTokens", RetentionSelectors.Verification, "\"Digest\"=NULL,\"RedactedUtc\"=@now", utcNow, dryRun, ct);
        var sessions = await Process("ReaderSessions", RetentionSelectors.Session, null, utcNow, dryRun, ct);
        var payloads = await Process("TransactionalEmailJobs", RetentionSelectors.Payload, "\"ProtectedPayload\"=NULL,\"PayloadRedactedUtc\"=@now", utcNow, dryRun, ct);
        var provider = await Process("ProviderEmailEvents", RetentionSelectors.Provider, null, utcNow, dryRun, ct, "EventId");
        var orphan = dryRun ? await Count("Subscribers", RetentionSelectors.Orphan, utcNow, ct) : await RemoveOrphans(utcNow, ct);
        var exceptions = await Count("Subscribers", RetentionSelectors.OldPending + " AND NOT (" + RetentionSelectors.Orphan + ")", utcNow, ct);
        var held = await Count("TransactionalEmailJobs", "\"State\"=5 AND \"UpdatedUtc\" < @now-interval '30 days'", utcNow, ct);
        await tx.CommitAsync(ct);
        return new(utcNow, dryRun, false, new(verification, login, sessions, payloads, provider, orphan, exceptions, held));
    }

    private Task<int> Count(string table, string predicate, DateTime now, CancellationToken ct)
    {
        // Identifiers/predicates originate only from the fixed selectors above; time is always a parameter.
        var sql = $"SELECT count(*)::int AS \"Value\" FROM (SELECT 1 FROM \"{table}\" t WHERE {predicate} LIMIT {RetentionPolicy.SampleLimit}) q";
        return db.Database.SqlQueryRaw<int>(sql, new NpgsqlParameter("now", now)).SingleAsync(ct);
    }

    private Task<int> Process(string table, string predicate, string? set, DateTime now, bool dryRun, CancellationToken ct, string key = "Id")
    {
        if (dryRun) return Count(table, predicate, now, ct);
        var mutation = set is null ? $"DELETE FROM \"{table}\" t USING selected s WHERE t.\"{key}\"=s.\"{key}\""
            : $"UPDATE \"{table}\" t SET {set} FROM selected s WHERE t.\"{key}\"=s.\"{key}\"";
        var sql = $"""
            WITH selected AS (SELECT t."{key}" FROM "{table}" t WHERE {predicate}
                ORDER BY t."{key}" LIMIT {RetentionPolicy.BatchSize} FOR UPDATE SKIP LOCKED)
            {mutation}
            """;
        return db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("now", now)], ct);
    }

    private async Task<int> RemoveOrphans(DateTime now, CancellationToken ct)
    {
        // Match admission's budget -> address -> subscriber order, before acquiring any subscriber row lock.
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM \"SubscriptionRegistrationBudgets\" WHERE \"Id\"=1 FOR UPDATE", ct);
        var candidates = await db.Subscribers.FromSqlRaw($"SELECT t.* FROM \"Subscribers\" t WHERE {RetentionSelectors.Orphan} ORDER BY t.\"Id\" LIMIT {RetentionPolicy.BatchSize}", new NpgsqlParameter("now", now))
            .AsNoTracking().Select(x => new { x.Id, x.AddressKey }).ToListAsync(ct);
        var removed = 0;
        foreach (var candidate in candidates)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({candidate.AddressKey},362026))", ct);
            // Current predicate and FK/trigger checks close the selection-to-delete race.
            removed += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"Subscribers\" t WHERE t.\"Id\"=@id AND ({RetentionSelectors.Orphan})",
                [new NpgsqlParameter("now", now), new NpgsqlParameter("id", candidate.Id)], ct);
        }
        return removed;
    }
}
