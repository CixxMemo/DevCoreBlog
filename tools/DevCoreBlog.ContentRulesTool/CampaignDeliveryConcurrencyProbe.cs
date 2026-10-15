using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static partial class CampaignDeliveryProbe
{
    private static async Task ConcurrencyTests(Fixture f)
    {
        var sub=await f.Single();
        var claims=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{await using var own=new ApplicationDbContext(f.Options);return await new CampaignDeliveryWorkStore(own).ClaimAsync(f.Time.Now,default);}));
        f.Check("two_workers_have_one_owner_for_one_pending_job",claims.Count(x=>x is not null)==1);
        var lease=claims.Single(x=>x is not null)??throw new InvalidOperationException();
        var starts=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{await using var own=new ApplicationDbContext(f.Options);return await new CampaignDeliveryWorkStore(own).StartAsync(lease,sub.AddressKey,f.Time.Now,default);}));
        f.Check("duplicate_start_has_one_durable_attempt",starts.Count(x=>x is not null)==1&&await f.Db.CampaignDeliveryAttempts.CountAsync(a=>a.JobId==lease.Id)==1);
        var start=starts.Single(x=>x is not null)??throw new InvalidOperationException();var id=Guid.NewGuid();var result=new EmailSendResult(EmailSendOutcome.Accepted,EmailSendReason.None,id,200);
        var finishes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{await using var own=new ApplicationDbContext(f.Options);return await new CampaignDeliveryWorkStore(own).CompleteAsync(lease,start,
            DevCoreBlog.Services.Newsletters.CampaignDeliveryRetryPolicy.Decide(lease,start,result,f.Time.Now),f.Time.Now,default);}));
        f.Check("duplicate_completion_has_one_irreversible_provider_acceptance",finishes.Count(x=>x)==1&&(await f.Job()).ProviderMessageId==id);
        foreach(var change in new[]{SubscriberChange.Unsubscribe,SubscriberChange.SuppressBounce,SubscriberChange.SuppressComplaint,SubscriberChange.SuppressManual})
        {
            sub=await f.Single();await new SubscriberRepository(f.Db).TryApplyAsync(sub.Id,sub.Version,change,f.Time.Now,default);
            var sender=new Sender();await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
            f.Check("fresh_"+change+"_prevents_call_and_attempt",sender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Cancelled&&(await f.Job()).AttemptCount==0);
        }
        sub=await f.Single();var repo=new SubscriberRepository(f.Db);var oldPeriod=sub.PeriodId;
        await repo.TryApplyAsync(sub.Id,sub.Version,SubscriberChange.Unsubscribe,f.Time.Now,default);sub=await repo.FindAsync(sub.Id,default)??throw new InvalidOperationException();
        await repo.TryApplyAsync(sub.Id,sub.Version,SubscriberChange.RequestReconfirmation,f.Time.Now,default);sub=await repo.FindAsync(sub.Id,default)??throw new InvalidOperationException();
        await repo.TryApplyAsync(sub.Id,sub.Version,SubscriberChange.ConfirmOwnership,f.Time.Now,default);var reopened=await repo.FindAsync(sub.Id,default)??throw new InvalidOperationException();
        var freshSender=new Sender();await f.Dispatcher(f.Db,freshSender).RunOnceAsync(default);
        f.Check("new_subscription_period_never_revives_old_campaign",reopened.PeriodId!=oldPeriod&&freshSender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Cancelled);
        sub=await f.Single();await new NewsletterCampaignStore(f.Db).TryCancelAsync(f.Campaign,1,f.Time.Now,default);
        freshSender=new Sender();await f.Dispatcher(f.Db,freshSender).RunOnceAsync(default);
        f.Check("campaign_cancellation_prevents_unstarted_transport",freshSender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Cancelled&&(await f.Job()).AttemptCount==0);
        // Ordered SQL gates prove both sides of the durable-start boundary on actual row locks.
        await ExitRace(f,exitFirst:true);await ExitRace(f,exitFirst:false);
        await f.Single();lease=await f.Claim();
        f.Check("claim_cannot_invent_attempt_without_atomic_journal",await f.Reject($"UPDATE \"CampaignDeliveryJobs\" SET \"State\"=2,\"AttemptCount\"=1,\"FirstAttemptUtc\"=\"UpdatedUtc\",\"Version\"=\"Version\"+1 WHERE \"Id\"={lease.Id}"));
        await using(var tx=await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE \"CampaignDeliveryJobs\" DISABLE TRIGGER \"TR_CampaignDeliveryJobs_Commit\"");
            var changed=await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CampaignDeliveryJobs\" SET \"State\"=2,\"AttemptCount\"=1,\"FirstAttemptUtc\"=\"UpdatedUtc\",\"Version\"=\"Version\"+1 WHERE \"Id\"={lease.Id}");
            f.Check("negative_control_detects_missing_atomic_attempt_guard",changed==1);await tx.RollbackAsync();
        }
        f.Check("negative_control_restores_claim_and_no_journal",(await f.Job()).State==CampaignDeliveryState.Claimed&&(await f.Job()).AttemptCount==0);
    }
    private static async Task ExitRace(Fixture f,bool exitFirst)
    {
        var sub=await f.Single();
        var gateSql=exitFirst
            ? "CREATE FUNCTION delivery_race_gate() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_advisory_xact_lock(734371); RETURN NEW; END $$; CREATE TRIGGER delivery_race_gate BEFORE UPDATE ON \"Subscribers\" FOR EACH ROW EXECUTE FUNCTION delivery_race_gate();"
            : "CREATE FUNCTION delivery_race_gate() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_advisory_xact_lock(734371); RETURN NEW; END $$; CREATE TRIGGER delivery_race_gate BEFORE INSERT ON \"CampaignDeliveryAttempts\" FOR EACH ROW EXECUTE FUNCTION delivery_race_gate();";
        await f.Db.Database.ExecuteSqlRawAsync(gateSql);
        await using var hold=new NpgsqlConnection(f.Db.Database.GetConnectionString());await hold.OpenAsync();await using var tx=await hold.BeginTransactionAsync();
        await using(var command=new NpgsqlCommand("SELECT pg_advisory_xact_lock(734371)",hold,tx))await command.ExecuteNonQueryAsync();
        await using var monitor=new NpgsqlConnection(f.Db.Database.GetConnectionString());await monitor.OpenAsync();
        async Task<bool> Wait(string predicate)
        {
            for(var i=0;i<80;i++)
            {await using var q=new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND state='active' AND "+predicate,monitor);
                if(Convert.ToInt64(await q.ExecuteScalarAsync())>0)return true;await Task.Delay(20);}
            return false;
        }
        async Task<UnsubscriptionOutcome> Exit()
        {
            await using var own=new ApplicationDbContext(f.Options);
            return await new SubscriberUnsubscriptionStore(own,new SubscriberRepository(own)).UnsubscribeAsync(new(sub.Id,sub.PeriodId,
                SubscriberVerificationRules.AddressBinding(sub.AddressKey)),f.Time.Now,default);
        }
        var sender=new Sender();
        async Task Send(){await using var own=new ApplicationDbContext(f.Options);await f.Dispatcher(own,sender).RunOnceAsync(default);}
        var released=false;
        try
        {
            var first=exitFirst?(Task)Exit():Send();var paused=await Wait("wait_event='advisory'");
            var second=exitFirst?Send():(Task)Exit();var blocked=await Wait("wait_event_type='Lock' AND wait_event<>'advisory'");
            await tx.CommitAsync();released=true;await Task.WhenAll(first,second);
            f.Check(exitFirst?"exit_commit_before_start_prevents_transport":"exit_after_durable_start_waits_and_cannot_retract_reserved_call",paused&&blocked
                &&sender.Requests.Count==(exitFirst?0:1)&&(await f.Job()).AttemptCount==(exitFirst?0:1)
                &&(await f.Job()).State==(exitFirst?CampaignDeliveryState.Cancelled:CampaignDeliveryState.Accepted));
        }
        finally
        {if(!released)await tx.RollbackAsync();var dropSql=exitFirst?"DROP TRIGGER delivery_race_gate ON \"Subscribers\"; DROP FUNCTION delivery_race_gate()":"DROP TRIGGER delivery_race_gate ON \"CampaignDeliveryAttempts\"; DROP FUNCTION delivery_race_gate()";await f.Db.Database.ExecuteSqlRawAsync(dropSql);}
    }
}
