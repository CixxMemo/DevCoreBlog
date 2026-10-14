using DevCoreBlog.Core.Email;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Email;

/// <summary>Receipt uniqueness, independent event facts and permanent suppression are one PostgreSQL transaction.</summary>
public sealed class ProviderEmailEventStore(ApplicationDbContext db) : IProviderEmailEventStore
{
    public async Task<ProviderEventOutcome> ApplyAsync(ProviderEmailEvent message, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'", cancellationToken);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProviderEmailEvents" ("EventId","PayloadHash","MessageId","AddressKey","Kind","OccurredUtc","ReceivedUtc")
            VALUES ({message.EventId},{message.PayloadHash},{message.MessageId},{message.AddressKey},{(int)message.Kind},{message.OccurredUtc},{message.ReceivedUtc})
            ON CONFLICT ("EventId") DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            var hash = await db.ProviderEmailEvents.AsNoTracking().Where(x => x.EventId == message.EventId)
                .Select(x => x.PayloadHash).SingleAsync(cancellationToken);
            return hash == message.PayloadHash ? ProviderEventOutcome.Duplicate : ProviderEventOutcome.Conflict;
        }
        DateTime? delivered = message.Kind == ProviderEmailEventKind.Delivered ? message.OccurredUtc : null;
        DateTime? bounced = message.Kind == ProviderEmailEventKind.Bounced ? message.OccurredUtc : null;
        DateTime? complained = message.Kind == ProviderEmailEventKind.Complained ? message.OccurredUtc : null;
        DateTime? temporary = message.Kind == ProviderEmailEventKind.TemporaryBounce ? message.OccurredUtc : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProviderEmailObservations" ("MessageId","AddressKey","DeliveredUtc","BouncedUtc","ComplainedUtc","TemporaryBounceUtc")
            VALUES ({message.MessageId},{message.AddressKey},{delivered},{bounced},{complained},{temporary})
            ON CONFLICT ("MessageId","AddressKey") DO UPDATE SET
                "DeliveredUtc"=least("ProviderEmailObservations"."DeliveredUtc",excluded."DeliveredUtc"),
                "BouncedUtc"=least("ProviderEmailObservations"."BouncedUtc",excluded."BouncedUtc"),
                "ComplainedUtc"=least("ProviderEmailObservations"."ComplainedUtc",excluded."ComplainedUtc"),
                "TemporaryBounceUtc"=least("ProviderEmailObservations"."TemporaryBounceUtc",excluded."TemporaryBounceUtc")
            """, cancellationToken);
        var flag = message.Kind switch { ProviderEmailEventKind.Bounced => 1, ProviderEmailEventKind.Complained => 2, _ => 0 };
        if (flag != 0)
        {
            // Same address lock is used by the subscriber INSERT trigger, closing first-registration/event races.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({message.AddressKey},362026))", cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProviderAddressSuppressions" ("AddressKey","Flags","UpdatedUtc") VALUES ({message.AddressKey},{flag},{message.ReceivedUtc})
                ON CONFLICT ("AddressKey") DO UPDATE SET "Flags"="ProviderAddressSuppressions"."Flags"|excluded."Flags",
                    "UpdatedUtc"=greatest("ProviderAddressSuppressions"."UpdatedUtc",excluded."UpdatedUtc")
                WHERE ("ProviderAddressSuppressions"."Flags"|excluded."Flags")<>"ProviderAddressSuppressions"."Flags"
                """, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Subscribers" SET "Suppression"="Suppression"|{flag},"Version"="Version"+1,
                    "UpdatedUtc"=greatest("UpdatedUtc",{message.ReceivedUtc})
                WHERE "AddressKey"={message.AddressKey} AND ("Suppression"&{flag})=0
                """, cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return ProviderEventOutcome.Applied;
    }
}
