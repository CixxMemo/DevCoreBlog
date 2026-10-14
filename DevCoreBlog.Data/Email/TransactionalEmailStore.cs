using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Email;

/// <summary>PostgreSQL row locks fence claims and journal transitions; network calls never hold these transactions.</summary>
public sealed class TransactionalEmailStore(ApplicationDbContext context) : ITransactionalEmailQueue, ITransactionalEmailWorkStore
{
    public async Task<EmailQueueOutcome> EnqueueAsync(PreparedTransactionalEmail email, CancellationToken cancellationToken)
    {
        var owns = context.Database.CurrentTransaction is null;
        await using var transaction = owns ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var subscriber = await LockSubscriber(email.SubscriberId, cancellationToken);
        if (subscriber is null || subscriber.Version != email.SubscriberVersion
            || !TransactionalEmailRules.CanSend(email.Purpose, subscriber.Status, subscriber.VerifiedUtc, subscriber.Suppression))
            return EmailQueueOutcome.Ineligible;
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TransactionalEmailJobs" ("Id","SubscriberId","SubscriberVersion","ProtectedPayload","PayloadHash",
                "CreatedUtc","UpdatedUtc","ExpiresUtc","DueUtc","State","Problem","AttemptCount","Purpose")
            VALUES ({email.Id},{email.SubscriberId},{email.SubscriberVersion},{email.ProtectedPayload},{email.PayloadHash},
                {email.CreatedUtc},{email.CreatedUtc},{email.ExpiresUtc},{email.CreatedUtc},0,0,0,{(int)email.Purpose})
            ON CONFLICT ("Id") DO NOTHING
            """, cancellationToken);
        var outcome = EmailQueueOutcome.Queued;
        if (inserted == 0)
        {
            var existing = await context.TransactionalEmailJobs.AsNoTracking().SingleAsync(x => x.Id == email.Id, cancellationToken);
            outcome = existing.Purpose == email.Purpose && existing.PayloadHash == email.PayloadHash && existing.SubscriberId == email.SubscriberId
                && existing.SubscriberVersion == email.SubscriberVersion && existing.ExpiresUtc == email.ExpiresUtc
                ? EmailQueueOutcome.AlreadyQueued : EmailQueueOutcome.Conflict;
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    public async Task<int> RecoverAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var jobs = await context.TransactionalEmailJobs.FromSqlInterpolated($"""
            SELECT * FROM "TransactionalEmailJobs"
            WHERE ("State" IN (1,2) AND "LeaseUntilUtc" <= {utcNow}) OR ("State" = 0 AND "ExpiresUtc" <= {utcNow})
            ORDER BY "UpdatedUtc", "Id" LIMIT 10 FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            var state = job.State == TransactionalEmailState.Sending ? TransactionalEmailState.Held
                : job.ExpiresUtc <= utcNow ? TransactionalEmailState.Expired : TransactionalEmailState.Pending;
            var problem = job.State == TransactionalEmailState.Sending ? TransactionalEmailProblem.LeaseLost
                : state == TransactionalEmailState.Expired ? TransactionalEmailProblem.LifetimeEnded : TransactionalEmailProblem.None;
            if (job.State == TransactionalEmailState.Sending)
                await context.TransactionalEmailAttempts.Where(x => x.JobId == job.Id && x.Number == job.AttemptCount && x.CompletedUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedUtc, utcNow)
                        .SetProperty(x => x.Outcome, EmailSendOutcome.Uncertain).SetProperty(x => x.Reason, EmailSendReason.Transport), cancellationToken);
            await SetState(job.Id, state, problem, utcNow, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return jobs.Count;
    }

    public async Task<TransactionalEmailLease?> ClaimAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var jobs = await context.TransactionalEmailJobs.FromSqlInterpolated($"""
            SELECT * FROM "TransactionalEmailJobs" WHERE "State" = 0 AND "DueUtc" <= {utcNow} AND "ExpiresUtc" > {utcNow}
            ORDER BY "DueUtc", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        if (jobs.Count == 0) return null;
        var job = jobs[0];
        var leaseId = Guid.NewGuid(); var until = utcNow + TransactionalEmailRules.Lease;
        await context.TransactionalEmailJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, TransactionalEmailState.Claimed).SetProperty(x => x.LeaseId, leaseId)
            .SetProperty(x => x.LeaseUntilUtc, until).SetProperty(x => x.UpdatedUtc, utcNow), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(job.Id, job.SubscriberId, job.SubscriberVersion, job.ProtectedPayload, job.PayloadHash,
            job.ExpiresUtc, job.FirstAttemptUtc, job.AttemptCount, leaseId, until) { Purpose = job.Purpose };
    }

    public async Task<TransactionalEmailStart?> StartAsync(TransactionalEmailLease lease, string addressKey, DateTime utcNow,
        CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var subscriber = await LockSubscriber(lease.SubscriberId, cancellationToken);
        var job = await LockJob(lease.Id, cancellationToken);
        if (!Owns(job, lease, TransactionalEmailState.Claimed, utcNow)) return null;
        if (job is null) return null;
        TransactionalEmailProblem problem = TransactionalEmailProblem.None;
        var state = TransactionalEmailState.Cancelled;
        if (subscriber is null || subscriber.Version != job.SubscriberVersion || subscriber.AddressKey != addressKey
            || !TransactionalEmailRules.CanSend(job.Purpose, subscriber.Status, subscriber.VerifiedUtc, SendingSuppression.None) || utcNow < subscriber.UpdatedUtc)
            problem = TransactionalEmailProblem.SubscriberChanged;
        else if (subscriber.Suppression != SendingSuppression.None) problem = TransactionalEmailProblem.Suppressed;
        else if (job.Purpose == TransactionalEmailPurpose.ReaderLogin && !await context.ReaderLoginTokens.AsNoTracking()
            .AnyAsync(x => x.Id == job.Id && x.SubscriberId == job.SubscriberId && x.SubscriberVersion == subscriber.Version
                && x.PeriodId == subscriber.PeriodId && x.ConsumedUtc == null && x.ExpiresUtc == job.ExpiresUtc && x.ExpiresUtc > utcNow, cancellationToken))
            problem = TransactionalEmailProblem.SubscriberChanged;
        else if (job.ExpiresUtc <= utcNow + TransactionalEmailRules.MinimumSendLifetime)
        { problem = TransactionalEmailProblem.LifetimeEnded; state = TransactionalEmailState.Expired; }
        else if (job.AttemptCount >= TransactionalEmailRules.MaximumAttempts)
        { problem = TransactionalEmailProblem.AttemptsExhausted; state = TransactionalEmailState.Held; }
        else if (job.FirstAttemptUtc is { } first && utcNow - first >= TransactionalEmailRules.MaximumRetryAge)
        { problem = TransactionalEmailProblem.LifetimeEnded; state = TransactionalEmailState.Held; }
        if (problem != TransactionalEmailProblem.None)
        {
            await SetState(job.Id, state, problem, utcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var number = job.AttemptCount + 1; var firstUtc = job.FirstAttemptUtc ?? utcNow;
        await context.TransactionalEmailJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, TransactionalEmailState.Sending).SetProperty(x => x.AttemptCount, number)
            .SetProperty(x => x.FirstAttemptUtc, firstUtc).SetProperty(x => x.UpdatedUtc, utcNow)
            .SetProperty(x => x.LeaseUntilUtc, utcNow + TransactionalEmailRules.Lease), cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TransactionalEmailAttempts" ("JobId","Number","LeaseId","StartedUtc")
            VALUES ({job.Id},{number},{lease.LeaseId},{utcNow})
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(number, firstUtc);
    }

    public async Task<bool> HoldAsync(TransactionalEmailLease lease, TransactionalEmailProblem problem, DateTime utcNow,
        CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow);
        return await context.TransactionalEmailJobs.Where(x => x.Id == lease.Id && x.State == TransactionalEmailState.Claimed
            && x.LeaseId == lease.LeaseId && x.LeaseUntilUtc > utcNow).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, TransactionalEmailState.Held).SetProperty(x => x.Problem, problem)
                .SetProperty(x => x.UpdatedUtc, utcNow).SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.LeaseUntilUtc, (DateTime?)null), cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(TransactionalEmailLease lease, int attemptNumber, TransactionalEmailCompletion completion,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        TransactionalEmailRules.RequireUtc(utcNow); TransactionalEmailRules.RequireUtc(completion.DueUtc);
        if (completion.State is not (TransactionalEmailState.Accepted or TransactionalEmailState.Rejected
            or TransactionalEmailState.Held or TransactionalEmailState.Pending or TransactionalEmailState.Expired))
            throw new ArgumentException("Invalid completion state.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var job = await LockJob(lease.Id, cancellationToken);
        if (!Owns(job, lease, TransactionalEmailState.Sending, utcNow) || job?.AttemptCount != attemptNumber) return false;
        var result = completion.Result;
        var affected = await context.TransactionalEmailAttempts.Where(x => x.JobId == lease.Id && x.Number == attemptNumber
            && x.LeaseId == lease.LeaseId && x.CompletedUtc == null).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CompletedUtc, utcNow).SetProperty(x => x.Outcome, result.Outcome)
                .SetProperty(x => x.Reason, result.Reason).SetProperty(x => x.HttpStatus, result.HttpStatus)
                .SetProperty(x => x.ProviderMessageId, result.ProviderMessageId), cancellationToken);
        if (affected != 1) return false;
        await context.TransactionalEmailJobs.Where(x => x.Id == lease.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, completion.State).SetProperty(x => x.Problem, completion.Problem)
            .SetProperty(x => x.DueUtc, completion.DueUtc).SetProperty(x => x.UpdatedUtc, utcNow)
            .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
            .SetProperty(x => x.ProviderMessageId, completion.State == TransactionalEmailState.Accepted ? result.ProviderMessageId : null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private Task<int> SetState(Guid id, TransactionalEmailState state, TransactionalEmailProblem problem, DateTime now, CancellationToken ct) =>
        context.TransactionalEmailJobs.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state)
            .SetProperty(x => x.Problem, problem).SetProperty(x => x.UpdatedUtc, now)
            .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null), ct);
    private async Task<Subscriber?> LockSubscriber(Guid id, CancellationToken ct) =>
        (await context.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
    private async Task<TransactionalEmailJob?> LockJob(Guid id, CancellationToken ct) =>
        (await context.TransactionalEmailJobs.FromSqlInterpolated($"SELECT * FROM \"TransactionalEmailJobs\" WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
    private static bool Owns(TransactionalEmailJob? job, TransactionalEmailLease lease, TransactionalEmailState state, DateTime now) =>
        job is not null && job.State == state && job.LeaseId == lease.LeaseId && job.LeaseUntilUtc > now && job.UpdatedUtc <= now;
}
