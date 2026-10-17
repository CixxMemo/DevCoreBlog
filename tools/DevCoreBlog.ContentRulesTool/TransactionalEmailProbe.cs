using System.Diagnostics;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Disposable PostgreSQL concurrency, crash and encryption acceptance; provider faults are explicitly simulated.</summary>
internal static class TransactionalEmailProbe
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var keyPath = Environment.GetEnvironmentVariable("OUTBOX_TEST_KEYS") ?? throw new InvalidOperationException("Test key directory required.");
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyPath));
        if (args.Contains("--outbox-live"))
            return await LiveAsync(options, provider);
        var config = new ResendOptions("re_synthetic", "sender@example.test", "Synthetic");
        var protector = new TransactionalEmailProtector(provider, config);
        await using var db = new ApplicationDbContext(options);
        var store = new TransactionalEmailStore(db);
        if (args.Contains("--outbox-child"))
        {
            var mode = args[^1];
            var lease = await store.ClaimAsync(Now, default) ?? throw new InvalidOperationException("Missing child job.");
            if (mode == "claim") Environment.Exit(0);
            var sub = await new SubscriberRepository(db).FindAsync(lease.SubscriberId, default);
            if (sub is null) return 2;
            if (mode == "start") { await store.StartAsync(lease, sub.AddressKey, Now, default); Environment.Exit(0); }
            return 2;
        }
        var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) => checks.Add(name, value);
        async Task<SubscriberSnapshot> Subscriber()
        {
            if (!SubscriberAddress.TryCreate(Guid.NewGuid().ToString("N") + "@example.test", out var address) || address is null) throw new InvalidOperationException();
            return await new SubscriberRepository(db).GetOrCreatePendingAsync(address, Now, default);
        }
        async Task<PreparedTransactionalEmail> Queue(SubscriberSnapshot? sub = null, bool invalidEnvelope = false)
        {
            sub ??= await Subscriber();
            var job = protector.Prepare(Guid.NewGuid(), sub, "Synthetic", "<p>private-token-fixture</p>", "private-token-fixture", Now.AddTicks(7), Now.AddDays(1).AddTicks(7));
            if (invalidEnvelope) job = new(job.Id,job.SubscriberId,job.SubscriberVersion,"AAAA",job.PayloadHash,job.CreatedUtc,job.ExpiresUtc);
            if (await store.EnqueueAsync(job, default) != EmailQueueOutcome.Queued) throw new InvalidOperationException("Fixture enqueue failed.");
            return job;
        }
        Task<TransactionalEmailJob> Job(Guid id) => db.TransactionalEmailJobs.AsNoTracking().SingleAsync(x => x.Id == id);
        async Task Drain()
        {
            // Terminalize only fixture leftovers; never delete journal/data.
            await store.RecoverAsync(Now.AddDays(2), default);
        }
        var sender = new Sender(new(EmailSendOutcome.Accepted, EmailSendReason.None, Guid.NewGuid(), 200));
        TransactionalEmailDispatcher Dispatcher(TransactionalEmailStore work, Sender transport, DateTime time, ResendOptions? binding = null) =>
            new(work, new(provider, binding ?? config), binding ?? config, transport, new Clock(time));
        var queued = await Queue();
        Check("postgres_microsecond_payload_identity", queued.CreatedUtc == Now && queued.ExpiresUtc == Now.AddDays(1));
        Check("payload_encrypted_no_plaintext", !queued.ProtectedPayload.Contains("private-token-fixture") && !queued.ProtectedPayload.Contains("@"));
        Check("idempotent_enqueue", await store.EnqueueAsync(queued, default) == EmailQueueOutcome.AlreadyQueued);
        var subscriber = await new SubscriberRepository(db).FindAsync(queued.SubscriberId, default) ?? throw new InvalidOperationException();
        var conflict = protector.Prepare(queued.Id, subscriber, "Different", "<p>other</p>", "other", Now, Now.AddDays(1));
        Check("same_identity_different_content_conflict", await store.EnqueueAsync(conflict, default) == EmailQueueOutcome.Conflict);
        await Dispatcher(store, sender, Now).RunOnceAsync(default);
        Check("acceptance_is_durable", (await Job(queued.Id)).State == TransactionalEmailState.Accepted && sender.Calls == 1);
        Check("attempt_journal_accepted", await db.TransactionalEmailAttempts.AnyAsync(x => x.JobId == queued.Id && x.Number == 1 && x.Outcome == EmailSendOutcome.Accepted && x.CompletedUtc == Now));
        await Dispatcher(store, sender, Now).RunOnceAsync(default);
        Check("accepted_never_replayed", sender.Calls == 1);
        // A caller rollback must roll back staging too.
        PreparedTransactionalEmail rolled;
        await using (var tx = await db.Database.BeginTransactionAsync()) { rolled = await Queue(); await tx.RollbackAsync(); }
        Check("enqueue_enlists_caller_transaction", !await db.TransactionalEmailJobs.AnyAsync(x => x.Id == rolled.Id));
        var race = await Queue();
        async Task<bool> ClaimRace()
        {
            await using var competitor = new ApplicationDbContext(options);
            return await new TransactionalEmailStore(competitor).ClaimAsync(Now, default) is not null;
        }
        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ClaimRace()));
        Check("twelve_workers_single_claim", claims.Count(x => x) == 1);
        await store.RecoverAsync(Now.AddMinutes(3), default);
        var recovered = await store.ClaimAsync(Now.AddMinutes(3), default) ?? throw new InvalidOperationException();
        Check("pre_send_crash_reclaimed_without_attempt", recovered.Id == race.Id && recovered.AttemptCount == 0);
        var started = await store.StartAsync(recovered, (await new SubscriberRepository(db).FindAsync(race.SubscriberId, default))?.AddressKey ?? "", Now.AddMinutes(3), default);
        Check("attempt_started_atomically", started?.AttemptNumber == 1 && (await Job(race.Id)).State == TransactionalEmailState.Sending);
        await store.RecoverAsync(Now.AddMinutes(6), default);
        Check("in_flight_crash_is_held_not_retried", (await Job(race.Id)).State == TransactionalEmailState.Held
            && await db.TransactionalEmailAttempts.AnyAsync(x => x.JobId == race.Id && x.Outcome == EmailSendOutcome.Uncertain));
        Check("stale_completion_cannot_overwrite", !await store.CompleteAsync(recovered, 1,
            new(TransactionalEmailState.Accepted, TransactionalEmailProblem.None, Now.AddMinutes(6), sender.Result), Now.AddMinutes(6), default));
        var blockedSub = await Subscriber(); var blocked = await Queue(blockedSub);
        await new SubscriberRepository(db).TryApplyAsync(blockedSub.Id, blockedSub.Version, SubscriberChange.SuppressComplaint, Now, default);
        var beforeCalls = sender.Calls;
        await Dispatcher(store, sender, Now).RunOnceAsync(default);
        Check("current_suppression_prevents_network", (await Job(blocked.Id)).State == TransactionalEmailState.Cancelled && sender.Calls == beforeCalls);
        var changedSub = await Subscriber(); var changed = await Queue(changedSub);
        await new SubscriberRepository(db).TryApplyAsync(changedSub.Id, 1, SubscriberChange.Unsubscribe, Now, default);
        await Dispatcher(store, sender, Now).RunOnceAsync(default);
        Check("unsubscribe_invalidates_queued_proof", (await Job(changed.Id)).State == TransactionalEmailState.Cancelled && sender.Calls == beforeCalls);
        var bad = await Queue(invalidEnvelope: true);
        await Dispatcher(store, sender, Now).RunOnceAsync(default);
        Check("tampered_payload_held_no_send", (await Job(bad.Id)).Problem == TransactionalEmailProblem.InvalidPayload && sender.Calls == beforeCalls);
        var rotated = await Queue();
        await Dispatcher(store, sender, Now, new("re_rotated", "sender@example.test", "Synthetic")).RunOnceAsync(default);
        Check("credential_rotation_cannot_rebind_job", (await Job(rotated.Id)).Problem == TransactionalEmailProblem.ConfigurationChanged && sender.Calls == beforeCalls);
        var uncertain = await Queue(); var fault = new Sender(new(EmailSendOutcome.Uncertain, EmailSendReason.Timeout));
        await Dispatcher(store, fault, Now).RunOnceAsync(default); await Dispatcher(store, fault, Now.AddMinutes(1)).RunOnceAsync(default);
        Check("uncertain_never_auto_retries", (await Job(uncertain.Id)).State == TransactionalEmailState.Held && fault.Calls == 1);
        var retry = await Queue(); var limited = new Sender(new(EmailSendOutcome.Retryable, EmailSendReason.RateLimit, HttpStatus:429, RetryAfter:TimeSpan.FromMinutes(1)));
        await Dispatcher(store, limited, Now).RunOnceAsync(default);
        Check("retry_after_is_respected", (await Job(retry.Id)).DueUtc == Now.AddMinutes(1) && (await Job(retry.Id)).State == TransactionalEmailState.Pending);
        await Dispatcher(store, limited, Now.AddSeconds(59)).RunOnceAsync(default);
        Check("no_early_retry", limited.Calls == 1);
        var retries = new[] { Now.AddMinutes(1), Now.AddMinutes(3), Now.AddMinutes(13), Now.AddMinutes(43) };
        foreach (var time in retries) await Dispatcher(store, limited, time).RunOnceAsync(default);
        Check("five_attempt_hard_limit", limited.Calls == 5 && (await Job(retry.Id)).Problem == TransactionalEmailProblem.AttemptsExhausted);
        Check("retry_identity_and_payload_unchanged", limited.Requests.Select(x => x.IdempotencyKey).Distinct().Count() == 1
            && limited.Requests.Select(x => x.FirstAttemptUtc).Distinct().Count() == 1 && limited.Requests.Select(x => x.Html).Distinct().Count() == 1);
        foreach (var pair in new[] { (EmailSendReason.DailyQuota, TransactionalEmailState.Expired), (EmailSendReason.MonthlyQuota, TransactionalEmailState.Held) })
        {
            var quota = await Queue(); await Dispatcher(store, new Sender(new(EmailSendOutcome.Retryable, pair.Item1)), Now).RunOnceAsync(default);
            Check("quota_" + pair.Item1, (await Job(quota.Id)).State == pair.Item2);
        }
        var rejected = await Queue(); await Dispatcher(store, new Sender(new(EmailSendOutcome.Rejected, EmailSendReason.Validation)), Now).RunOnceAsync(default);
        Check("rejection_terminal", (await Job(rejected.Id)).State == TransactionalEmailState.Rejected);
        var expiry = await Queue(); await Dispatcher(store, sender, Now.AddDays(1)).RunOnceAsync(default);
        Check("expired_never_sent", (await Job(expiry.Id)).State == TransactionalEmailState.Expired && sender.Calls == beforeCalls);
        // Real process exits leave committed states. No finally/Dispose path runs in child.
        foreach (var mode in new[] { "claim", "start" })
        {
            var crash = await Queue();
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(typeof(TransactionalEmailProbe).Assembly.Location); info.ArgumentList.Add("--outbox-child"); info.ArgumentList.Add(mode);
            using var child = Process.Start(info) ?? throw new InvalidOperationException();
            var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch { child.Kill(true); await child.WaitForExitAsync(); throw; }
            await output; await errors;
            await store.RecoverAsync(Now.AddMinutes(3), default);
            Check("real_process_exit_" + mode, child.ExitCode == 0 && (await Job(crash.Id)).State == (mode == "claim" ? TransactionalEmailState.Pending : TransactionalEmailState.Held));
            if (mode == "claim") await Dispatcher(store, sender, Now.AddMinutes(3)).RunOnceAsync(default);
            Check("restart_protected_payload_" + mode, (await Job(crash.Id)).State == (mode == "claim" ? TransactionalEmailState.Accepted : TransactionalEmailState.Held));
        }
        async Task<bool> Reject(FormattableString sql)
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync(sql); return false; }
            catch (PostgresException error) when (error.SqlState == "23514") { return true; }
        }
        Check("db_rejects_out_of_range_reason", await Reject($"""
            UPDATE "TransactionalEmailAttempts" SET "Reason"=16 WHERE "JobId"={queued.Id}
            """));
        Check("db_rejects_unleased_sending", await Reject($"""
            UPDATE "TransactionalEmailJobs" SET "State"=2,"ProviderMessageId"=NULL WHERE "Id"={queued.Id}
            """));
        Check("db_rejects_plaintext_payload", await Reject($"""
            UPDATE "TransactionalEmailJobs" SET "ProtectedPayload"='private text' WHERE "Id"={queued.Id}
            """));
        await Drain();
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Values.All(x => x), count = checks.Count, checks }));
        return checks.Values.All(x => x) ? 0 : 1;
    }

    private static async Task<int> LiveAsync(DbContextOptions<ApplicationDbContext> options, IDataProtectionProvider provider)
    {
        var config = new ResendOptions(Environment.GetEnvironmentVariable("RESEND_API_KEY"), "onboarding@resend.dev", "Aether Notes Test");
        using var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddResendEmail(config).BuildServiceProvider();
        var transport = services.GetRequiredService<IEmailSender>();
        await using var db = new ApplicationDbContext(options);
        if (!SubscriberAddress.TryCreate("delivered@resend.dev", out var address) || address is null) return 2;
        var now = DateTime.UtcNow;
        var subscriber = await new SubscriberRepository(db).GetOrCreatePendingAsync(address, now, default);
        var protection = new TransactionalEmailProtector(provider, config);
        var store = new TransactionalEmailStore(db);
        var job = protection.Prepare(Guid.NewGuid(), subscriber, "Aether Notes outbox simulator", "<p>Synthetic outbox acceptance.</p>", "Synthetic outbox acceptance.", now, now.AddDays(1));
        var checks = new Dictionary<string,bool>();
        checks["live_isolated_enqueue"] = await store.EnqueueAsync(job, default) == EmailQueueOutcome.Queued;
        await new TransactionalEmailDispatcher(store, protection, config, transport, TimeProvider.System).RunOnceAsync(default);
        var saved = await db.TransactionalEmailJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
        checks["real_resend_acceptance_persisted"] = saved.State == TransactionalEmailState.Accepted && saved.ProviderMessageId is not null;
        checks["real_attempt_persisted"] = await db.TransactionalEmailAttempts.AnyAsync(x => x.JobId == job.Id && x.Outcome == EmailSendOutcome.Accepted);
        checks["accepted_job_not_claimed_again"] = await store.ClaimAsync(DateTime.UtcNow, default) is null;
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Values.All(x=>x), count=checks.Count, checks,
            scope="One real API call to Resend fixed simulator; acceptance is not inbox delivery." }));
        return checks.Values.All(x=>x) ? 0 : 1;
    }
    private sealed class Clock(DateTime time) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(time); }
    private sealed class Sender(EmailSendResult result) : IEmailSender
    {
        public EmailSendResult Result { get; } = result;
        public int Calls { get; private set; }
        public List<EmailSendRequest> Requests { get; } = [];
        public Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken = default)
        { Calls++; Requests.Add(request); return Task.FromResult(Result); }
    }
}
