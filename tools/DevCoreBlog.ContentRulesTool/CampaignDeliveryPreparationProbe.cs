using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Newsletters;
using DevCoreBlog.Services.Email;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static partial class CampaignDeliveryProbe
{
    private static async Task PreparationTests(Fixture f)
    {
        foreach(var sub in await f.Db.Subscribers.AsNoTracking().Select(s=>new{s.Id,s.Version}).ToListAsync())
            await new SubscriberRepository(f.Db).TryApplyAsync(sub.Id,sub.Version,SubscriberChange.Unsubscribe,f.Time.Now,default);
        for(var i=0;i<25;i++)await f.Subscriber();
        await f.NewCampaign();await f.Select();var off=new CampaignPreparationService(new CampaignDispatchStore(f.Db),f.Protector(),f.Ready(false),f.Config,f.Time);
        f.Check("disabled_readiness_does_not_arm_or_send",await off.ArmAsync(f.Campaign,default)==CampaignArmOutcome.NotConfigured
            && !await f.Db.CampaignDispatches.AnyAsync() && !await f.Db.CampaignDeliveryJobs.AnyAsync());
        f.Check("selection_and_site_snapshot_alone_do_not_send",!await f.Dispatcher(f.Db,new Sender()).RunOnceAsync(default));
        var arms=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{await using var own=new ApplicationDbContext(f.Options);return await f.Preparation(own).ArmAsync(f.Campaign,default);}));
        f.Check("two_parallel_intents_arm_once",arms.Count(x=>x==CampaignArmOutcome.Armed)==1&&arms.Count(x=>x==CampaignArmOutcome.Existing)==1);
        var context=await f.Db.CampaignDispatches.AsNoTracking().SingleAsync();
        var gate=new PreparationGate();
        await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            await using var own=new ApplicationDbContext(f.Options);
            await new CampaignPreparationService(new GatedPreparation(new CampaignDispatchStore(own),gate),f.Protector(),f.Ready(),f.Config,f.Time).PrepareOnceAsync(default);
        }));
        var partial=await f.Db.CampaignDispatches.AsNoTracking().SingleAsync();
        f.Check("same_page_concurrency_commits_ten_jobs_and_one_cursor",partial.StagedCount==10&&partial.Version==2&&await f.Db.CampaignDeliveryJobs.CountAsync()==10);
        f.Check("partial_preparation_cannot_be_claimed",await new CampaignDeliveryWorkStore(f.Db).ClaimAsync(f.Time.Now,default)is null);
        f.Check("actual_restart_finishes_preparation_and_sends_exactly_selected_count",await f.Child(25)
            && await f.Db.CampaignDeliveryJobs.CountAsync(j=>j.State==CampaignDeliveryState.Accepted)==25
            && await f.Db.CampaignDeliveryAttempts.CountAsync()==25);
        var complete=await f.Db.CampaignDispatches.AsNoTracking().SingleAsync();
        f.Check("immutable_context_and_complete_cursor_survive_restart",complete.ProtectedContext==context.ProtectedContext&&complete.ContextHash==context.ContextHash
            && complete.PreparationState==CampaignPreparationState.Complete&&complete.StagedCount==25&&complete.Version==4);
        var cipher=await f.Db.CampaignDeliveryJobs.AsNoTracking().Select(j=>j.ProtectedPayload).ToListAsync();
        f.Check("persistent_envelopes_hide_body_recipient_key_and_exit_proof",cipher.All(c=>!c.Contains("Private synthetic",StringComparison.Ordinal)&&!c.Contains('@')&&!c.Contains("re_synthetic",StringComparison.Ordinal)));
        f.Check("complete_arm_replay_never_rekeys_or_restages",await f.Preparation(f.Db).ArmAsync(f.Campaign,default)==CampaignArmOutcome.Existing
            && !await f.Preparation(f.Db).PrepareOnceAsync(default)&&complete.Version==await f.Db.CampaignDispatches.Select(d=>d.Version).SingleAsync());
        f.Check("dispatch_and_payload_mutation_and_deletion_rejected",await f.Reject($"UPDATE \"CampaignDispatches\" SET \"SnapshotHash\"={new string('A',64)} WHERE \"CampaignId\"={f.Campaign}")
            && await f.Reject($"DELETE FROM \"CampaignDispatches\" WHERE \"CampaignId\"={f.Campaign}")
            && await f.Reject($"UPDATE \"CampaignDeliveryJobs\" SET \"ProtectedPayload\"={"YWJj"},\"Version\"=\"Version\"+1 WHERE \"CampaignId\"={f.Campaign}")
            && await f.Reject($"DELETE FROM \"CampaignDeliveryJobs\" WHERE \"CampaignId\"={f.Campaign}")
            && await f.Reject($"DELETE FROM \"CampaignDeliveryAttempts\" WHERE \"JobId\" IN (SELECT \"Id\" FROM \"CampaignDeliveryJobs\" WHERE \"CampaignId\"={f.Campaign})"));
        await f.Single();await new NewsletterCampaignStore(f.Db).TryCancelAsync(f.Campaign,1,f.Time.Now,default);
        await new CampaignDeliveryWorkStore(f.Db).RecoverAsync(f.Time.Now,default);
        await f.NewCampaign();await f.Select();await f.Preparation(f.Db).ArmAsync(f.Campaign,default);
        // A database failure is injected after actual encrypted inserts, rather than failing before the transaction.
        await f.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION delivery_fixture_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic staging failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER delivery_fixture_fail BEFORE UPDATE ON "CampaignDispatches" FOR EACH ROW EXECUTE FUNCTION delivery_fixture_fail();
            """);
        var failed=false;
        try{await f.Preparation(f.Db).PrepareOnceAsync(default);}catch(PostgresException e)when(e.SqlState=="23514"){failed=true;}
        finally{await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER delivery_fixture_fail ON \"CampaignDispatches\"; DROP FUNCTION delivery_fixture_fail()");}
        var progress=await f.Db.CampaignDispatches.AsNoTracking().SingleAsync(d=>d.CampaignId==f.Campaign);
        f.Check("staging_failure_rolls_back_ciphertext_cursor_count_and_version",failed&&progress.StagedCount==0&&progress.Version==1&&!await f.Db.CampaignDeliveryJobs.AnyAsync(j=>j.CampaignId==f.Campaign));
        await f.Stage();f.Check("rollback_can_resume_same_intent",(await f.Job()).State==CampaignDeliveryState.Pending);
        await new NewsletterCampaignStore(f.Db).TryCancelAsync(f.Campaign,1,f.Time.Now,default);
        await new CampaignDeliveryWorkStore(f.Db).RecoverAsync(f.Time.Now,default);
        var source=await f.Source(new string('&',19500));await f.NewCampaign(source);await f.Select();
        var message=await new CampaignDispatchStore(f.Db).ReadMessageAsync(f.Campaign,default)??throw new InvalidOperationException();
        f.Check("large_valid_body_demonstrates_default_json_escape_overflow",System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{message.Html,message.Text}).Length>2_097_152);
        await f.Stage();var largeSender=new Sender();await f.Dispatcher(f.Db,largeSender).RunOnceAsync(default);
        f.Check("large_renderer_body_with_entities_stages_and_sends_without_truncation",(await f.Job()).State==CampaignDeliveryState.Accepted
            &&largeSender.Requests.Count==1&&largeSender.Requests[0].Html.StartsWith(message.Html,StringComparison.Ordinal)
            &&largeSender.Requests[0].Text.StartsWith(message.Text,StringComparison.Ordinal));
        using var boundary=new NoNetwork();using var client=new HttpClient(boundary);
        var preflight=await new ResendEmailSender(client,f.Config,f.Time).SendAsync(largeSender.Requests[0]);
        f.Check("large_envelope_does_not_bypass_existing_provider_wire_budget",preflight.Outcome==EmailSendOutcome.Rejected
            &&preflight.Reason==EmailSendReason.PayloadLimit&&boundary.Calls==0);
    }
    private sealed class NoNetwork:HttpMessageHandler
    {
        public int Calls{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Calls++;throw new InvalidOperationException("Unexpected network invocation in local preflight probe.");}
    }
    private sealed class PreparationGate
    {
        private int arrivals;private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Wait(CancellationToken ct){if(Interlocked.Increment(ref arrivals)==2)ready.TrySetResult();return ready.Task.WaitAsync(TimeSpan.FromSeconds(5),ct);}
    }
    private sealed class GatedPreparation(ICampaignDispatchStore store,PreparationGate gate):ICampaignDispatchStore
    {
        public Task<CampaignMessage?> ReadMessageAsync(Guid id,CancellationToken ct)=>store.ReadMessageAsync(id,ct);
        public Task<CampaignArmOutcome> ArmAsync(PreparedCampaignDispatch value,CancellationToken ct)=>store.ArmAsync(value,ct);
        public Task<CampaignPreparation?> ReadPreparationAsync(DateTime now,CancellationToken ct)=>store.ReadPreparationAsync(now,ct);
        public async Task<bool> CommitPreparationAsync(CampaignPreparation page,IReadOnlyList<PreparedCampaignDelivery> jobs,DateTime now,CancellationToken ct)
        {await gate.Wait(ct);return await store.CommitPreparationAsync(page,jobs,now,ct);}
        public Task<bool> StopPreparationAsync(Guid id,long version,DateTime now,CancellationToken ct)=>store.StopPreparationAsync(id,version,now,ct);
    }
}
