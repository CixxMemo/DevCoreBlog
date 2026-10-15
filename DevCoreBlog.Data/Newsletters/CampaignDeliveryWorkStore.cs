using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Persistent claims and attempts; start locks campaign then subscriber then job, while recovery never takes a campaign lock after a job.</summary>
public sealed class CampaignDeliveryWorkStore(ApplicationDbContext db):ICampaignDeliveryWorkStore
{
    private Task<int> Limits(CancellationToken ct)=>db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'",ct);
    public async Task<int> RecoverAsync(DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Limits(ct);
        var rows=await db.Database.SqlQuery<RecoveryRow>($"""
            SELECT j."Id",j."State",j."AttemptCount",j."ExpiresUtc",j."Version",c."State"=1 AS "CampaignCancelled",d."PreparationState"=2 AS "PreparationStopped"
            FROM "CampaignDeliveryJobs" j JOIN "NewsletterCampaigns" c ON c."Id"=j."CampaignId"
                JOIN "CampaignDispatches" d ON d."CampaignId"=j."CampaignId"
            WHERE j."UpdatedUtc"<={now} AND ((j."State"=2 AND j."LeaseUntilUtc"<={now})
                OR (j."State" IN (0,1) AND (j."ExpiresUtc"<={now+CampaignDeliveryRules.MinimumSendLifetime} OR c."State"=1 OR d."PreparationState"=2))
                OR (j."State"=1 AND j."LeaseUntilUtc"<={now}))
            ORDER BY j."UpdatedUtc",j."Id" LIMIT 10 FOR UPDATE OF j SKIP LOCKED
            """).ToListAsync(ct);
        foreach(var job in rows)
        {
            var cancelled=job.CampaignCancelled;
            var stopped=job.PreparationStopped;
            var state=job.State==CampaignDeliveryState.Sending?CampaignDeliveryState.Held:
                cancelled?CampaignDeliveryState.Cancelled:stopped?CampaignDeliveryState.Held:
                job.ExpiresUtc<=now+CampaignDeliveryRules.MinimumSendLifetime?CampaignDeliveryState.Expired:CampaignDeliveryState.Pending;
            var problem=job.State==CampaignDeliveryState.Sending?CampaignDeliveryProblem.LeaseLost:
                cancelled?CampaignDeliveryProblem.CampaignCancelled:stopped?CampaignDeliveryProblem.ConfigurationChanged:
                state==CampaignDeliveryState.Expired?CampaignDeliveryProblem.LifetimeEnded:CampaignDeliveryProblem.None;
            if(job.State==CampaignDeliveryState.Sending)
                await db.CampaignDeliveryAttempts.Where(a=>a.JobId==job.Id && a.Number==job.AttemptCount && a.CompletedUtc==null)
                    .ExecuteUpdateAsync(s=>s.SetProperty(a=>a.CompletedUtc,now).SetProperty(a=>a.Outcome,EmailSendOutcome.Uncertain)
                        .SetProperty(a=>a.Reason,EmailSendReason.Transport),ct);
            await SetState(job.Id,job.Version,state,problem,now,ct);
        }
        await tx.CommitAsync(ct);return rows.Count;
    }

    public async Task<CampaignDeliveryLease?> ClaimAsync(DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Limits(ct);
        var job=(await db.CampaignDeliveryJobs.FromSqlInterpolated($"""
            SELECT j.* FROM "CampaignDeliveryJobs" j JOIN "CampaignDispatches" d ON d."CampaignId"=j."CampaignId"
                JOIN "NewsletterCampaigns" c ON c."Id"=j."CampaignId"
            WHERE j."State"=0 AND j."UpdatedUtc"<={now} AND j."DueUtc"<={now}
                AND j."ExpiresUtc">{now+CampaignDeliveryRules.MinimumSendLifetime} AND d."PreparationState"=1 AND c."State"=0
            ORDER BY j."DueUtc",j."Id" LIMIT 1 FOR UPDATE OF j SKIP LOCKED
            """).AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if(job is null)return null;
        var leaseId=Guid.NewGuid();var until=now+CampaignDeliveryRules.Lease;
        await db.CampaignDeliveryJobs.Where(j=>j.Id==job.Id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,CampaignDeliveryState.Claimed)
            .SetProperty(j=>j.LeaseId,leaseId).SetProperty(j=>j.LeaseUntilUtc,until).SetProperty(j=>j.UpdatedUtc,now).SetProperty(j=>j.Version,job.Version+1),ct);
        var hash=await db.CampaignDispatches.Where(d=>d.CampaignId==job.CampaignId).Select(d=>d.SnapshotHash).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return new(job.Id,job.CampaignId,job.SubscriberId,job.SubscriberVersion,job.PeriodId,job.ProtectedPayload,job.PayloadHash,
            job.ExpiresUtc,job.FirstAttemptUtc,job.AttemptCount,leaseId,until,job.Version+1,hash);
    }

    public async Task<CampaignDeliveryStart?> StartAsync(CampaignDeliveryLease lease,string addressKey,DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Limits(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"NewsletterCampaigns\" WHERE \"Id\"={lease.CampaignId} FOR UPDATE",ct);
        var subscriber=(await db.Subscribers.FromSqlInterpolated($"SELECT * FROM \"Subscribers\" WHERE \"Id\"={lease.SubscriberId} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        var job=await LockJob(lease.Id,ct);
        if(!Owns(job,lease,CampaignDeliveryState.Claimed,lease.Version,now) || job is null)return null;
        var problem=CampaignDeliveryProblem.None;var state=CampaignDeliveryState.Cancelled;
        if(!await db.NewsletterCampaigns.AnyAsync(c=>c.Id==job.CampaignId && c.State==NewsletterCampaignState.Prepared,ct))problem=CampaignDeliveryProblem.CampaignCancelled;
        else if(subscriber is null || subscriber.Version!=job.SubscriberVersion || subscriber.PeriodId!=job.PeriodId || subscriber.AddressKey!=addressKey
            || subscriber.UpdatedUtc>now || !SubscriberSendingRules.IsEligible(subscriber.Status,subscriber.VerifiedUtc,SendingSuppression.None))problem=CampaignDeliveryProblem.SubscriberChanged;
        else if(subscriber.Suppression!=SendingSuppression.None || await db.ProviderAddressSuppressions.AnyAsync(s=>s.AddressKey==addressKey,ct))problem=CampaignDeliveryProblem.Suppressed;
        else if(job.ExpiresUtc<=now+CampaignDeliveryRules.MinimumSendLifetime){problem=CampaignDeliveryProblem.LifetimeEnded;state=CampaignDeliveryState.Expired;}
        else if(job.AttemptCount>=CampaignDeliveryRules.MaximumAttempts){problem=CampaignDeliveryProblem.AttemptsExhausted;state=CampaignDeliveryState.Held;}
        else if(job.FirstAttemptUtc is {} first && now-first>=CampaignDeliveryRules.MaximumRetryAge){problem=CampaignDeliveryProblem.LifetimeEnded;state=CampaignDeliveryState.Held;}
        if(problem!=CampaignDeliveryProblem.None)
        {
            await SetState(job.Id,job.Version,state,problem,now,ct);await tx.CommitAsync(ct);return null;
        }
        var number=job.AttemptCount+1;var firstUtc=job.FirstAttemptUtc??now;
        await db.CampaignDeliveryJobs.Where(j=>j.Id==job.Id && j.Version==job.Version).ExecuteUpdateAsync(s=>s
            .SetProperty(j=>j.State,CampaignDeliveryState.Sending).SetProperty(j=>j.AttemptCount,number).SetProperty(j=>j.FirstAttemptUtc,firstUtc)
            .SetProperty(j=>j.UpdatedUtc,now).SetProperty(j=>j.LeaseUntilUtc,now+CampaignDeliveryRules.Lease).SetProperty(j=>j.Version,job.Version+1),ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CampaignDeliveryAttempts" ("JobId","Number","LeaseId","StartedUtc") VALUES ({job.Id},{number},{lease.LeaseId},{now})
            """,ct);
        await tx.CommitAsync(ct);return new(number,firstUtc,job.Version+1);
    }

    public async Task<bool> HoldAsync(CampaignDeliveryLease lease,CampaignDeliveryProblem problem,DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Limits(ct);
        var job=await LockJob(lease.Id,ct);if(!Owns(job,lease,CampaignDeliveryState.Claimed,lease.Version,now) || job is null)return false;
        await SetState(job.Id,job.Version,CampaignDeliveryState.Held,problem,now,ct);await tx.CommitAsync(ct);return true;
    }

    public async Task<bool> CompleteAsync(CampaignDeliveryLease lease,CampaignDeliveryStart start,CampaignDeliveryCompletion completion,DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);NewsletterCampaign.RequireUtc(completion.DueUtc);
        if(completion.State is not (CampaignDeliveryState.Accepted or CampaignDeliveryState.Rejected or CampaignDeliveryState.Held or CampaignDeliveryState.Pending or CampaignDeliveryState.Expired))
            throw new ArgumentException("Invalid completion state.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Limits(ct);
        var job=await LockJob(lease.Id,ct);
        if(!Owns(job,lease,CampaignDeliveryState.Sending,start.Version,now) || job is null || job.AttemptCount!=start.AttemptNumber || job.FirstAttemptUtc!=start.FirstAttemptUtc)return false;
        var result=completion.Result;
        var updated=await db.CampaignDeliveryAttempts.Where(a=>a.JobId==job.Id && a.Number==start.AttemptNumber && a.LeaseId==lease.LeaseId && a.CompletedUtc==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(a=>a.CompletedUtc,now).SetProperty(a=>a.Outcome,result.Outcome).SetProperty(a=>a.Reason,result.Reason)
                .SetProperty(a=>a.HttpStatus,result.HttpStatus).SetProperty(a=>a.ProviderMessageId,result.ProviderMessageId),ct);
        if(updated!=1)return false;
        await db.CampaignDeliveryJobs.Where(j=>j.Id==job.Id && j.Version==job.Version).ExecuteUpdateAsync(s=>s
            .SetProperty(j=>j.State,completion.State).SetProperty(j=>j.Problem,completion.Problem).SetProperty(j=>j.DueUtc,completion.DueUtc)
            .SetProperty(j=>j.UpdatedUtc,now).SetProperty(j=>j.LeaseId,(Guid?)null).SetProperty(j=>j.LeaseUntilUtc,(DateTime?)null)
            .SetProperty(j=>j.ProviderMessageId,completion.State==CampaignDeliveryState.Accepted?result.ProviderMessageId:null).SetProperty(j=>j.Version,job.Version+1),ct);
        await tx.CommitAsync(ct);return true;
    }
    private Task<int> SetState(Guid id,long version,CampaignDeliveryState state,CampaignDeliveryProblem problem,DateTime now,CancellationToken ct)=>
        db.CampaignDeliveryJobs.Where(j=>j.Id==id && j.Version==version).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.State,state).SetProperty(j=>j.Problem,problem)
            .SetProperty(j=>j.UpdatedUtc,now).SetProperty(j=>j.LeaseId,(Guid?)null).SetProperty(j=>j.LeaseUntilUtc,(DateTime?)null).SetProperty(j=>j.Version,version+1),ct);
    private async Task<LockedJob?> LockJob(Guid id,CancellationToken ct)=>(await db.Database.SqlQuery<LockedJob>($"""
        SELECT "Id","CampaignId","SubscriberId","SubscriberVersion","PeriodId","State","AttemptCount","FirstAttemptUtc",
            "ExpiresUtc","LeaseId","LeaseUntilUtc","UpdatedUtc","Version" FROM "CampaignDeliveryJobs" WHERE "Id"={id} FOR UPDATE
        """).ToListAsync(ct)).SingleOrDefault();
    private static bool Owns(LockedJob? job,CampaignDeliveryLease lease,CampaignDeliveryState state,long version,DateTime now)=>
        job is not null && job.CampaignId==lease.CampaignId && job.SubscriberId==lease.SubscriberId && job.State==state && job.LeaseId==lease.LeaseId
        && job.Version==version && job.LeaseUntilUtc>now && job.UpdatedUtc<=now;
    private sealed record LockedJob(Guid Id,Guid CampaignId,Guid SubscriberId,long SubscriberVersion,Guid PeriodId,CampaignDeliveryState State,
        int AttemptCount,DateTime? FirstAttemptUtc,DateTime ExpiresUtc,Guid? LeaseId,DateTime? LeaseUntilUtc,DateTime UpdatedUtc,long Version);
    private sealed record RecoveryRow(Guid Id,CampaignDeliveryState State,int AttemptCount,DateTime ExpiresUtc,long Version,bool CampaignCancelled,bool PreparationStopped);
}
