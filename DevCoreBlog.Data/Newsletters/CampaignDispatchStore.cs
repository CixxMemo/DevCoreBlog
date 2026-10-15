using System.Linq.Expressions;
using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Explicit intent and cursor/jobs commit atomically; immutable recipient membership is the only staging source.</summary>
public sealed class CampaignDispatchStore(ApplicationDbContext db):ICampaignDispatchStore
{
    private static readonly Expression<Func<NewsletterCampaign,CampaignMessage>> MessageProjection=x=>new(x.Id,x.Subject,x.Html,x.Text,x.SnapshotHash,x.SenderEmail,x.SenderName,x.ReplyTo,x.ProviderContract);
    public Task<CampaignMessage?> ReadMessageAsync(Guid campaignId,CancellationToken ct)=>db.NewsletterCampaigns.AsNoTracking()
        .Where(x=>x.Id==campaignId && x.State==NewsletterCampaignState.Prepared).Select(MessageProjection).SingleOrDefaultAsync(ct);

    public async Task<CampaignArmOutcome> ArmAsync(PreparedCampaignDispatch dispatch,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(dispatch.CreatedUtc);NewsletterCampaign.RequireUtc(dispatch.ExpiresUtc);
        if(dispatch.CampaignId==Guid.Empty || dispatch.ExpiresUtc-dispatch.CreatedUtc!=CampaignDeliveryRules.MaximumLifetime)
            return CampaignArmOutcome.InvalidState;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"NewsletterCampaigns\" WHERE \"Id\"={dispatch.CampaignId} FOR UPDATE",ct);
        var source=await db.NewsletterCampaigns.AsNoTracking().Where(x=>x.Id==dispatch.CampaignId).Select(x=>new{x.State,x.SnapshotHash,x.CreatedUtc}).SingleOrDefaultAsync(ct);
        if(source is null)return CampaignArmOutcome.NotFound;
        if(source.State!=NewsletterCampaignState.Prepared || source.SnapshotHash!=dispatch.SnapshotHash || source.CreatedUtc>dispatch.CreatedUtc
            || !await db.NewsletterRecipientSelections.AnyAsync(x=>x.CampaignId==dispatch.CampaignId && x.State==RecipientSelectionState.Complete && x.UpdatedUtc<=dispatch.CreatedUtc,ct))
            return CampaignArmOutcome.InvalidState;
        if(await db.CampaignDispatches.AnyAsync(x=>x.CampaignId==dispatch.CampaignId,ct))return CampaignArmOutcome.Existing;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CampaignDispatches" ("CampaignId","ProtectedContext","ContextHash","SnapshotHash","CreatedUtc","ExpiresUtc","UpdatedUtc","StagedCount","PreparationState","Version")
            VALUES ({dispatch.CampaignId},{dispatch.ProtectedContext},{dispatch.ContextHash},{dispatch.SnapshotHash},{dispatch.CreatedUtc},{dispatch.ExpiresUtc},{dispatch.CreatedUtc},0,0,1)
            """,ct);
        await tx.CommitAsync(ct);return CampaignArmOutcome.Armed;
    }

    public async Task<CampaignPreparation?> ReadPreparationAsync(DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        // Stop at most ten expired/cancelled preparations; never create new intent just because a campaign exists.
        await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'",ct);
            var stopped=await db.CampaignDispatches.FromSqlInterpolated($"""
                SELECT d.* FROM "CampaignDispatches" d JOIN "NewsletterCampaigns" c ON c."Id"=d."CampaignId"
                WHERE d."PreparationState"=0 AND d."UpdatedUtc"<={now} AND (d."ExpiresUtc"<={now+CampaignDeliveryRules.MinimumSendLifetime} OR c."State"=1)
                ORDER BY d."CreatedUtc",d."CampaignId" LIMIT 10 FOR UPDATE OF d SKIP LOCKED
                """).AsNoTracking().ToListAsync(ct);
            foreach(var item in stopped)await StopPreparationAsync(item.CampaignId,item.Version,now,ct);
            await tx.CommitAsync(ct);
        }
        var dispatch=await db.CampaignDispatches.AsNoTracking().Where(d=>d.PreparationState==CampaignPreparationState.Building
            && d.ExpiresUtc>now+CampaignDeliveryRules.MinimumSendLifetime && d.UpdatedUtc<=now
            && db.NewsletterCampaigns.Any(c=>c.Id==d.CampaignId && c.State==NewsletterCampaignState.Prepared))
            .OrderBy(d=>d.CreatedUtc).ThenBy(d=>d.CampaignId).FirstOrDefaultAsync(ct);
        if(dispatch is null)return null;
        var message=await ReadMessageAsync(dispatch.CampaignId,ct);if(message is null)return null;
        var candidates=await db.NewsletterRecipients.AsNoTracking().Where(r=>r.CampaignId==dispatch.CampaignId)
            .Where(r=>r.SubscriberId.CompareTo(dispatch.LastSubscriberId??Guid.Empty)>0).OrderBy(r=>r.SubscriberId)
            .Select(r=>new CampaignSelectedAddress(r.SubscriberId,r.SubscriberVersion,r.PeriodId,r.EmailAddress,r.AddressKey))
            .Take(CampaignDeliveryRules.PreparationBatchSize+1).ToListAsync(ct);
        return new(dispatch,message,candidates.Take(CampaignDeliveryRules.PreparationBatchSize).ToArray(),candidates.Count<=CampaignDeliveryRules.PreparationBatchSize);
    }

    public async Task<bool> CommitPreparationAsync(CampaignPreparation page,IReadOnlyList<PreparedCampaignDelivery> jobs,DateTime now,CancellationToken ct)
    {
        NewsletterCampaign.RequireUtc(now);
        if(jobs.Count!=page.Addresses.Count || jobs.Count>CampaignDeliveryRules.PreparationBatchSize)return false;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"NewsletterCampaigns\" WHERE \"Id\"={page.Dispatch.CampaignId} FOR UPDATE",ct);
        var dispatch=(await db.CampaignDispatches.FromSqlInterpolated($"SELECT * FROM \"CampaignDispatches\" WHERE \"CampaignId\"={page.Dispatch.CampaignId} FOR UPDATE").AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if(dispatch is null || dispatch.Version!=page.Dispatch.Version || dispatch.PreparationState!=CampaignPreparationState.Building
            || dispatch.UpdatedUtc>now || dispatch.ExpiresUtc<=now+CampaignDeliveryRules.MinimumSendLifetime
            || !await db.NewsletterCampaigns.AnyAsync(x=>x.Id==dispatch.CampaignId && x.State==NewsletterCampaignState.Prepared,ct))return false;
        for(var i=0;i<jobs.Count;i++)
        {
            var job=jobs[i];var address=page.Addresses[i];
            if(job.CampaignId!=dispatch.CampaignId || job.SubscriberId!=address.SubscriberId || job.SubscriberVersion!=address.SubscriberVersion
                || job.PeriodId!=address.PeriodId || job.PreparationVersion!=dispatch.Version+1 || job.ExpiresUtc!=dispatch.ExpiresUtc || job.CreatedUtc>now)return false;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CampaignDeliveryJobs" ("Id","CampaignId","SubscriberId","SubscriberVersion","PeriodId","PreparationVersion","ProtectedPayload","PayloadHash",
                    "CreatedUtc","UpdatedUtc","ExpiresUtc","DueUtc","State","Problem","AttemptCount","Version")
                VALUES ({job.Id},{job.CampaignId},{job.SubscriberId},{job.SubscriberVersion},{job.PeriodId},{job.PreparationVersion},{job.ProtectedPayload},{job.PayloadHash},
                    {job.CreatedUtc},{job.CreatedUtc},{job.ExpiresUtc},{job.CreatedUtc},0,0,0,1)
                """,ct);
        }
        var last=jobs.Count>0?jobs[^1].SubscriberId:dispatch.LastSubscriberId;
        var state=page.IsFinalPage?CampaignPreparationState.Complete:CampaignPreparationState.Building;
        await db.CampaignDispatches.Where(d=>d.CampaignId==dispatch.CampaignId && d.Version==dispatch.Version).ExecuteUpdateAsync(s=>s
            .SetProperty(d=>d.LastSubscriberId,last).SetProperty(d=>d.StagedCount,dispatch.StagedCount+jobs.Count)
            .SetProperty(d=>d.PreparationState,state).SetProperty(d=>d.UpdatedUtc,now).SetProperty(d=>d.Version,dispatch.Version+1),ct);
        await tx.CommitAsync(ct);return true;
    }
    public async Task<bool> StopPreparationAsync(Guid id,long version,DateTime now,CancellationToken ct)=>
        await db.CampaignDispatches.Where(d=>d.CampaignId==id && d.Version==version && d.PreparationState==CampaignPreparationState.Building && d.UpdatedUtc<=now)
            .ExecuteUpdateAsync(s=>s.SetProperty(d=>d.PreparationState,CampaignPreparationState.Stopped).SetProperty(d=>d.UpdatedUtc,now).SetProperty(d=>d.Version,version+1),ct)==1;
}
