using DevCoreBlog.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Publishing;

/// <summary>Serializes first insert and CAS changes with campaign creation; no tracked input entity is attached.</summary>
public sealed class PublicationSettingsStore(ApplicationDbContext db) : IPublicationSettingsStore
{
    private Task<PublicationSettings?> Query(CancellationToken ct) => db.PublicationSettings.AsNoTracking().SingleOrDefaultAsync(ct);
    public async Task<PublicationSettings?> ReadAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SET TRANSACTION READ ONLY", ct);
        var value = await Query(ct); await tx.CommitAsync(ct); return value;
    }
    public async Task<PublicationSaveStatus> SaveAsync(PublicationSettings proposed, long expectedVersion, CancellationToken ct)
    {
        if (expectedVersion is < 0 or >= long.MaxValue - 1 || proposed.Version != expectedVersion + 1)
            return PublicationSaveStatus.Invalid;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SELECT pg_advisory_xact_lock(422026)", ct);
        var old = await Query(ct);
        if ((old?.Version ?? 0) != expectedVersion || (old is not null && proposed.UpdatedUtc < old.UpdatedUtc)) return PublicationSaveStatus.Conflict;
        if (old is not null && old.Name == proposed.Name && old.Cadence == proposed.Cadence
            && old.SenderEmail == proposed.SenderEmail && old.SenderName == proposed.SenderName) return PublicationSaveStatus.Unchanged;
        var changed = old is null ? await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PublicationSettings" ("Id","Name","Cadence","SenderEmail","SenderName","Version","UpdatedUtc")
            VALUES (1,{proposed.Name},{proposed.Cadence},{proposed.SenderEmail},{proposed.SenderName},{proposed.Version},{proposed.UpdatedUtc})
            """, ct) : await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PublicationSettings" SET "Name"={proposed.Name},"Cadence"={proposed.Cadence},"SenderEmail"={proposed.SenderEmail},
                "SenderName"={proposed.SenderName},"Version"={proposed.Version},"UpdatedUtc"={proposed.UpdatedUtc}
            WHERE "Id"=1 AND "Version"={expectedVersion}
            """, ct);
        if (changed != 1) return PublicationSaveStatus.Conflict;
        await tx.CommitAsync(ct); return PublicationSaveStatus.Saved;
    }
}
