using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Email;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Owned real database checks, official verifier and fault injection; output contains only booleans.</summary>
internal static class ProviderEmailEventProbe
{
    private static readonly string Secret = "whsec_" + Convert.ToBase64String(new byte[32].Select((_,i)=>(byte)(i+1)).ToArray());
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var checks = new Dictionary<string,bool>(); string last = "ownership";
        void Check(string name, bool value) { last=name; checks[name]=value; if (!value) throw new InvalidOperationException(); }
        try
        {
            await using var db = new ApplicationDbContext(options);
            var root=Environment.GetEnvironmentVariable("PROVIDER_TEST_ROOT") ?? "";
            var conn=new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            await db.Database.OpenConnectionAsync();
            using var guard=db.Database.GetDbConnection().CreateCommand(); guard.CommandText="SHOW data_directory";
            if (!root.StartsWith("/tmp/devcoreblog-provider-",StringComparison.Ordinal) || (string?)await guard.ExecuteScalarAsync()!=root+"/pg"
                || conn.Host!="127.0.0.1" || conn.Database?.StartsWith("provider_",StringComparison.Ordinal)!=true) return 2;
            var now=ReaderIdentityRules.Utc(DateTime.UtcNow);
            var repository=new SubscriberRepository(db);
            async Task<SubscriberSnapshot> Subscriber(string email)
            {
                if (!SubscriberAddress.TryCreate(email,out var address) || address is null) throw new InvalidOperationException();
                return await repository.GetOrCreatePendingAsync(address,now.AddMinutes(-1),default);
            }
            if (args.Contains("--provider-events-seed"))
            {
                var seed=await Subscriber("preserved@example.test");
                await repository.TryApplyAsync(seed.Id,seed.Version,SubscriberChange.SuppressBounce,now,default);
                Check("previous_suppressed_subscriber_seeded",(await repository.FindAsync(seed.Id,default))?.Suppression==SendingSuppression.Bounce);
                return Report();
            }
            var active=await Subscriber("events@example.test");
            await repository.TryApplyAsync(active.Id,active.Version,SubscriberChange.ConfirmOwnership,now.AddSeconds(-30),default);
            var webhookOptions=new ResendWebhookOptions(true,Secret,"onboarding@resend.dev");
            var service=new ResendWebhookService(webhookOptions,new ProviderEmailEventStore(db),TimeProvider.System);
            var signer=new Svix.Webhook(Secret); var messageId=Guid.NewGuid();
            string Body(string type, string address="events@example.test", Guid? id=null, string bounce="Permanent", DateTime? occurred=null)=>
                JsonSerializer.Serialize(new { type, created_at=(occurred??now).ToString("O"), data=new { email_id=id??messageId, from="Fixture <onboarding@resend.dev>", to=new[]{address}, bounce=new { type=bounce }, subject="DO_NOT_LOG_PRIVATE_SUBJECT" }});
            Task<WebhookResponse> Receive(string id,string body, DateTimeOffset? stamp=null,string? signature=null)
            {
                var time=stamp??DateTimeOffset.UtcNow;
                return service.ReceiveAsync(body,id,time.ToUnixTimeSeconds().ToString(),signature??signer.Sign(id,time,body),default);
            }
            var delivered=Body("email.delivered");
            Check("signature_rejects_modified_raw_body",await Receive("msg_tamper",delivered+" ",signature:signer.Sign("msg_tamper",DateTimeOffset.UtcNow,delivered))==WebhookResponse.InvalidSignature);
            Check("old_and_future_attempt_timestamps_rejected",await Receive("msg_old",delivered,DateTimeOffset.UtcNow.AddMinutes(-6))==WebhookResponse.InvalidSignature
                && await Receive("msg_future",delivered,DateTimeOffset.UtcNow.AddMinutes(6))==WebhookResponse.InvalidSignature);
            Check("wrong_secret_cannot_affect_subscriber",await Receive("msg_wrong",Body("email.complained"),signature:new Svix.Webhook(Convert.ToBase64String(new byte[32])).Sign("msg_wrong",DateTimeOffset.UtcNow,delivered))==WebhookResponse.InvalidSignature
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.None);
            Check("delivered_is_recorded_without_changing_subscription",await Receive("msg_delivered",delivered)==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.None);
            var version=(await repository.FindAsync(active.Id,default))?.Version;
            Check("duplicate_is_no_write",await Receive("msg_delivered",delivered)==WebhookResponse.Accepted && await db.ProviderEmailEvents.CountAsync()==1
                && (await repository.FindAsync(active.Id,default))?.Version==version);
            Check("same_id_different_payload_is_conflict",await Receive("msg_delivered",Body("email.complained"))==WebhookResponse.Conflict
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.None);
            Check("tracking_ignored_without_receipt",await Receive("msg_opened",Body("email.opened"))==WebhookResponse.Accepted && await db.ProviderEmailEvents.CountAsync()==1);
            var bad=delivered.Replace("\"type\":\"email.delivered\"","\"type\":\"email.delivered\",\"type\":\"email.complained\"");
            Check("duplicate_properties_rejected",await Receive("msg_dupfields",bad)==WebhookResponse.InvalidPayload);
            Check("unapproved_sender_rejected",await Receive("msg_foreign",delivered.Replace("onboarding@resend.dev","other@example.test"))==WebhookResponse.InvalidPayload);
            Check("multiple_recipients_rejected",await Receive("msg_multi",delivered.Replace("[\"events@example.test\"]","[\"events@example.test\",\"other@example.test\"]"))==WebhookResponse.InvalidPayload);
            Check("ambiguous_event_time_rejected",await Receive("msg_time",delivered.Replace(now.ToString("O"),"2026-10-14"))==WebhookResponse.InvalidPayload);
            Check("temporary_bounce_does_not_suppress",await Receive("msg_temporary",Body("email.bounced",bounce:"Temporary"))==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.None);
            Check("hard_bounce_suppresses",await Receive("msg_bounce",Body("email.bounced"))==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.Bounce);
            version=(await repository.FindAsync(active.Id,default))?.Version;
            Check("distinct_duplicate_bounce_does_not_increment_subscriber",await Receive("msg_bounce_again",Body("email.bounced"))==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Version==version && await db.ProviderEmailObservations.CountAsync()==1);
            Check("late_delivery_never_clears_bounce",await Receive("msg_delivery_late",Body("email.delivered",occurred:now.AddSeconds(-10)))==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Suppression==SendingSuppression.Bounce
                && (await db.ProviderEmailObservations.SingleAsync(x=>x.MessageId==messageId)).DeliveredUtc==now.AddSeconds(-10));
            Check("complaint_adds_flag_without_reactivation",await Receive("msg_complaint",Body("email.complained"))==WebhookResponse.Accepted
                && (await repository.FindAsync(active.Id,default))?.Suppression==(SendingSuppression.Bounce|SendingSuppression.Complaint));
            var state=await repository.FindAsync(active.Id,default) ?? throw new InvalidOperationException();
            await repository.TryApplyAsync(active.Id,state.Version,SubscriberChange.Unsubscribe,now.AddSeconds(1),default);
            state=await repository.FindAsync(active.Id,default) ?? throw new InvalidOperationException();
            await repository.TryApplyAsync(active.Id,state.Version,SubscriberChange.RequestReconfirmation,now.AddSeconds(2),default);
            Check("exit_reapplication_preserves_suppression",(await repository.FindAsync(active.Id,default))?.Suppression==(SendingSuppression.Bounce|SendingSuppression.Complaint));
            Check("unknown_address_event_persists_suppression",await Receive("msg_before_registration",Body("email.complained","later@example.test",Guid.NewGuid()))==WebhookResponse.Accepted);
            var later=await Subscriber("later@example.test");
            Check("first_registration_inherits_prior_suppression",later.Suppression==SendingSuppression.Complaint);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "InjectProviderFailure"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF NEW."AddressKey"='fault@example.test' THEN RAISE EXCEPTION 'Synthetic rollback'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER "INJECT_provider_failure" BEFORE INSERT ON "ProviderEmailObservations" FOR EACH ROW EXECUTE FUNCTION "InjectProviderFailure"();
                """);
            Check("failure_after_receipt_rolls_back_entire_event",await Receive("msg_fault",Body("email.bounced","fault@example.test",Guid.NewGuid()))==WebhookResponse.Unavailable
                && !await db.ProviderEmailEvents.AnyAsync(x=>x.EventId=="msg_fault") && !await db.ProviderAddressSuppressions.AnyAsync(x=>x.AddressKey=="fault@example.test"));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"INJECT_provider_failure\" ON \"ProviderEmailObservations\"; DROP FUNCTION \"InjectProviderFailure\"();");
            var sourceFault = await Subscriber("source-fault@example.test");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "InjectSourceFailure"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF NEW."AddressKey"='source-fault@example.test' AND NEW."Suppression"<>OLD."Suppression" THEN RAISE EXCEPTION 'Synthetic source rollback'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER "INJECT_source_failure" BEFORE UPDATE ON "Subscribers" FOR EACH ROW EXECUTE FUNCTION "InjectSourceFailure"();
                """);
            Check("source_failure_rolls_back_receipt_fact_and_ledger",await Receive("msg_source_fault",Body("email.bounced","source-fault@example.test",Guid.NewGuid()))==WebhookResponse.Unavailable
                && !await db.ProviderEmailEvents.AnyAsync(x=>x.EventId=="msg_source_fault")
                && !await db.ProviderEmailObservations.AnyAsync(x=>x.AddressKey=="source-fault@example.test")
                && !await db.ProviderAddressSuppressions.AnyAsync(x=>x.AddressKey=="source-fault@example.test")
                && (await repository.FindAsync(sourceFault.Id,default))?.Suppression==SendingSuppression.None);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"INJECT_source_failure\" ON \"Subscribers\"; DROP FUNCTION \"InjectSourceFailure\"();");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "InjectProviderSleep"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF NEW."AddressKey"='cancel@example.test' THEN PERFORM pg_sleep(4); END IF; RETURN NEW; END $$;
                CREATE TRIGGER "INJECT_provider_sleep" BEFORE INSERT ON "ProviderEmailObservations" FOR EACH ROW EXECUTE FUNCTION "InjectProviderSleep"();
                """);
            using (var cancel=new CancellationTokenSource())
            {
                await using var separate=new ApplicationDbContext(options);
                var cancelService=new ResendWebhookService(webhookOptions,new ProviderEmailEventStore(separate),TimeProvider.System);
                var rawCancel=Body("email.bounced","cancel@example.test",Guid.NewGuid());var time=DateTimeOffset.UtcNow;
                var pending=cancelService.ReceiveAsync(rawCancel,"msg_cancel",time.ToUnixTimeSeconds().ToString(),signer.Sign("msg_cancel",time,rawCancel),cancel.Token);
                var observed=false;
                for (var i=0;i<100;i++)
                {
                    observed=await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event='PgSleep'").SingleAsync()>0;
                    if (observed) break;await Task.Delay(20);
                }
                cancel.Cancel();var cancelled=false;try { await pending; }catch(OperationCanceledException) { cancelled=true; }
                Check("actual_pg_sleep_caller_cancellation_rolls_back",observed && cancelled && !await db.ProviderEmailEvents.AnyAsync(x=>x.EventId=="msg_cancel")
                    && !await db.ProviderAddressSuppressions.AnyAsync(x=>x.AddressKey=="cancel@example.test"));
            }
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"INJECT_provider_sleep\" ON \"ProviderEmailObservations\"; DROP FUNCTION \"InjectProviderSleep\"();");
            var parallelId=Guid.NewGuid(); var parallelBody=Body("email.bounced","parallel@example.test",parallelId);
            async Task<WebhookResponse> Parallel(int i)
            {
                await using var separate=new ApplicationDbContext(options);
                var separateService=new ResendWebhookService(webhookOptions,new ProviderEmailEventStore(separate),TimeProvider.System);
                var time=DateTimeOffset.UtcNow;return await separateService.ReceiveAsync(parallelBody,"msg_parallel",time.ToUnixTimeSeconds().ToString(),signer.Sign("msg_parallel",time,parallelBody),default);
            }
            Check("twelve_parallel_replays_one_receipt_one_fact",(await Task.WhenAll(Enumerable.Range(0,12).Select(Parallel))).All(x=>x==WebhookResponse.Accepted)
                && await db.ProviderEmailEvents.CountAsync(x=>x.EventId=="msg_parallel")==1 && await db.ProviderEmailObservations.CountAsync(x=>x.MessageId==parallelId)==1);
            // Registration and event race uses separate contexts, same advisory address lock and real PostgreSQL commits.
            async Task RegisterRace()
            {
                await using var separate=new ApplicationDbContext(options);SubscriberAddress.TryCreate("race@example.test",out var address);
                await new SubscriberRepository(separate).GetOrCreatePendingAsync(address??throw new InvalidOperationException(),now,default);
            }
            var race=Receive("msg_race",Body("email.complained","race@example.test",Guid.NewGuid()));
            await Task.WhenAll(race,RegisterRace());
            Check("registration_event_race_cannot_lose_suppression",await race==WebhookResponse.Accepted
                && (await db.Subscribers.SingleAsync(x=>x.AddressKey=="race@example.test")).Suppression==SendingSuppression.Complaint);
            async Task<bool> Reject(string sql)
            {
                await using var separate=new ApplicationDbContext(options);await using var tx=await separate.Database.BeginTransactionAsync();
                try { await separate.Database.ExecuteSqlRawAsync(sql);await tx.CommitAsync();return false; }
                catch (PostgresException e) when (e.SqlState=="23514") { return true; }
            }
            Check("raw_receipt_update_and_delete_rejected",await Reject("UPDATE \"ProviderEmailEvents\" SET \"PayloadHash\"=repeat('A',64)") && await Reject("DELETE FROM \"ProviderEmailEvents\""));
            Check("raw_observation_clear_and_delete_rejected",await Reject("UPDATE \"ProviderEmailObservations\" SET \"DeliveredUtc\"=NULL WHERE \"DeliveredUtc\" IS NOT NULL") && await Reject("DELETE FROM \"ProviderEmailObservations\""));
            Check("raw_ledger_and_subscriber_suppression_clear_rejected",await Reject("UPDATE \"ProviderAddressSuppressions\" SET \"Flags\"=1 WHERE \"Flags\"=3")
                && await Reject("UPDATE \"Subscribers\" SET \"Suppression\"=0 WHERE \"AddressKey\"='events@example.test'"));
            Check("orphan_receipt_commit_rejected",await Reject("""
                INSERT INTO "ProviderEmailEvents" ("EventId","PayloadHash","MessageId","AddressKey","Kind","OccurredUtc","ReceivedUtc")
                VALUES ('msg_orphan',repeat('A',64),gen_random_uuid(),'orphan@example.test',0,now(),now())
                """));
            await using (var tx=await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Subscribers\" DISABLE TRIGGER \"TR_Subscribers_ProviderSuppression\"");
                var rows=await db.Database.ExecuteSqlRawAsync("UPDATE \"Subscribers\" SET \"Suppression\"=0 WHERE \"AddressKey\"='events@example.test'");
                Check("negative_control_removed_guard_allows_bad_update",rows==1);
                await tx.RollbackAsync();
            }
            Check("negative_control_rollback_restores_guard_and_flags",await Reject("UPDATE \"Subscribers\" SET \"Suppression\"=0 WHERE \"AddressKey\"='events@example.test'")
                && (await repository.FindAsync(active.Id,default))?.Suppression==(SendingSuppression.Bounce|SendingSuppression.Complaint));
            Check("no_mail_jobs_campaign_mutation_or_tracked_graph",await db.TransactionalEmailJobs.CountAsync()==0 && db.ChangeTracker.Entries().All(x=>x.State==EntityState.Unchanged));
            Check("missing_or_malformed_config_fail_closed",!new ResendWebhookOptions(true,"invalid","onboarding@resend.dev").IsConfigured && !new ResendWebhookOptions(false,Secret,"onboarding@resend.dev").IsConfigured);
            return Report();
        }
        catch (Exception e)
        {
            Console.WriteLine(JsonSerializer.Serialize(new {passed=false,exceptionType=e.GetType().Name,sqlState=(e as PostgresException)?.SqlState,lastCheck=last}));return 1;
        }
        int Report() { Console.WriteLine(JsonSerializer.Serialize(new {passed=checks.Values.All(x=>x),count=checks.Count,checks}));return checks.Values.All(x=>x)?0:1; }
    }
}
