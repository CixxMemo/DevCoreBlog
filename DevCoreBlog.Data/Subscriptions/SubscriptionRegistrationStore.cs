using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Global counter then subscriber locks bound public issuance across races, restarts and lifecycle versions.</summary>
public sealed class SubscriptionRegistrationStore(ApplicationDbContext context, SubscriberRepository subscribers,
    SubscriberVerificationStore verification) : ISubscriptionRegistrationStore
{
    public async Task<RegistrationWriteOutcome> RegisterAsync(SubscriberAddress address, DateTime utcNow,
        Func<SubscriberSnapshot, PreparedSubscriberVerification> prepare, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        utcNow = utcNow.AddTicks(-(utcNow.Ticks % 10));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var day = utcNow.Date;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriptionRegistrationBudgets" ("Id","WindowStartUtc","Count","UpdatedUtc")
            VALUES (1,{day},0,{utcNow}) ON CONFLICT ("Id") DO NOTHING
            """, cancellationToken);
        var budget = (await context.SubscriptionRegistrationBudgets.FromSqlRaw("""
            SELECT * FROM "SubscriptionRegistrationBudgets" WHERE "Id"=1 FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken)).Single();
        if (utcNow < budget.UpdatedUtc || day < budget.WindowStartUtc) return RegistrationWriteOutcome.NoAction;
        var count = day == budget.WindowStartUtc ? budget.Count : 0;
        // Admission happens before creating an identity; quota exhaustion cannot grow the subscriber table.
        if (count >= SubscriptionRegistrationRules.MaximumDailyEmails) return RegistrationWriteOutcome.NoAction;
        var existing = await subscribers.GetOrCreatePendingAsync(address, utcNow, cancellationToken);
        var stored = (await context.Subscribers.FromSqlInterpolated($"""
            SELECT * FROM "Subscribers" WHERE "Id"={existing.Id} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken)).Single();
        if (stored.Status == SubscriptionStatus.Active || stored.Suppression != SendingSuppression.None || utcNow < stored.UpdatedUtc)
            return RegistrationWriteOutcome.NoAction;
        var cutoff = utcNow - SubscriptionRegistrationRules.AddressCooldown;
        if (await context.SubscriberVerificationTokens.AsNoTracking().AnyAsync(x => x.SubscriberId == stored.Id
            && x.CreatedUtc > cutoff, cancellationToken)) return RegistrationWriteOutcome.NoAction;
        var snapshot = SubscriberRepository.Snapshot(stored);
        if (stored.Status == SubscriptionStatus.Unsubscribed)
        {
            // A repeated explicit request may reopen only pending proof; suppression and exit history survive.
            var reopened = await subscribers.TryApplyAsync(stored.Id, stored.Version, SubscriberChange.RequestReconfirmation, utcNow, cancellationToken);
            if (reopened != SubscriberWriteOutcome.Changed) return RegistrationWriteOutcome.NoAction;
            snapshot = await subscribers.FindAsync(stored.Id, cancellationToken)
                ?? throw new InvalidOperationException("Subscription changed during registration.");
        }
        var prepared = prepare(snapshot);
        if (prepared.Token.SubscriberId != snapshot.Id || prepared.Token.SubscriberVersion != snapshot.Version
            || prepared.Token.CreatedUtc != utcNow) throw new ArgumentException("Registration proof does not match its admission.");
        var staged = await verification.StageAsync(prepared.Token, prepared.Email, cancellationToken);
        if (staged != VerificationStageOutcome.Queued) return RegistrationWriteOutcome.NoAction;
        await context.SubscriptionRegistrationBudgets.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.WindowStartUtc, day).SetProperty(x => x.Count, count + 1).SetProperty(x => x.UpdatedUtc, utcNow), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RegistrationWriteOutcome.Queued;
    }
}
