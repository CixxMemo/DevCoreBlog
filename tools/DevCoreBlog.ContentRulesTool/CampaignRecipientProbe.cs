using System.Diagnostics;
using System.Text.Json;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Email;
using DevCoreBlog.Services.Newsletters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Owned real PostgreSQL cohort/concurrency/rollback acceptance; no transport or real credentials are used.</summary>
internal static class CampaignRecipientProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var checks = new Dictionary<string, bool>();
        try
        {
            await using var db = new ApplicationDbContext(options);
            var owned = Environment.GetEnvironmentVariable("RECIPIENT_TEST_ROOT") ?? "";
            var conn = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            await db.Database.OpenConnectionAsync();
            using var guard = db.Database.GetDbConnection().CreateCommand(); guard.CommandText = "SHOW data_directory";
            if (!owned.StartsWith("/tmp/devcoreblog-recipients-", StringComparison.Ordinal)
                || (string?)await guard.ExecuteScalarAsync() != owned + "/pg" || conn.Host != "127.0.0.1"
                || conn.Database?.StartsWith("recipients_", StringComparison.Ordinal) != true) return 2;
            var now = ReaderIdentityRules.Utc(DateTime.UtcNow);
            if (args.Contains("--recipients-resume"))
            {
                var campaign = Guid.Parse(Environment.GetEnvironmentVariable("RECIPIENT_TEST_CAMPAIGN") ?? "");
                now = DateTime.Parse(Environment.GetEnvironmentVariable("RECIPIENT_TEST_NOW") ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind);
                var store = new CampaignRecipientStore(db); RecipientBatchResult? last = null;
                for (var i = 0; i < 1000; i++)
                {
                    last = await store.AdvanceAsync(campaign, now, default);
                    if (last.Status == RecipientBatchStatus.Complete) break;
                    if (last.Status != RecipientBatchStatus.Advanced) break;
                }
                checks["restarted_process_finishes_existing_selection"] = last?.Status == RecipientBatchStatus.Complete;
                return Report(checks);
            }
            var category = new Category { Name = "Recipients fixture", Slug = "recipients-" + Guid.NewGuid().ToString("N"), CreatedDate = now, IsActive = true };
            db.Categories.Add(category); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var validator = new ContentDocumentValidator(); var text = new DocumentTextProducer();
            const string json = """{"version":1,"document":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Synthetic recipients"}]}]}}""";
            var validated = validator.Validate(json).Document ?? throw new InvalidOperationException(); var reading = text.Produce(validated);
            var source = new Post { Title = "Recipient test", Slug = "recipients-"+Guid.NewGuid().ToString("N"), CategoryId = category.Id,
                ContentKind = PostContentKind.Newsletter, AccessScope = PostAccessScope.Subscribers, Content = "Synthetic legacy",
                CreatedDate = now, IsActive = true, PublishDate = now.AddDays(1), DocumentVersion = 1, DocumentJson = json,
                DocumentPlainText = reading.PlainText, DocumentWordCount = reading.WordCount, DocumentReadingMinutes = reading.ReadingMinutes };
            db.Posts.Add(source); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var campaigns = new NewsletterCampaignStore(db);
            async Task<Guid> Campaign()
            {
                var service = new NewsletterCampaignService(campaigns, new ResendOptions("re_synthetic", "sender@example.test", "Synthetic"),
                    new NewsletterCampaignOrigin("https://aether.example.test"), validator, text, new DocumentEmailRenderer(text), new Clock(now));
                return (await service.PrepareAsync(source.Id, 1, Guid.NewGuid(), default)).Intent?.Id ?? throw new InvalidOperationException();
            }
            var id = await Campaign();
            if (args.Contains("--recipients-seed"))
            {
                SubscriberAddress.TryCreate("preserved@example.test", out var address);
                await new SubscriberRepository(db).GetOrCreatePendingAsync(address ?? throw new InvalidOperationException(), now, default);
                checks["previous_schema_campaign_and_subscriber_seeded"] = await db.NewsletterCampaigns.AnyAsync(x => x.Id == id);
                return Report(checks);
            }
            var baselineJobs = await db.TransactionalEmailJobs.CountAsync(); var baselineAttempts = await db.TransactionalEmailAttempts.CountAsync();
            var emptyId = await Campaign(); var emptyStore = new CampaignRecipientStore(db);
            var empty = await emptyStore.AdvanceAsync(emptyId, now, default);
            checks["no_eligible_subscribers_complete_with_real_zero"] = empty is { Status: RecipientBatchStatus.Complete, Progress.SelectedCount: 0 };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","UnsubscribedUtc","Version","PeriodId")
                SELECT ('10000000-0000-0000-0000-'||lpad(i::text,12,'0'))::uuid, 'recipient'||i||'@example.test','recipient'||i||'@example.test',
                    CASE WHEN i%5=0 THEN 0 WHEN i%5=1 THEN 2 ELSE 1 END, CASE WHEN i%5=2 THEN 1 ELSE 0 END,
                    {now.AddDays(-2)},{now.AddDays(-1)},CASE WHEN i%5=0 THEN NULL ELSE {now.AddDays(-1)} END,
                    CASE WHEN i%5=1 THEN {now.AddDays(-1)} ELSE NULL END, 1, gen_random_uuid() FROM generate_series(1,10003) i
                """);
            checks["completed_empty_selection_never_backfills_later_rows"] = (await emptyStore.AdvanceAsync(emptyId, now.AddMinutes(1), default)).Progress == empty.Progress;
            var expected = (await db.Subscribers.AsNoTracking().Where(s => s.Status == SubscriptionStatus.Active && s.VerifiedUtc != null
                && s.Suppression == SendingSuppression.None).Select(s => s.Id).ToListAsync()).ToHashSet();
            var store1 = new CampaignRecipientStore(db);
            checks["invalid_and_missing_do_not_create_selection"] = (await store1.AdvanceAsync(Guid.Empty, now, default)).Status == RecipientBatchStatus.Invalid
                && (await store1.AdvanceAsync(Guid.NewGuid(), now, default)).Status == RecipientBatchStatus.NotFound;
            var first = await store1.AdvanceAsync(id, now, default);
            checks["first_batch_is_bounded_and_atomic"] = first is { Status: RecipientBatchStatus.Advanced, Scanned: 250, Progress.ScannedCount: 250 }
                && first.Added == await db.NewsletterRecipients.CountAsync(x => x.CampaignId == id)
                && first.Progress.SelectedCount == first.Added && db.ChangeTracker.Entries().Count() == 0;
            var originalCampaign = await db.NewsletterCampaigns.AsNoTracking().Where(x => x.Id == id).Select(x => x.SnapshotHash).SingleAsync();
            var lifecycle = new SubscriberRepository(db);
            var selectedId = await db.NewsletterRecipients.Where(x => x.CampaignId == id).OrderBy(x => x.SubscriberId).Select(x => x.SubscriberId).FirstAsync();
            var old = await db.NewsletterRecipients.AsNoTracking().SingleAsync(x => x.CampaignId == id && x.SubscriberId == selectedId);
            var state = await lifecycle.FindAsync(selectedId, default) ?? throw new InvalidOperationException();
            await lifecycle.TryApplyAsync(state.Id, state.Version, SubscriberChange.Unsubscribe, now.AddSeconds(1), default);
            state = await lifecycle.FindAsync(state.Id, default) ?? throw new InvalidOperationException();
            await lifecycle.TryApplyAsync(state.Id, state.Version, SubscriberChange.RequestReconfirmation, now.AddSeconds(2), default);
            state = await lifecycle.FindAsync(state.Id, default) ?? throw new InvalidOperationException();
            await lifecycle.TryApplyAsync(state.Id, state.Version, SubscriberChange.ConfirmOwnership, now.AddSeconds(3), default);
            var changed = Guid.Parse("10000000-0000-0000-0000-000000008998");
            state = await lifecycle.FindAsync(changed, default) ?? throw new InvalidOperationException();
            await lifecycle.TryApplyAsync(changed, state.Version, SubscriberChange.SuppressComplaint, now.AddSeconds(1), default); expected.Remove(changed);
            SubscriberAddress.TryCreate("late@example.test", out var late);
            var lateSub = await lifecycle.GetOrCreatePendingAsync(late ?? throw new InvalidOperationException(), now.AddSeconds(1), default);
            await lifecycle.TryApplyAsync(lateSub.Id, lateSub.Version, SubscriberChange.ConfirmOwnership, now.AddSeconds(2), default);
            var pending = Guid.Parse("10000000-0000-0000-0000-000000009000");
            state = await lifecycle.FindAsync(pending, default) ?? throw new InvalidOperationException();
            await lifecycle.TryApplyAsync(pending, state.Version, SubscriberChange.ConfirmOwnership, now.AddSeconds(1), default);
            var child = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            child.ArgumentList.Add(typeof(CampaignRecipientProbe).Assembly.Location); child.ArgumentList.Add("--recipients-resume");
            child.Environment["RECIPIENT_TEST_CAMPAIGN"] = id.ToString(); child.Environment["RECIPIENT_TEST_NOW"] = now.AddMinutes(1).ToString("O");
            using (var process = Process.Start(child) ?? throw new InvalidOperationException())
            {
                try
                {
                    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await process.WaitForExitAsync(deadline.Token);
                    using var childReport = JsonDocument.Parse(await output); await error;
                    checks["actual_process_restart_resumes"] = process.ExitCode == 0 && childReport.RootElement.GetProperty("passed").GetBoolean();
                }
                finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            }
            var members = await db.NewsletterRecipients.AsNoTracking().Where(x => x.CampaignId == id).Select(x => x.SubscriberId).ToListAsync();
            var completed = await store1.ReadAsync(id, default) ?? throw new InvalidOperationException();
            checks["large_cohort_exact_no_pending_exited_or_suppressed"] = expected.SetEquals(members) && members.Count == expected.Count;
            checks["cutoff_excludes_late_registration_confirmation_and_changes"] = !members.Contains(lateSub.Id) && !members.Contains(pending) && !members.Contains(changed);
            state = await lifecycle.FindAsync(selectedId, default) ?? throw new InvalidOperationException();
            var persisted = await db.NewsletterRecipients.AsNoTracking().SingleAsync(x => x.CampaignId == id && x.SubscriberId == selectedId);
            checks["selected_history_not_rebound_to_new_subscription_period"] = persisted.PeriodId == old.PeriodId && persisted.SubscriberVersion == old.SubscriberVersion
                && persisted.PeriodId != state.PeriodId && persisted.SubscriberVersion != state.Version;
            checks["count_matches_rows_and_completion_replays_without_write"] = completed.SelectedCount == members.Count
                && (await store1.AdvanceAsync(id, now.AddMinutes(2), default)) is { Status: RecipientBatchStatus.Complete, Scanned: 0, Added: 0 }
                && completed == await store1.ReadAsync(id, default);
            checks["backwards_clock_rejected"] = (await store1.AdvanceAsync(id, now.AddTicks(-10), default)).Status == RecipientBatchStatus.InvalidTime;
            var parallelId = await Campaign();
            var parallel = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await using var own = new ApplicationDbContext(options);
                return await new CampaignRecipientStore(own).AdvanceAsync(parallelId, now.AddMinutes(3), default);
            }));
            var progress = await store1.ReadAsync(parallelId, default) ?? throw new InvalidOperationException();
            checks["twelve_parallel_batches_have_disjoint_monotonic_progress"] = parallel.All(x => x.Status == RecipientBatchStatus.Advanced && x.Scanned <= 250)
                && progress.ScannedCount == 3000 && progress.SelectedCount == await db.NewsletterRecipients.CountAsync(x => x.CampaignId == parallelId);
            while ((await store1.AdvanceAsync(parallelId, now.AddMinutes(3), default)).Status == RecipientBatchStatus.Advanced) { }
            checks["parallel_then_resume_has_no_duplicate_or_skipped_member"] = await db.NewsletterRecipients.CountAsync(x => x.CampaignId == parallelId)
                == await db.Subscribers.CountAsync(x => x.Status == SubscriptionStatus.Active && x.VerifiedUtc != null && x.Suppression == SendingSuppression.None);
            var cancelled = await Campaign();
            await store1.AdvanceAsync(cancelled, now.AddMinutes(4), default);
            await campaigns.TryCancelAsync(cancelled, 1, now.AddMinutes(4), default);
            var cancelBefore = await store1.ReadAsync(cancelled, default);
            checks["cancelled_partial_selection_never_advances"] = (await store1.AdvanceAsync(cancelled, now.AddMinutes(5), default)).Status == RecipientBatchStatus.Cancelled
                && cancelBefore == await store1.ReadAsync(cancelled, default);
            var never = await Campaign(); await campaigns.TryCancelAsync(never, 1, now.AddMinutes(4), default);
            checks["cancel_before_selection_creates_nothing"] = (await store1.AdvanceAsync(never, now.AddMinutes(5), default)).Status == RecipientBatchStatus.Cancelled
                && await store1.ReadAsync(never, default) is null;
            var faultId = await Campaign();
            var partialId = await Campaign(); await store1.AdvanceAsync(partialId, now.AddMinutes(6), default);
            var partialBefore = await store1.ReadAsync(partialId, default);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION recipient_fixture_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF (SELECT count(*) FROM "NewsletterRecipients" WHERE "CampaignId"=NEW."CampaignId" AND "SelectionVersion"=NEW."SelectionVersion")>=5 THEN
                        RAISE EXCEPTION 'Synthetic recipient failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER recipient_fixture_failure BEFORE INSERT ON "NewsletterRecipients" FOR EACH ROW EXECUTE FUNCTION recipient_fixture_failure();
                """);
            try
            {
                var service = new CampaignRecipientService(store1, new Clock(now.AddMinutes(6)));
                checks["failure_after_five_inserts_rolls_back_members_cursor_and_selection"] = (await service.AdvanceAsync(faultId, default)).Status == RecipientBatchStatus.Unavailable
                    && await store1.ReadAsync(faultId, default) is null && !await db.NewsletterRecipients.AnyAsync(x => x.CampaignId == faultId);
                checks["failed_next_batch_keeps_previous_committed_checkpoint"] = (await service.AdvanceAsync(partialId, default)).Status == RecipientBatchStatus.Unavailable
                    && partialBefore == await store1.ReadAsync(partialId, default)
                    && partialBefore?.SelectedCount == await db.NewsletterRecipients.CountAsync(x => x.CampaignId == partialId);
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER recipient_fixture_failure ON \"NewsletterRecipients\"; DROP FUNCTION recipient_fixture_failure()"); }
            checks["retry_after_rollback_starts_clean"] = (await store1.AdvanceAsync(faultId, now.AddMinutes(6), default)).Scanned == 250;
            async Task<bool> Rejected(FormattableString sql, string code = "23514")
            {
                await using var own = new ApplicationDbContext(options);
                try { await own.Database.ExecuteSqlInterpolatedAsync(sql); return false; }
                catch (PostgresException e) when (e.SqlState == code) { return true; }
            }
            checks["recipient_mutation_and_deletion_rejected"] = await Rejected($"UPDATE \"NewsletterRecipients\" SET \"SubscriberVersion\"=\"SubscriberVersion\"+1 WHERE \"CampaignId\"={id}")
                && await Rejected($"DELETE FROM \"NewsletterRecipients\" WHERE \"CampaignId\"={id}");
            checks["selection_cutoff_rewind_counter_and_deletion_rejected"] = await Rejected($"UPDATE \"NewsletterRecipientSelections\" SET \"CutoffUtc\"=\"CutoffUtc\"+interval '1 second' WHERE \"CampaignId\"={faultId}")
                && await Rejected($"UPDATE \"NewsletterRecipientSelections\" SET \"SelectedCount\"=\"SelectedCount\"+1,\"Version\"=\"Version\"+1 WHERE \"CampaignId\"={faultId}")
                && await Rejected($"DELETE FROM \"NewsletterRecipientSelections\" WHERE \"CampaignId\"={faultId}");
            var checkpoint = await store1.ReadAsync(faultId, default) ?? throw new InvalidOperationException();
            var nextCursor = Guid.Parse("10000000-0000-0000-0000-000000000251");
            checks["invented_count_without_batch_members_rejected"] = await Rejected($"UPDATE \"NewsletterRecipientSelections\" SET \"SelectedCount\"=\"SelectedCount\"+1,\"ScannedCount\"=\"ScannedCount\"+1,\"LastSubscriberId\"={nextCursor},\"Version\"=\"Version\"+1 WHERE \"CampaignId\"={faultId}");
            checks["cursor_rewind_rejected"] = await Rejected($"UPDATE \"NewsletterRecipientSelections\" SET \"LastSubscriberId\"={Guid.Parse("10000000-0000-0000-0000-000000000001")},\"ScannedCount\"=\"ScannedCount\"+1,\"Version\"=\"Version\"+1 WHERE \"CampaignId\"={faultId}");
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"NewsletterRecipients\" DISABLE TRIGGER \"TR_NewsletterRecipients_Binding\"");
                var rejected = false;
                try { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"NewsletterRecipients\" SELECT * FROM \"NewsletterRecipients\" WHERE \"CampaignId\"={id} LIMIT 1"); }
                catch (PostgresException e) when (e.SqlState == "23505") { rejected = true; }
                checks["unique_database_key_rejects_duplicate_even_without_binding_trigger"] = rejected;
                await tx.RollbackAsync();
            }
            async Task Init(Guid campaign, DateTime at)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "NewsletterRecipientSelections" ("CampaignId","CutoffUtc","UpperSubscriberId","LastSubscriberId","ScannedCount","SelectedCount","State","UpdatedUtc","Version")
                    VALUES ({campaign},{at},{Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")},NULL,0,0,0,{at},1)
                    """);
            }
            Task<int> Insert(Guid campaign, SubscriberSnapshot sub, DateTime at, long? revision = null, Guid? period = null) =>
                db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "NewsletterRecipients" ("CampaignId","SubscriberId","SubscriberVersion","SelectionVersion","PeriodId","EmailAddress","AddressKey","SelectedUtc")
                    VALUES ({campaign},{sub.Id},{revision ?? sub.Version},2,{period ?? sub.PeriodId},{sub.EmailAddress},{sub.AddressKey},{at})
                    """);
            var orphan = await Campaign(); var activeId = await db.Subscribers.Where(s => s.Status == SubscriptionStatus.Active && s.Suppression == SendingSuppression.None).Select(s => s.Id).FirstAsync();
            var eligible = await lifecycle.FindAsync(activeId, default) ?? throw new InvalidOperationException();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await Init(orphan, now.AddMinutes(7)); await Insert(orphan, eligible, now.AddMinutes(7));
                var rejected = false;
                try { await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS \"TR_NewsletterRecipients_BatchCommit\" IMMEDIATE"); }
                catch (PostgresException e) when (e.SqlState == "23514") { rejected = true; }
                checks["deferred_guard_rejects_members_without_progress_commit"] = rejected;
                await tx.RollbackAsync();
            }
            checks["orphan_transaction_leaves_no_selection_or_member"] = await store1.ReadAsync(orphan, default) is null && !await db.NewsletterRecipients.AnyAsync(x => x.CampaignId == orphan);
            var binding = await Campaign(); await Init(binding, now.AddMinutes(7));
            async Task<bool> InvalidInsert(SubscriberSnapshot sub, long? revision = null, Guid? period = null)
            {
                try { await Insert(binding, sub, now.AddMinutes(7), revision, period); return false; }
                catch (PostgresException e) when (e.SqlState == "23514") { return true; }
            }
            var pendingId = await db.Subscribers.Where(s => s.Status == SubscriptionStatus.PendingVerification).Select(s => s.Id).FirstAsync();
            var pendingSub = await lifecycle.FindAsync(pendingId, default) ?? throw new InvalidOperationException();
            checks["database_rejects_pending_wrong_revision_and_wrong_period"] = await InvalidInsert(pendingSub)
                && await InvalidInsert(eligible, eligible.Version + 1) && await InvalidInsert(eligible, period: Guid.NewGuid());
            var race = await Campaign();
            async Task RaceBatch() { await using var own = new ApplicationDbContext(options); await new CampaignRecipientStore(own).AdvanceAsync(race, now.AddMinutes(8), default); }
            async Task RaceCancel() { await using var own = new ApplicationDbContext(options); await new NewsletterCampaignStore(own).TryCancelAsync(race, 1, now.AddMinutes(8), default); }
            await Task.WhenAll(RaceBatch(), RaceCancel());
            var raceBefore = await store1.ReadAsync(race, default);
            checks["cancel_batch_race_has_no_post_cancel_advance"] = (await store1.AdvanceAsync(race, now.AddMinutes(9), default)).Status == RecipientBatchStatus.Cancelled
                && raceBefore == await store1.ReadAsync(race, default);
            var abortId = await Campaign();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION recipient_fixture_pause() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_sleep(4); RETURN NEW; END $$;
                CREATE TRIGGER recipient_fixture_pause BEFORE INSERT ON "NewsletterRecipients" FOR EACH ROW EXECUTE FUNCTION recipient_fixture_pause();
                """);
            try
            {
                using var abort = new CancellationTokenSource();
                var operation = new CampaignRecipientService(store1, new Clock(now.AddMinutes(10))).AdvanceAsync(abortId, abort.Token);
                await using var observer = new NpgsqlConnection(db.Database.GetConnectionString()); await observer.OpenAsync();
                var paused = false;
                for (var attempt = 0; attempt < 80 && !paused; attempt++)
                {
                    await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='PgSleep' AND pid<>pg_backend_pid())", observer);
                    paused = (bool)(await command.ExecuteScalarAsync() ?? false);
                    if (!paused) await Task.Delay(20);
                }
                abort.Cancel(); var cancelledByCaller = false;
                try { await operation; } catch (OperationCanceledException) { cancelledByCaller = true; }
                checks["caller_cancellation_during_actual_insert_rolls_back"] = paused && cancelledByCaller
                    && await store1.ReadAsync(abortId, default) is null && !await db.NewsletterRecipients.AnyAsync(x => x.CampaignId == abortId);
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER recipient_fixture_pause ON \"NewsletterRecipients\"; DROP FUNCTION recipient_fixture_pause()"); }
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"NewsletterRecipientSelections\" DISABLE TRIGGER \"TR_RecipientSelection_Progress\"");
                checks["negative_control_detects_missing_progress_guard"] = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"NewsletterRecipientSelections\" SET \"SelectedCount\"=\"SelectedCount\"+1 WHERE \"CampaignId\"={id}") == 1;
                await tx.RollbackAsync();
            }
            checks["negative_control_rollback_restores_exact_count"] = completed == await store1.ReadAsync(id, default);
            checks["source_snapshot_and_transactional_email_are_untouched"] = originalCampaign == await db.NewsletterCampaigns.Where(x => x.Id == id).Select(x => x.SnapshotHash).SingleAsync()
                && baselineJobs == await db.TransactionalEmailJobs.CountAsync() && baselineAttempts == await db.TransactionalEmailAttempts.CountAsync()
                && !await db.Posts.Where(x => x.Id == source.Id).Select(x => x.IsPublished).SingleAsync();
            return Report(checks);
        }
        catch (Exception e)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, exceptionType = e.GetType().Name, sqlState = (e as PostgresException)?.SqlState, lastCheck = checks.LastOrDefault().Key })); return 1;
        }
    }
    private static int Report(Dictionary<string, bool> checks)
    {
        var passed = checks.Count > 0 && checks.Values.All(x => x);
        Console.WriteLine(JsonSerializer.Serialize(new { passed, count = checks.Count, checks })); return passed ? 0 : 1;
    }
    private sealed class Clock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
}
