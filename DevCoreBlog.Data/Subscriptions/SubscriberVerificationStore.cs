using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data.Email;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>One DbContext transaction couples proof, pending lifecycle revision and encrypted mail staging.</summary>
public sealed class SubscriberVerificationStore(ApplicationDbContext context, SubscriberRepository subscribers,
    TransactionalEmailStore queue) : ISubscriberVerificationStore
{
    public async Task<VerificationStageOutcome> StageAsync(SubscriberVerificationDraft token, PreparedTransactionalEmail email,
        CancellationToken cancellationToken)
    {
        if (token.Id != email.Id || token.SubscriberId != email.SubscriberId || token.SubscriberVersion != email.SubscriberVersion
            || token.CreatedUtc != email.CreatedUtc || token.ExpiresUtc != email.ExpiresUtc)
            throw new ArgumentException("Proof and mail must share one identity.");
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var subscriber = await LockSubscriber(token.SubscriberId, cancellationToken);
        if (subscriber is null || subscriber.Version != token.SubscriberVersion || subscriber.Status != SubscriptionStatus.PendingVerification
            || subscriber.Suppression != SendingSuppression.None || token.CreatedUtc < subscriber.UpdatedUtc
            || SubscriberVerificationRules.AddressBinding(subscriber.AddressKey) != token.AddressBinding)
            return VerificationStageOutcome.Ineligible;
        if (await context.SubscriberVerificationTokens.AsNoTracking().AnyAsync(x => x.SubscriberId == token.SubscriberId
            && x.SubscriberVersion == token.SubscriberVersion && x.ConsumedUtc == null && x.ExpiresUtc > token.CreatedUtc, cancellationToken))
            return VerificationStageOutcome.AlreadyPending;
        var queued = await queue.EnqueueAsync(email, cancellationToken);
        if (queued != EmailQueueOutcome.Queued) return VerificationStageOutcome.Ineligible;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriberVerificationTokens" ("Id","SubscriberId","SubscriberVersion","Purpose","Digest","AddressBinding","CreatedUtc","ExpiresUtc")
            VALUES ({token.Id},{token.SubscriberId},{token.SubscriberVersion},1,{token.Digest},{token.AddressBinding},{token.CreatedUtc},{token.ExpiresUtc})
            """, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return VerificationStageOutcome.Queued;
    }

    public async Task<VerificationConsumeOutcome> ConsumeAsync(string digest, int purpose, DateTime utcNow, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        if (purpose != SubscriberVerificationRules.Purpose || digest.Length != 64) return VerificationConsumeOutcome.Invalid;
        var identity = await context.SubscriberVerificationTokens.AsNoTracking().Where(x => x.Digest == digest)
            .Select(x => new { x.Id, x.SubscriberId }).SingleOrDefaultAsync(cancellationToken);
        if (identity is null) return VerificationConsumeOutcome.Invalid;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var subscriber = await LockSubscriber(identity.SubscriberId, cancellationToken);
        var token = (await context.SubscriberVerificationTokens.FromSqlInterpolated($"""
            SELECT * FROM "SubscriberVerificationTokens" WHERE "Id"={identity.Id} FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
        if (subscriber is null || token is null || token.Purpose != purpose || token.ConsumedUtc is not null
            || utcNow < token.CreatedUtc || utcNow >= token.ExpiresUtc || utcNow < subscriber.UpdatedUtc
            || token.SubscriberVersion != subscriber.Version || token.AddressBinding != SubscriberVerificationRules.AddressBinding(subscriber.AddressKey)
            || subscriber.Status != SubscriptionStatus.PendingVerification || subscriber.Suppression != SendingSuppression.None)
            return VerificationConsumeOutcome.Invalid;
        var result = await subscribers.TryApplyAsync(subscriber.Id, subscriber.Version, SubscriberChange.ConfirmOwnership, utcNow, cancellationToken);
        if (result != SubscriberWriteOutcome.Changed) return VerificationConsumeOutcome.Invalid;
        await context.SubscriberVerificationTokens.Where(x => x.Id == token.Id && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedUtc, utcNow), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return VerificationConsumeOutcome.Confirmed;
    }
    private async Task<Subscriber?> LockSubscriber(Guid id, CancellationToken ct) =>
        (await context.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"Id\"={id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
}
