using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Email;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Owned real PostgreSQL admission/result/concurrency acceptance, with explicitly synthetic transport faults.</summary>
internal static class TestEmailSubmissionProbe
{
    private const string Doc = """{"version":1,"document":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Synthetic Türkçe 👋 <script> yalnız metin"}]}]}}""";
    private const string Subject = "Deneme 👋";
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new AssertionFailure(name); }
        try
        {
            await using var db = new ApplicationDbContext(options);
            var root = Environment.GetEnvironmentVariable("TEST_EMAIL_ROOT") ?? "";
            var conn = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            await db.Database.OpenConnectionAsync(); using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SHOW data_directory";
            if (!root.StartsWith("/tmp/devcoreblog-test-email-", StringComparison.Ordinal) || (string?)await command.ExecuteScalarAsync() != root + "/pg"
                || conn.Host != "127.0.0.1" || conn.Database?.StartsWith("test_email_", StringComparison.Ordinal) != true) return 2;
            var clock = new Clock(ReaderIdentityRules.Utc(DateTime.UtcNow));
            if (Environment.GetEnvironmentVariable("TEST_EMAIL_NOW") is { } now) clock.Now = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var config = new ResendOptions("re_synthetic", "sender@example.test", "Synthetic");
            var target = "operator@example.test";
            TestEmailSubmissionService Service(ApplicationDbContext context, Sender sender, bool enabled = true, string? recipient = null, ResendOptions? transport = null)
            {
                var settings = new TestEmailOptions(enabled, recipient ?? target, "https://aether.example.test"); var text = new DocumentTextProducer();
                return new(new(new ContentDocumentValidator(), new DocumentEmailRenderer(text), settings), settings, transport ?? config,
                    new TestEmailSubmissionStore(context), sender, clock);
            }
            async Task<TestEmailSubmission> Row(Guid id) => await db.TestEmailSubmissions.AsNoTracking().SingleAsync(x => x.Id == id);
            void Window() => clock.Now += TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1);
            async Task<TestEmailResult> Submit(Sender sender, Guid id, string? doc = Doc, string? subject = Subject) => await Service(db, sender).SubmitAsync(id, doc, subject, default);
            if (args.Contains("--test-email-seed"))
            {
                SubscriberAddress.TryCreate("preserved@example.test", out var address);
                await new SubscriberRepository(db).GetOrCreatePendingAsync(address ?? throw new InvalidOperationException(), clock.Now, default);
                Check("previous_schema_subscriber_seeded", true); return Report(checks);
            }
            if (args.Contains("--test-email-resume"))
            {
                var id = Guid.Parse(Environment.GetEnvironmentVariable("TEST_EMAIL_REQUEST_ID") ?? ""); var resumedSender = new Sender();
                var result = await Submit(resumedSender, id);
                Check("actual_restarted_process_replays_without_transport", result.Status == TestEmailResultStatus.Uncertain && result.Replayed && resumedSender.Requests.Count == 0);
                return Report(checks);
            }
            var posts = await db.Posts.CountAsync(); var subscribers = await db.Subscribers.CountAsync(); var campaigns = await db.NewsletterCampaigns.CountAsync(); var jobs = await db.TransactionalEmailJobs.CountAsync();
            var spy = new Sender();
            Check("disabled_missing_recipient_and_missing_transport_fail_closed", (await Service(db, spy, false).SubmitAsync(Guid.NewGuid(), Doc, Subject, default)).Status == TestEmailResultStatus.Disabled
                && (await Service(db, spy, recipient: "invalid").SubmitAsync(Guid.NewGuid(), Doc, Subject, default)).Status == TestEmailResultStatus.Disabled
                && (await Service(db, spy, transport: new(null, null, null)).SubmitAsync(Guid.NewGuid(), Doc, Subject, default)).Status == TestEmailResultStatus.Disabled && spy.Requests.Count == 0);
            Check("invalid_document_subject_id_and_limits_never_reserve", (await Submit(spy, Guid.NewGuid(), "{")).Status == TestEmailResultStatus.Invalid
                && (await Submit(spy, Guid.NewGuid(), subject: "bad\r\nsubject")).Status == TestEmailResultStatus.Invalid
                && (await Submit(spy, Guid.Empty)).Status == TestEmailResultStatus.Invalid
                && (await Submit(spy, Guid.NewGuid(), new string('x', 1_048_577))).Status == TestEmailResultStatus.TooLarge
                && !await db.TestEmailSubmissions.AnyAsync() && spy.Requests.Count == 0);
            var intent = Guid.NewGuid(); var sender = new Sender();
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            { await using var own = new ApplicationDbContext(options); return await Service(own, sender).SubmitAsync(intent, Doc, Subject, default); }));
            Check("twelve_parallel_replays_make_one_durable_start_and_transport_call", sender.Requests.Count == 1 && await db.TestEmailSubmissions.CountAsync() == 1
                && concurrent.All(x => x.Status is TestEmailResultStatus.Accepted or TestEmailResultStatus.InProgress));
            var packet = sender.Requests.Single();
            Check("fixed_recipient_safe_renderer_test_label_and_no_newsletter_headers", packet.Recipient == target && packet.Subject == "[TEST] " + Subject
                && packet.IdempotencyKey == "test/" + intent.ToString("N") && packet.Purpose == EmailPurpose.Transactional && packet.Unsubscribe is null
                && packet.Html.Contains("&lt;script&gt;", StringComparison.Ordinal) && !packet.Html.Contains("<script>", StringComparison.Ordinal)
                && packet.Text.Contains("Bu bir test", StringComparison.Ordinal));
            Check("acceptance_and_refresh_are_truthful_and_idempotent", (await Row(intent)).Outcome == EmailSendOutcome.Accepted && (await Row(intent)).ProviderMessageId is not null
                && (await Submit(sender, intent)) is { Status: TestEmailResultStatus.Accepted, Replayed: true } && sender.Requests.Count == 1);
            Check("same_id_changed_content_or_configuration_is_conflict", (await Submit(sender, intent, subject: "Changed")).Status == TestEmailResultStatus.Conflict
                && (await Service(db, sender, recipient: "other@example.test").SubmitAsync(intent, Doc, Subject, default)).Status == TestEmailResultStatus.Conflict && sender.Requests.Count == 1);
            var others = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            { await using var own = new ApplicationDbContext(options); return await Service(own, sender).SubmitAsync(Guid.NewGuid(), Doc, Subject, default); }));
            Check("different_intents_share_persistent_one_minute_cooldown", others.All(x => x.Status == TestEmailResultStatus.Limited && x.RetryAfter > TimeSpan.Zero) && sender.Requests.Count == 1);
            for (var i = 0; i < 2; i++) { clock.Now += TimeSpan.FromMinutes(1); Check("next_minute_test_" + i, (await Submit(sender, Guid.NewGuid())).Status == TestEmailResultStatus.Accepted); }
            clock.Now += TimeSpan.FromMinutes(1);
            Check("rolling_three_test_budget_is_persistent_and_cross_recipient", (await Submit(sender, Guid.NewGuid())).Status == TestEmailResultStatus.Limited
                && (await Service(db, sender, recipient: "other@example.test").SubmitAsync(Guid.NewGuid(), Doc, Subject, default)).Status == TestEmailResultStatus.Limited
                && sender.Requests.Count == 3 && await db.TestEmailSubmissions.CountAsync() == 3);
            clock.Now -= TimeSpan.FromHours(1); Check("backwards_clock_cannot_bypass_budget", (await Submit(sender, Guid.NewGuid())).Status == TestEmailResultStatus.Unavailable); clock.Now += TimeSpan.FromHours(1); Window();
            Check("rolling_budget_recovers_after_old_window", (await Submit(sender, Guid.NewGuid())).Status == TestEmailResultStatus.Accepted);
            foreach (var result in new[] { new EmailSendResult(EmailSendOutcome.Uncertain, EmailSendReason.Timeout), new(EmailSendOutcome.Retryable, EmailSendReason.RateLimit, HttpStatus: 429),
                new(EmailSendOutcome.Rejected, EmailSendReason.Validation, HttpStatus: 422), new(EmailSendOutcome.Accepted, EmailSendReason.None) })
            {
                Window(); var single = new Sender { Result = result }; var id = Guid.NewGuid(); var first = await Submit(single, id); var replay = await Submit(single, id);
                var expected = result.Outcome is EmailSendOutcome.Uncertain or EmailSendOutcome.Accepted ? TestEmailResultStatus.Uncertain : TestEmailResultStatus.Rejected;
                Check("provider_" + result.Outcome + "_never_auto_retries_or_fakes_success", first.Status == expected && replay.Status == expected && replay.Replayed
                    && single.Requests.Count == 1 && (await Row(id)).CompletedUtc is not null);
            }
            Window(); var cancelledSender = new Sender { Cancel = true }; var cancelledId = Guid.NewGuid();
            Check("cancel_after_start_persists_uncertain_independently", (await Submit(cancelledSender, cancelledId)).Status == TestEmailResultStatus.Uncertain
                && (await Row(cancelledId)).Reason == EmailSendReason.Cancelled && (await Submit(cancelledSender, cancelledId)).Replayed && cancelledSender.Requests.Count == 1);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var stopped = false;
                try { await Service(db, spy).SubmitAsync(Guid.NewGuid(), Doc, Subject, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
                Check("cancel_before_start_performs_no_transport", stopped && spy.Requests.Count == 0);
            }
            Window(); var uncertainId = Guid.NewGuid(); var accepted = new Sender();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION test_email_finish_fault() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic finish fault' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER test_email_finish_fault BEFORE UPDATE ON "TestEmailSubmissions" FOR EACH ROW EXECUTE FUNCTION test_email_finish_fault();
                """);
            try { Check("provider_acceptance_db_fault_is_uncertain_not_success", (await Submit(accepted, uncertainId)).Status == TestEmailResultStatus.Uncertain && accepted.Requests.Count == 1 && (await Row(uncertainId)).Outcome is null); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_email_finish_fault ON \"TestEmailSubmissions\"; DROP FUNCTION test_email_finish_fault()"); }
            clock.Now += TimeSpan.FromMinutes(2);
            var child = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            child.ArgumentList.Add(typeof(TestEmailSubmissionProbe).Assembly.Location); child.ArgumentList.Add("--test-email-resume");
            child.Environment["TEST_EMAIL_REQUEST_ID"] = uncertainId.ToString(); child.Environment["TEST_EMAIL_NOW"] = clock.Now.ToString("O");
            using (var process = Process.Start(child) ?? throw new InvalidOperationException())
            {
                try { var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await process.WaitForExitAsync(timeout.Token); using var parsed = JsonDocument.Parse(await output); await error;
                    Check("actual_restart_does_not_replay_incomplete_start", process.ExitCode == 0 && parsed.RootElement.GetProperty("passed").GetBoolean()); }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            }
            var late = new TestEmailSubmissionStore(db);
            Check("expired_completion_cannot_invent_late_success", !await late.CompleteAsync(uncertainId, (await Row(uncertainId)).InputHash,
                new(EmailSendOutcome.Accepted, EmailSendReason.None, Guid.NewGuid(), 200), clock.Now, default));
            foreach (var change in new[] { SubscriberChange.Unsubscribe, SubscriberChange.SuppressBounce, SubscriberChange.SuppressComplaint, SubscriberChange.SuppressManual })
            {
                Window(); target = "target" + Guid.NewGuid().ToString("N") + "@example.test"; SubscriberAddress.TryCreate(target, out var address);
                var repo = new SubscriberRepository(db); var sub = await repo.GetOrCreatePendingAsync(address ?? throw new InvalidOperationException(), clock.Now, default);
                await repo.TryApplyAsync(sub.Id, sub.Version, change, clock.Now, default); var before = await db.TestEmailSubmissions.CountAsync(); var blocked = new Sender();
                Check("current_" + change + "_blocks_test_without_consuming_budget", (await Submit(blocked, Guid.NewGuid())).Status == TestEmailResultStatus.Suppressed
                    && blocked.Requests.Count == 0 && before == await db.TestEmailSubmissions.CountAsync());
            }
            Window(); target = "ledger" + Guid.NewGuid().ToString("N") + "@example.test";
            await new ProviderEmailEventStore(db).ApplyAsync(ProviderEmailEvent.Create("evt_" + Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid(), target,
                ProviderEmailEventKind.Bounced, clock.Now, clock.Now), default);
            Check("provider_ledger_without_subscriber_also_blocks_test", (await Submit(spy, Guid.NewGuid())).Status == TestEmailResultStatus.Suppressed && spy.Requests.Count == 0);
            async Task<bool> Rejected(FormattableString sql)
            { await using var own = new ApplicationDbContext(options); try { await own.Database.ExecuteSqlInterpolatedAsync(sql); return false; } catch (PostgresException e) when (e.SqlState == "23514") { return true; } }
            Check("immutable_intent_result_and_history_guard", await Rejected($"UPDATE \"TestEmailSubmissions\" SET \"InputHash\"={new string('A',64)} WHERE \"Id\"={intent}")
                && await Rejected($"UPDATE \"TestEmailSubmissions\" SET \"Outcome\"=3 WHERE \"Id\"={intent}") && await Rejected($"DELETE FROM \"TestEmailSubmissions\" WHERE \"Id\"={intent}"));
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"TestEmailSubmissions\" DISABLE TRIGGER \"TR_TestEmailSubmissions_Guard\"");
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"TestEmailSubmissions\" SET \"InputHash\"={new string('A',64)} WHERE \"Id\"={intent}");
                Check("negative_control_detects_missing_immutable_guard", changed == 1); await tx.RollbackAsync();
            }
            Check("negative_control_rolls_back_hash", (await Row(intent)).InputHash != new string('A',64));
            Check("no_post_campaign_or_transactional_queue_mutation", posts == await db.Posts.CountAsync() && campaigns == await db.NewsletterCampaigns.CountAsync() && jobs == await db.TransactionalEmailJobs.CountAsync());
            Check("only_explicit_suppression_fixtures_created_subscribers", await db.Subscribers.CountAsync() == subscribers + 4);
            return Report(checks);
        }
        catch (Exception e)
        {
            var pg = e as PostgresException ?? e.InnerException as PostgresException;
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, exceptionType = e.GetType().Name, sqlState = pg?.SqlState, constraint = pg?.ConstraintName,
                assertion = e is AssertionFailure ? e.Message : null, lastCheck = checks.LastOrDefault().Key, checks })); return 1;
        }
    }
    private static int Report(Dictionary<string, bool> checks)
    { var passed = checks.Count > 0 && checks.Values.All(x => x); Console.WriteLine(JsonSerializer.Serialize(new { passed, count = checks.Count, checks })); return passed ? 0 : 1; }
    private sealed class AssertionFailure(string message) : Exception(message);
    private sealed class Clock(DateTime now) : TimeProvider { public DateTime Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class Sender : IEmailSender
    {
        public ConcurrentBag<EmailSendRequest> Requests { get; } = [];
        public EmailSendResult? Result { get; init; }
        public bool Cancel { get; init; }
        public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken ct = default)
        { Requests.Add(request); await Task.Delay(100, CancellationToken.None); if (Cancel) throw new OperationCanceledException(ct); return Result ?? new(EmailSendOutcome.Accepted, EmailSendReason.None, Guid.NewGuid(), 200); }
    }
}
