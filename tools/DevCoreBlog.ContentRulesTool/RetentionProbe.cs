using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Retention;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Retention;
using DevCoreBlog.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Real PostgreSQL acceptance with synthetic rows; never calls an email transport or reads dotenv.</summary>
internal static class RetentionProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool ok) { checks[name] = ok; if (!ok) throw new InvalidOperationException("Retention assertion failed."); }
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new ApplicationDbContext(options);
        async Task<int> Sql(FormattableString sql) => await db.Database.ExecuteSqlInterpolatedAsync(sql);
        async Task<bool> Rejected(FormattableString sql)
        {
            try { await Sql(sql); return false; } catch (PostgresException ex) when (ex.SqlState == "23514") { return true; }
        }
        async Task<Guid> Subscriber(DateTime created, int suppression = 0)
        {
            var id = Guid.NewGuid(); var address = "retention-" + id.ToString("N") + "@example.test";
            await Sql($"""
                INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version")
                VALUES ({id},{address},{address},0,{suppression},{created},{created},1)
                """); return id;
        }
        async Task<Guid> Job(Guid subscriber, DateTime created, int state = 6)
        {
            var id = Guid.NewGuid(); var expiry = created.AddDays(1);
            await Sql($"""
                INSERT INTO "TransactionalEmailJobs" ("Id","SubscriberId","SubscriberVersion","ProtectedPayload","PayloadHash","CreatedUtc","UpdatedUtc","ExpiresUtc","DueUtc","State","Problem","AttemptCount")
                VALUES ({id},{subscriber},1,'QUJD',{new string('A',64)},{created},{expiry},{expiry},{created},{state},0,0)
                """); return id;
        }
        async Task<Guid> Token(Guid subscriber, DateTime expiry, bool login = false, bool consumed = false)
        {
            var created = login ? expiry.AddMinutes(-15) : expiry.AddDays(-1);
            var id = await Job(subscriber, created);
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(id.ToByteArray()));
            DateTime? used = consumed ? created.AddMinutes(1) : null;
            if (login)
                await Sql($"""
                    INSERT INTO "ReaderLoginTokens" ("Id","SubscriberId","SubscriberVersion","PeriodId","Digest","AddressBinding","CreatedUtc","ExpiresUtc","ConsumedUtc")
                    SELECT {id},{subscriber},1,"PeriodId",{digest},{new string('B',64)},{created},{expiry},{used} FROM "Subscribers" WHERE "Id"={subscriber}
                    """);
            else
                await Sql($"""
                    INSERT INTO "SubscriberVerificationTokens" ("Id","SubscriberId","SubscriberVersion","Purpose","Digest","AddressBinding","CreatedUtc","ExpiresUtc","ConsumedUtc")
                    VALUES ({id},{subscriber},1,1,{digest},{new string('B',64)},{created},{expiry},{used})
                    """);
            return id;
        }
        async Task<Guid> Session(Guid subscriber, DateTime expiry)
        {
            var id = Guid.NewGuid(); var created = expiry.AddDays(-7);
            await Sql($"""
                INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc")
                SELECT {id},{subscriber},1,"PeriodId",{created},{expiry} FROM "Subscribers" WHERE "Id"={subscriber}
                """); return id;
        }
        async Task<string> Snapshot() => string.Join("|", await db.Database.SqlQueryRaw<string>("""
            SELECT md5(coalesce(string_agg(value,'|' ORDER BY value),'')) AS "Value" FROM (
             SELECT to_jsonb(t)::text value FROM "SubscriberVerificationTokens" t UNION ALL SELECT to_jsonb(t)::text FROM "ReaderLoginTokens" t
             UNION ALL SELECT to_jsonb(t)::text FROM "ReaderSessions" t UNION ALL SELECT to_jsonb(t)::text FROM "TransactionalEmailJobs" t
             UNION ALL SELECT to_jsonb(t)::text FROM "ProviderEmailEvents" t UNION ALL SELECT to_jsonb(t)::text FROM "ProviderEmailReceipts" t
             UNION ALL SELECT to_jsonb(t)::text FROM "ProviderEmailObservations" t UNION ALL SELECT to_jsonb(t)::text FROM "Subscribers" t
             UNION ALL SELECT to_jsonb(t)::text FROM "ProviderAddressSuppressions" t UNION ALL SELECT to_jsonb(t)::text FROM "RetentionCheckpoints" t) q
            """).ToListAsync());
        try
        {
            var subscriber = await Subscriber(now.AddDays(-50));
            var old = await Token(subscriber, now.AddDays(-7).AddTicks(-10));
            var boundary = await Token(subscriber, now.AddDays(-7));
            var recent = await Token(subscriber, now.AddDays(-6));
            var consumed = await Token(subscriber, now.AddDays(-8), consumed: true);
            var login = await Token(subscriber, now.AddDays(-8), login: true);
            var oldSession = await Session(subscriber, now.AddDays(-8));
            var exactSession = await Session(subscriber, now.AddDays(-7));
            var activeSession = await Session(subscriber, now.AddDays(1));
            var orphan = await Subscriber(now.AddDays(-8)); var exactPending = await Subscriber(now.AddDays(-7));
            var suppressed = await Subscriber(now.AddDays(-50), 4);
            var held = await Job(subscriber, now.AddDays(-40), 5);
            var activeJob = await Job(subscriber, now, 0);
            var payloadBoundary = await Job(subscriber, now.AddDays(-2));
            var payloadOld = await Job(subscriber, now.AddDays(-2).AddTicks(-10));
            var accepted = Guid.NewGuid(); var providerId = Guid.NewGuid(); var acceptedCreated = now.AddDays(-4); var acceptedEnd = now.AddDays(-3);
            await Sql($"""
                INSERT INTO "TransactionalEmailJobs" ("Id","SubscriberId","SubscriberVersion","ProtectedPayload","PayloadHash","CreatedUtc","UpdatedUtc","ExpiresUtc","DueUtc","State","Problem","AttemptCount","FirstAttemptUtc","ProviderMessageId")
                VALUES ({accepted},{subscriber},1,'QUJD',{new string('A',64)},{acceptedCreated},{acceptedEnd},{acceptedEnd},{acceptedCreated},3,0,1,{acceptedCreated},{providerId})
                """);
            await Sql($"""
                INSERT INTO "TransactionalEmailAttempts" ("JobId","Number","LeaseId","StartedUtc","CompletedUtc","Outcome","Reason","HttpStatus","ProviderMessageId")
                VALUES ({accepted},1,{Guid.NewGuid()},{acceptedCreated},{acceptedEnd},0,0,200,{providerId})
                """);
            var eventId = "msg_retention_" + Guid.NewGuid().ToString("N"); var message = Guid.NewGuid();
            var evt = ProviderEmailEvent.Create(eventId, new string('C',64), message, "retention-provider@example.test", ProviderEmailEventKind.Complained, now.AddDays(-92), now.AddDays(-91));
            Check("initial_provider_atomic_receipt", await new ProviderEmailEventStore(db).ApplyAsync(evt, default) == ProviderEventOutcome.Applied);
            var edgeId = "msg_edge_" + Guid.NewGuid().ToString("N");
            var edgeEvent = ProviderEmailEvent.Create(edgeId,new string('A',64),Guid.NewGuid(),"retention-edge@example.test",ProviderEmailEventKind.Delivered,now.AddDays(-90),now.AddDays(-90));
            Check("provider_exact_90_day_edge_seeded",await new ProviderEmailEventStore(db).ApplyAsync(edgeEvent,default)==ProviderEventOutcome.Applied);
            var sourceBefore = await Snapshot();
            var preview = await new RetentionStore(db).RunAsync(now, true, default);
            Check("preview_is_read_only", sourceBefore == await Snapshot());
            Check("preview_strict_edges_and_exception_counts", preview.DryRun && !preview.ClockRejected && preview.Counts.VerificationTokens == 2 && preview.Counts.LoginTokens == 1
                && preview.Counts.Sessions == 1 && preview.Counts.OrphanPending == 1 && preview.Counts.ProviderDetails == 1 && preview.Counts.HeldReviewDue == 1 && preview.Counts.PendingHistoryExceptions >= 1);
            // A real statement failure after token mutations must roll the whole pass and checkpoint back.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION retention_fixture_fault() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'owned fixture fault'; END $$;
                CREATE TRIGGER retention_fixture_fault BEFORE DELETE ON "ReaderSessions" FOR EACH ROW EXECUTE FUNCTION retention_fixture_fault();
                """);
            var failed = false;
            try { await new RetentionStore(db).RunAsync(now, false, default); } catch (PostgresException) { failed = true; }
            Check("mid_batch_failure_rolls_back_redactions_and_clock", failed && sourceBefore == await Snapshot());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER retention_fixture_fault ON \"ReaderSessions\"; DROP FUNCTION retention_fixture_fault();");
            var applied = await new RetentionStore(db).RunAsync(now, false, default);
            Check("apply_matches_preview_for_selected_classes", applied.Counts.VerificationTokens == 2 && applied.Counts.LoginTokens == 1 && applied.Counts.Sessions == 1 && applied.Counts.OrphanPending == 1 && applied.Counts.ProviderDetails == 1);
            Check("only_old_token_material_removed", await db.SubscriberVerificationTokens.Where(x => x.Id == old || x.Id == consumed).AllAsync(x => x.Digest == null && x.RedactedUtc == now)
                && await db.SubscriberVerificationTokens.Where(x => x.Id == boundary || x.Id == recent).AllAsync(x => x.Digest != null));
            Check("verification_evidence_preserved", await db.SubscriberVerificationTokens.AnyAsync(x => x.Id == consumed && x.ConsumedUtc != null && x.AddressBinding == new string('B',64)));
            Check("login_metadata_preserves_cooldown", await db.ReaderLoginTokens.AnyAsync(x => x.Id == login && x.Digest == null && x.CreatedUtc < now));
            Check("expired_session_only_removed", !await db.ReaderSessions.AnyAsync(x => x.Id == oldSession) && await db.ReaderSessions.CountAsync(x => x.Id == exactSession || x.Id == activeSession) == 2);
            Check("orphan_only_and_history_suppression_preserved", !await db.Subscribers.AnyAsync(x => x.Id == orphan) && await db.Subscribers.CountAsync(x => x.Id == exactPending || x.Id == subscriber || x.Id == suppressed) == 3);
            Check("payload_strict_24h_boundary", await db.TransactionalEmailJobs.AnyAsync(x => x.Id == payloadOld && x.ProtectedPayload == null && x.PayloadRedactedUtc == now)
                && await db.TransactionalEmailJobs.AnyAsync(x => x.Id == payloadBoundary && x.ProtectedPayload != null));
            Check("accepted_payload_removed_journal_and_provider_id_preserved",await db.TransactionalEmailJobs.AnyAsync(x=>x.Id==accepted && x.ProtectedPayload==null && x.ProviderMessageId==providerId)
                && await db.TransactionalEmailAttempts.AnyAsync(x=>x.JobId==accepted && x.Outcome==EmailSendOutcome.Accepted && x.ProviderMessageId==providerId));
            Check("provider_exact_90_day_boundary_retained",await db.ProviderEmailEvents.AnyAsync(x=>x.EventId==edgeId));
            Check("active_and_held_envelopes_preserved", await db.TransactionalEmailJobs.Where(x => x.Id == held || x.Id == activeJob).AllAsync(x => x.ProtectedPayload != null));
            Check("provider_details_removed_receipt_facts_and_suppression_retained", !await db.ProviderEmailEvents.AnyAsync(x => x.EventId == eventId)
                && await db.Set<ProviderEmailReceipt>().AnyAsync(x => x.EventId == eventId) && await db.ProviderEmailObservations.AnyAsync(x => x.MessageId == message && x.ComplainedUtc != null)
                && await db.ProviderAddressSuppressions.AnyAsync(x => x.AddressKey == "retention-provider@example.test"));
            Check("old_event_replay_does_not_recreate_detail", await new ProviderEmailEventStore(db).ApplyAsync(evt, default) == ProviderEventOutcome.Duplicate && !await db.ProviderEmailEvents.AnyAsync(x => x.EventId == eventId));
            var conflict = ProviderEmailEvent.Create(eventId, new string('D',64), message, evt.AddressKey, evt.Kind, evt.OccurredUtc, now);
            Check("old_receipt_conflict_survives_reduction", await new ProviderEmailEventStore(db).ApplyAsync(conflict, default) == ProviderEventOutcome.Conflict);
            var late = ProviderEmailEvent.Create("msg_late_"+Guid.NewGuid().ToString("N"), new string('E',64), message, evt.AddressKey, ProviderEmailEventKind.Delivered, now.AddDays(-93), now);
            Check("late_fact_updates_after_witness_reduction", await new ProviderEmailEventStore(db).ApplyAsync(late, default) == ProviderEventOutcome.Applied
                && await db.ProviderEmailObservations.AnyAsync(x => x.MessageId == message && x.DeliveredUtc == late.OccurredUtc && x.ComplainedUtc == evt.OccurredUtc));
            Check("redacted_payload_cannot_revive", await Rejected($"UPDATE \"TransactionalEmailJobs\" SET \"ProtectedPayload\"='QUJD',\"PayloadRedactedUtc\"=NULL WHERE \"Id\"={payloadOld}"));
            Check("closed_job_cannot_requeue", await Rejected($"UPDATE \"TransactionalEmailJobs\" SET \"State\"=0 WHERE \"Id\"={payloadOld}"));
            Check("redacted_digest_cannot_revive", await Rejected($"UPDATE \"SubscriberVerificationTokens\" SET \"Digest\"={new string('F',64)},\"RedactedUtc\"=NULL WHERE \"Id\"={old}"));
            Check("receipt_and_observation_delete_forbidden", await Rejected($"DELETE FROM \"ProviderEmailReceipts\" WHERE \"EventId\"={eventId}") && await Rejected($"DELETE FROM \"ProviderEmailObservations\" WHERE \"MessageId\"={message}"));
            var stable = await Snapshot();
            var back = await new RetentionStore(db).RunAsync(now.AddSeconds(-1), false, default);
            Check("backward_clock_after_restart_fails_closed", back.ClockRejected && stable == await Snapshot());
            var repeat = await new RetentionStore(db).RunAsync(now, false, default);
            Check("repeat_is_idempotent", repeat.Counts.VerificationTokens == 0 && repeat.Counts.LoginTokens == 0 && repeat.Counts.Payloads == 0 && repeat.Counts.Sessions == 0 && repeat.Counts.ProviderDetails == 0);
            for (var i = 0; i < 30; i++) await Token(subscriber, now.AddDays(-9));
            var cap = await new RetentionStore(db).RunAsync(now, true, default);
            Check("inspection_count_is_capped_sample", cap.Counts.VerificationTokens == RetentionPolicy.SampleLimit);
            var batch = await new RetentionStore(db).RunAsync(now, false, default);
            Check("mutation_has_25_row_class_limit", batch.Counts.VerificationTokens == RetentionPolicy.BatchSize);
            await using (var restarted = new ApplicationDbContext(options))
                Check("fresh_process_context_resumes_remaining_batch", (await new RetentionStore(restarted).RunAsync(now, false, default)).Counts.VerificationTokens == 5);
            var locked = await Token(subscriber, now.AddDays(-10));
            await using (var other = new ApplicationDbContext(options))
            await using (var lockTx = await other.Database.BeginTransactionAsync())
            {
                await other.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SubscriberVerificationTokens\" WHERE \"Id\"={locked} FOR UPDATE");
                Check("concurrent_locked_proof_is_skipped", (await new RetentionStore(db).RunAsync(now, false, default)).Counts.VerificationTokens == 0);
                await lockTx.RollbackAsync();
            }
            Check("skipped_proof_resumes_next_pass", (await new RetentionStore(db).RunAsync(now, false, default)).Counts.VerificationTokens == 1);
            await using (var other = new ApplicationDbContext(options))
            await using (var lockTx = await other.Database.BeginTransactionAsync())
            {
                await other.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(502026)");
                using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)); var stopped = false;
                try { await new RetentionStore(db).RunAsync(now, false, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
                Check("lock_wait_obeys_cancellation", stopped); await lockTx.RollbackAsync();
            }
            // Exercise independent real contexts against one cleanup pass, not merely sequential eligibility.
            async Task CleanupRace()
            {
                await using var own = new ApplicationDbContext(options);
                await new RetentionStore(own).RunAsync(now,false,default);
            }
            var orphanRace = await Subscriber(now.AddDays(-8));
            var raceAddress = (await db.Subscribers.AsNoTracking().SingleAsync(x=>x.Id==orphanRace)).AddressKey;
            if (!SubscriberAddress.TryCreate(raceAddress,out var address) || address is null) throw new InvalidOperationException();
            async Task RegisterRace()
            {
                await using var own = new ApplicationDbContext(options); var repo = new SubscriberRepository(own); var queue = new TransactionalEmailStore(own);
                var registration = new SubscriptionRegistrationStore(own,repo,new SubscriberVerificationStore(own,repo,queue));
                await registration.RegisterAsync(address,now,subscriberSnapshot=>
                {
                    var id=Guid.NewGuid();var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(id.ToByteArray()));
                    return new(new(id,subscriberSnapshot.Id,subscriberSnapshot.Version,digest,SubscriberVerificationRules.AddressBinding(subscriberSnapshot.AddressKey),now),
                        new(id,subscriberSnapshot.Id,subscriberSnapshot.Version,"QUJD",new string('A',64),now,now.AddDays(1)));
                },default);
            }
            await Task.WhenAll(CleanupRace(),RegisterRace());
            var registered=await db.Subscribers.AsNoTracking().SingleAsync(x=>x.AddressKey==raceAddress);
            Check("parallel_reapplication_and_orphan_cleanup_keep_one_complete_intent",registered.Status==SubscriptionStatus.PendingVerification
                && await db.TransactionalEmailJobs.CountAsync(x=>x.SubscriberId==registered.Id)==1 && await db.SubscriberVerificationTokens.CountAsync(x=>x.SubscriberId==registered.Id)==1);
            var exitSource=await db.Subscribers.AsNoTracking().SingleAsync(x=>x.Id==subscriber);
            async Task ExitRace()
            {
                await using var own=new ApplicationDbContext(options);
                var outcome=await new SubscriberUnsubscriptionStore(own,new SubscriberRepository(own)).UnsubscribeAsync(
                    new(subscriber,exitSource.PeriodId,SubscriberVerificationRules.AddressBinding(exitSource.AddressKey)),now,default);
                if(outcome!=UnsubscriptionOutcome.Unsubscribed)throw new InvalidOperationException();
            }
            await Task.WhenAll(CleanupRace(),ExitRace());
            Check("parallel_exit_and_cleanup_do_not_reopen_history",await db.Subscribers.AnyAsync(x=>x.Id==subscriber && x.Status==SubscriptionStatus.Unsubscribed));
            var lateComplaint=ProviderEmailEvent.Create("msg_race_"+Guid.NewGuid().ToString("N"),new string('F',64),message,evt.AddressKey,ProviderEmailEventKind.Complained,now.AddDays(-95),now);
            async Task CallbackRace()
            {
                await using var own=new ApplicationDbContext(options);
                if(await new ProviderEmailEventStore(own).ApplyAsync(lateComplaint,default)!=ProviderEventOutcome.Applied)throw new InvalidOperationException();
            }
            await Task.WhenAll(CleanupRace(),CallbackRace());
            Check("parallel_callback_preserves_earlier_fact_and_suppression",await db.ProviderEmailObservations.AnyAsync(x=>x.MessageId==message && x.ComplainedUtc==lateComplaint.OccurredUtc)
                && await db.ProviderAddressSuppressions.AnyAsync(x=>x.AddressKey==evt.AddressKey && (x.Flags & SendingSuppression.Complaint)!=0));
            TransactionalEmailLease? raceLease=null;
            async Task ClaimRace()
            {
                await using var own=new ApplicationDbContext(options);
                raceLease=await new TransactionalEmailStore(own).ClaimAsync(now,default);
            }
            await Task.WhenAll(CleanupRace(),ClaimRace());
            Check("parallel_worker_claim_keeps_pending_payload",raceLease is not null && await db.TransactionalEmailJobs.AnyAsync(x=>x.Id==raceLease.Id && x.State==TransactionalEmailState.Claimed && x.ProtectedPayload!=null));
            var proofRace=await Token(subscriber,now.AddDays(-8));
            var proofDigest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(proofRace.ToByteArray()));
            VerificationConsumeOutcome consumedRace=VerificationConsumeOutcome.Invalid;
            async Task ConsumeRace()
            {
                await using var own=new ApplicationDbContext(options);var repo=new SubscriberRepository(own);
                consumedRace=await new SubscriberVerificationStore(own,repo,new TransactionalEmailStore(own)).ConsumeAsync(proofDigest,1,now,default);
            }
            await Task.WhenAll(CleanupRace(),ConsumeRace());
            Check("parallel_expired_consume_and_redaction_cannot_grant_access",consumedRace==VerificationConsumeOutcome.Invalid
                && await db.SubscriberVerificationTokens.AnyAsync(x=>x.Id==proofRace && x.Digest==null && x.ConsumedUtc==null));
            var dstNow=new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc);
            var dstSource=await Subscriber(dstNow.AddDays(-9));var dstToken=await Token(dstSource,dstNow.AddDays(-7));
            await db.Database.OpenConnectionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Europe/Berlin'");
                await new RetentionStore(db).RunAsync(dstNow,false,default);
                Check("dst_session_exact_168_hours_retained",await db.SubscriberVerificationTokens.AnyAsync(x=>x.Id==dstToken && x.Digest!=null));
                await new RetentionStore(db).RunAsync(dstNow.AddTicks(10),false,default);
                Check("dst_session_one_microsecond_after_168_hours_redacted",await db.SubscriberVerificationTokens.AnyAsync(x=>x.Id==dstToken && x.Digest==null));
                Check("retention_timezone_setting_is_transaction_local",await db.Database.SqlQueryRaw<string>("SELECT current_setting('TimeZone') AS \"Value\"").SingleAsync()=="Europe/Berlin");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'UTC'");await db.Database.CloseConnectionAsync();
            }
            Check("checkpoint_delete_forbidden", await Rejected($"DELETE FROM \"RetentionCheckpoints\" WHERE \"Id\"=1"));
            Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Values.All(x => x), count = checks.Count, checks }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, count = checks.Count, checks, exceptionType = ex.GetType().Name, sqlState = (ex as PostgresException)?.SqlState }));
            return 1;
        }
    }
}
