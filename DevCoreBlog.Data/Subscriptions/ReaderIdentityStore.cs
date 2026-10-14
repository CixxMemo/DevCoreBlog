using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data.Email;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Lock order is budget, subscriber, proof. Consumption and unsubscription serialize on the subscriber.</summary>
public sealed class ReaderIdentityStore(ApplicationDbContext db, TransactionalEmailStore queue) : IReaderIdentityStore
{
    public async Task RequestAsync(SubscriberAddress address, DateTime now, Func<SubscriberSnapshot, PreparedReaderLogin> prepare, CancellationToken ct)
    {
        now = ReaderIdentityRules.Utc(now);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var day = now.Date;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReaderLoginBudgets" ("Id","WindowStartUtc","UpdatedUtc","Count")
            VALUES (1,{day},{now},0) ON CONFLICT ("Id") DO NOTHING
            """, ct);
        var budget = (await db.ReaderLoginBudgets.FromSqlRaw("SELECT * FROM \"ReaderLoginBudgets\" WHERE \"Id\"=1 FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).Single();
        var count = day == budget.WindowStartUtc ? budget.Count : 0;
        if (now < budget.UpdatedUtc || day < budget.WindowStartUtc || count >= ReaderIdentityRules.MaximumDailyEmails) return;
        var subscriber = (await db.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"AddressKey\"={address.Key} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (subscriber is null || !ReaderIdentityRules.CanAccess(subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression)
            || now < subscriber.UpdatedUtc) return;
        var cutoff = now - ReaderIdentityRules.LinkLifetime;
        if (await db.ReaderLoginTokens.AnyAsync(x => x.SubscriberId == subscriber.Id && x.CreatedUtc > cutoff, ct)) return;
        var prepared = prepare(SubscriberRepository.Snapshot(subscriber));
        var token = prepared.Token; var email = prepared.Email;
        if (token.Id == Guid.Empty || token.SubscriberId != subscriber.Id || token.SubscriberVersion != subscriber.Version
            || token.PeriodId != subscriber.PeriodId || token.CreatedUtc != now || token.ExpiresUtc != now + ReaderIdentityRules.LinkLifetime
            || token.AddressBinding != SubscriberVerificationRules.AddressBinding(subscriber.AddressKey)
            || token.Digest.Length != 64 || token.Digest.Any(c => !char.IsAsciiHexDigit(c))
            || email.Id != token.Id || email.SubscriberId != subscriber.Id || email.SubscriberVersion != subscriber.Version
            || email.CreatedUtc != now || email.ExpiresUtc != token.ExpiresUtc || email.Purpose != TransactionalEmailPurpose.ReaderLogin)
            throw new ArgumentException("Invalid reader login staging.");
        if (await queue.EnqueueAsync(email, ct) != EmailQueueOutcome.Queued) throw new InvalidOperationException("Reader login staging failed.");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReaderLoginTokens" ("Id","SubscriberId","SubscriberVersion","PeriodId","Digest","AddressBinding","CreatedUtc","ExpiresUtc")
            VALUES ({token.Id},{token.SubscriberId},{token.SubscriberVersion},{token.PeriodId},{token.Digest},{token.AddressBinding},{now},{token.ExpiresUtc})
            """, ct);
        await db.ReaderLoginBudgets.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Count, count + 1)
            .SetProperty(x => x.WindowStartUtc, day).SetProperty(x => x.UpdatedUtc, now), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<ReaderSessionGrant?> ConsumeAsync(string digest, DateTime now, CancellationToken ct)
    {
        now = ReaderIdentityRules.Utc(now);
        var identity = await db.ReaderLoginTokens.AsNoTracking().Where(x => x.Digest == digest)
            .Select(x => new { x.Id, x.SubscriberId }).SingleOrDefaultAsync(ct);
        if (identity is null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var subscriber = (await db.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"Id\"={identity.SubscriberId} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        var token = (await db.ReaderLoginTokens.FromSqlInterpolated($"SELECT * FROM \"ReaderLoginTokens\" WHERE \"Id\"={identity.Id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (subscriber is null || token is null || token.ConsumedUtc is not null || now < token.CreatedUtc || now >= token.ExpiresUtc
            || now < subscriber.UpdatedUtc || token.SubscriberVersion != subscriber.Version || token.PeriodId != subscriber.PeriodId
            || token.AddressBinding != SubscriberVerificationRules.AddressBinding(subscriber.AddressKey)
            || !ReaderIdentityRules.CanAccess(subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression)) return null;
        var session = ReaderSession.Create(SubscriberRepository.Snapshot(subscriber), now);
        var used = await db.ReaderLoginTokens.Where(x => x.Id == token.Id && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedUtc, now), ct);
        if (used != 1) return null;
        db.ReaderSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(session.Id, session.CreatedUtc, session.ExpiresUtc);
    }

    public async Task<bool> IsValidAsync(Guid sessionId, DateTime now, CancellationToken ct)
    {
        now = ReaderIdentityRules.Utc(now);
        var facts = await (from session in db.ReaderSessions.AsNoTracking()
                join subscriber in db.Subscribers.AsNoTracking() on session.SubscriberId equals subscriber.Id
                where session.Id == sessionId && session.RevokedUtc == null && session.CreatedUtc <= now && session.ExpiresUtc > now
                    && subscriber.UpdatedUtc <= now && session.SubscriberVersion == subscriber.Version && session.PeriodId == subscriber.PeriodId
                select new { subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression }).SingleOrDefaultAsync(ct);
        return facts is not null && ReaderIdentityRules.CanAccess(facts.Status, facts.VerifiedUtc, facts.Suppression);
    }

    public async Task RevokeAsync(Guid sessionId, DateTime now, CancellationToken ct)
    {
        now = ReaderIdentityRules.Utc(now);
        await db.ReaderSessions.Where(x => x.Id == sessionId && x.RevokedUtc == null && x.CreatedUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, now), ct);
    }
}
