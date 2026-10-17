using System.Text.Json;
using System.Text.RegularExpressions;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Email;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Owned PostgreSQL login proof, admission and session races; live mode is one fixed Resend simulator submission.</summary>
internal static class ReaderIdentityProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var checks = new Dictionary<string, bool>();
        try
        {
            await using var db = new ApplicationDbContext(options);
            var owned = Environment.GetEnvironmentVariable("READER_TEST_ROOT") ?? "";
            var conn = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            await db.Database.OpenConnectionAsync();
            using var guard = db.Database.GetDbConnection().CreateCommand(); guard.CommandText = "SHOW data_directory";
            var dir = (string?)await guard.ExecuteScalarAsync();
            if (!owned.StartsWith("/tmp/devcoreblog-reader-", StringComparison.Ordinal) || dir != owned + "/pg"
                || conn.Host != "127.0.0.1" || conn.Database?.StartsWith("reader_", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Owned reader fixture required.");
            var provider = DataProtectionProvider.Create(new DirectoryInfo(owned + "/keys"), b => b.SetApplicationName("DevCoreBlog"));
            var now = ReaderIdentityRules.Utc(DateTime.UtcNow);
            var config = new ResendOptions("re_synthetic", "sender@example.test", "Synthetic");
            var live = args.Contains("--reader-live");
            if (live) config = new ResendOptions(Environment.GetEnvironmentVariable("RESEND_API_KEY"), "onboarding@resend.dev", "Aether Notes Test");
            var protector = new TransactionalEmailProtector(provider, config);
            var links = new SubscriberVerificationLinks("https://aether.example.test");
            var factory = new ReaderLoginMessageFactory(protector, links);
            var repo = new SubscriberRepository(db);
            var store = new ReaderIdentityStore(db, new TransactionalEmailStore(db));
            async Task<SubscriberSnapshot> Sub(bool active = true, string? email = null)
            {
                if (!SubscriberAddress.TryCreate(email ?? Guid.NewGuid().ToString("N") + "@example.test", out var address) || address is null) throw new InvalidOperationException();
                var result = await repo.GetOrCreatePendingAsync(address, now, default);
                if (active) await repo.TryApplyAsync(result.Id, result.Version, SubscriberChange.ConfirmOwnership, now, default);
                return await repo.FindAsync(result.Id, default) ?? throw new InvalidOperationException();
            }
            async Task<PreparedReaderLogin> Issue(SubscriberSnapshot sub, DateTime? at = null)
            {
                PreparedReaderLogin? draft = null;
                SubscriberAddress.TryCreate(sub.EmailAddress, out var address);
                var clock = at ?? now;
                await store.RequestAsync(address ?? throw new InvalidOperationException(), clock, s => draft = factory.Prepare(s, clock), default);
                return draft ?? throw new InvalidOperationException("Synthetic login not admitted.");
            }
            string Proof(PreparedReaderLogin p)
            {
                var bytes = provider.CreateProtector("AetherNotes.TransactionalEmail.v1", p.Email.Id.ToString("N"), p.Email.SubscriberId.ToString("N"),
                    p.Email.SubscriberVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)).Unprotect(Convert.FromBase64String(p.Email.ProtectedPayload));
                using var doc = JsonDocument.Parse(bytes);
                return Regex.Match(doc.RootElement.GetProperty("Text").GetString() ?? "", @"#t=([A-Za-z0-9_-]{43})").Groups[1].Value;
            }
            if (args.Contains("--reader-seed") || args.Contains("--reader-active") || live)
            {
                var sub = await Sub(email: live ? "delivered@resend.dev" : null);
                var issued = args.Contains("--reader-active") ? null : await Issue(sub);
                var token = issued is null ? "" : Proof(issued);
                if (live && issued is not null)
                {
                    using var services = new ServiceCollection().AddLogging().AddResendEmail(config).BuildServiceProvider();
                    var observer = new Sender(services.GetRequiredService<IEmailSender>());
                    await new TransactionalEmailDispatcher(new TransactionalEmailStore(db), protector, config, observer, TimeProvider.System).RunOnceAsync(default);
                    checks["real_active_reader_login_queued"] = issued.Email.Purpose == TransactionalEmailPurpose.ReaderLogin;
                    checks["one_fixed_resend_simulator_submission_accepted"] = observer.Calls == 1 && await db.TransactionalEmailJobs.AnyAsync(x => x.Id == issued.Email.Id && x.State == TransactionalEmailState.Accepted);
                    checks["real_submitted_proof_consumes_once"] = await store.ConsumeAsync(issued.Token.Digest, now, default) is { } grant
                        && await store.IsValidAsync(grant.Id, now, default) && await store.ConsumeAsync(issued.Token.Digest, now, default) is null;
                    return Report(checks);
                }
                var exits = new SubscriberExitLinks(provider, links);
                var file = Environment.GetEnvironmentVariable("READER_TEST_PROOF_FILE") ?? throw new InvalidOperationException();
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { subscriberId = sub.Id, email = sub.EmailAddress,
                    token, exitToken = new Uri(exits.Create(sub)).Fragment[3..] }));
                Console.WriteLine("{\"passed\":true}"); return 0;
            }
            var active = await Sub();
            var first = await Issue(active); var raw = Proof(first);
            ReaderIdentityService.TryDigest(raw, out var digest);
            checks["proof_is_canonical_256_bit_purpose_bound"] = raw.Length == 43 && digest == first.Token.Digest
                && SubscriberVerificationConsumer.TryDigest(raw, out var verificationDigest) && verificationDigest != digest
                && !ReaderIdentityService.TryDigest(raw + "=", out _) && !ReaderIdentityService.TryDigest(new string('x', 44), out _);
            checks["encrypted_payload_binds_active_reader_and_15_minutes"] = !first.Email.ProtectedPayload.Contains(raw)
                && !first.Email.ProtectedPayload.Contains(active.EmailAddress) && first.Token.ExpiresUtc == now.AddMinutes(15)
                && first.Email.Purpose == TransactionalEmailPurpose.ReaderLogin;
            var sessionsBefore = await db.ReaderSessions.CountAsync();
            checks["issuance_does_not_sign_in_or_reactivate"] = sessionsBefore == 0 && (await repo.FindAsync(active.Id, default)) == active;
            var grant1 = await store.ConsumeAsync(digest ?? "", now, default);
            checks["consume_creates_one_seven_day_session"] = grant1 is not null && grant1.ExpiresUtc == now.AddDays(7)
                && await store.IsValidAsync(grant1.Id, now, default);
            checks["used_proof_rejected_without_second_session"] = await store.ConsumeAsync(digest ?? "", now, default) is null && await db.ReaderSessions.CountAsync() == sessionsBefore + 1;
            if (grant1 is null) throw new InvalidOperationException();
            checks["session_expiry_boundary_and_backwards_time_fail_closed"] = !await store.IsValidAsync(grant1.Id, now.AddDays(7), default)
                && await store.IsValidAsync(grant1.Id, now.AddDays(7).AddTicks(-10), default) && !await store.IsValidAsync(grant1.Id, now.AddTicks(-10), default);
            await store.RevokeAsync(grant1.Id, now, default); await store.RevokeAsync(grant1.Id, now.AddSeconds(1), default);
            checks["logout_revokes_server_side_without_rewriting_time"] = !await store.IsValidAsync(grant1.Id, now, default)
                && await db.ReaderSessions.AnyAsync(x => x.Id == grant1.Id && x.RevokedUtc == now);
            var exp = await Issue(await Sub());
            checks["expired_proof_cannot_create_session"] = await store.ConsumeAsync(exp.Token.Digest, now.AddMinutes(15), default) is null
                && await store.ConsumeAsync(exp.Token.Digest, now.AddTicks(-10), default) is null;
            var pending = await Sub(false); var pendingJobs = await db.TransactionalEmailJobs.CountAsync();
            SubscriberAddress.TryCreate(pending.EmailAddress, out var pendingAddress); SubscriberAddress.TryCreate("unknown@example.test", out var unknown);
            await store.RequestAsync(pendingAddress ?? throw new InvalidOperationException(), now, s => factory.Prepare(s, now), default);
            await store.RequestAsync(unknown ?? throw new InvalidOperationException(), now, s => factory.Prepare(s, now), default);
            checks["pending_and_unknown_do_not_create_mail_or_identity"] = pendingJobs == await db.TransactionalEmailJobs.CountAsync()
                && !await db.Subscribers.AnyAsync(x => x.AddressKey == "unknown@example.test");
            var parallel = await Sub();
            var starts = await Task.WhenAll(Enumerable.Range(0,12).Select(async _ =>
            {
                await using var own = new ApplicationDbContext(options);
                SubscriberAddress.TryCreate(parallel.EmailAddress, out var address);
                await new ReaderIdentityStore(own, new TransactionalEmailStore(own)).RequestAsync(address ?? throw new InvalidOperationException(), now,
                    s => factory.Prepare(s, now), default); return true;
            }));
            checks["twelve_parallel_requests_one_email"] = starts.All(x => x) && await db.ReaderLoginTokens.CountAsync(x => x.SubscriberId == parallel.Id) == 1;
            var parallelToken = await db.ReaderLoginTokens.AsNoTracking().SingleAsync(x => x.SubscriberId == parallel.Id);
            var grants = await Task.WhenAll(Enumerable.Range(0,12).Select(async _ =>
            {
                await using var own = new ApplicationDbContext(options);
                return await new ReaderIdentityStore(own, new TransactionalEmailStore(own)).ConsumeAsync(parallelToken.Digest ?? throw new InvalidOperationException("Fixture proof missing."), now, default);
            }));
            checks["twelve_parallel_consumptions_one_session"] = grants.Count(x => x is not null) == 1;
            var singleGrant = grants.Single(x => x is not null) ?? throw new InvalidOperationException();
            await repo.TryApplyAsync(parallel.Id, parallel.Version, SubscriberChange.Unsubscribe, now, default);
            checks["unsubscribe_invalidates_existing_cookie_session"] = !await store.IsValidAsync(singleGrant.Id, now, default);
            var exited = await repo.FindAsync(parallel.Id, default) ?? throw new InvalidOperationException();
            await repo.TryApplyAsync(exited.Id, exited.Version, SubscriberChange.RequestReconfirmation, now, default);
            var reopened = await repo.FindAsync(exited.Id, default) ?? throw new InvalidOperationException();
            await repo.TryApplyAsync(reopened.Id, reopened.Version, SubscriberChange.ConfirmOwnership, now, default);
            checks["new_subscription_period_does_not_revive_old_session"] = !await store.IsValidAsync(singleGrant.Id, now, default);
            var suppressed = await Sub(); var suppressionProof = await Issue(suppressed);
            var suppressionGrant = await store.ConsumeAsync(suppressionProof.Token.Digest, now, default) ?? throw new InvalidOperationException();
            await repo.TryApplyAsync(suppressed.Id, suppressed.Version, SubscriberChange.SuppressManual, now, default);
            checks["suppression_closes_reader_access"] = !await store.IsValidAsync(suppressionGrant.Id, now, default);
            var stale = await Sub(); var staleProof = await Issue(stale);
            await repo.TryApplyAsync(stale.Id, stale.Version, SubscriberChange.Unsubscribe, now, default);
            checks["unsubscribe_invalidates_unconsumed_link"] = await store.ConsumeAsync(staleProof.Token.Digest, now, default) is null;
            await db.TransactionalEmailJobs.Where(x => x.State == TransactionalEmailState.Pending && x.Id != first.Email.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, TransactionalEmailState.Cancelled));
            var q = new TransactionalEmailStore(db); var lease = await q.ClaimAsync(now, default);
            if (lease is null) throw new InvalidOperationException();
            checks["consumed_or_changed_login_not_sent"] = await q.StartAsync(lease, active.AddressKey, now, default) is null;
            // A valid active-reader job exercises the real dispatcher with an explicit observer, not a network claim.
            await db.TransactionalEmailJobs.Where(x => x.State == TransactionalEmailState.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, TransactionalEmailState.Cancelled));
            var outbound = await Sub(); var outboundProof = await Issue(outbound); var sender = new Sender(null);
            await new TransactionalEmailDispatcher(q, protector, config, sender, new Clock(now)).RunOnceAsync(default);
            checks["dispatcher_supports_active_reader_purpose"] = sender.Calls == 1 && await db.TransactionalEmailJobs.AnyAsync(x => x.Id == outboundProof.Email.Id && x.State == TransactionalEmailState.Accepted);
            // Losing the exact lifetime constraint must make this deliberately invalid SQL succeed.
            var rejected = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReaderLoginTokens\" SET \"ExpiresUtc\"=\"ExpiresUtc\"+interval '1 minute' WHERE \"Id\"={outboundProof.Token.Id}"); }
            catch (PostgresException e) when (e.SqlState == "23514") { rejected = true; }
            checks["database_rejects_extended_login_lifetime"] = rejected;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ReaderLoginTokens\" DROP CONSTRAINT \"CK_ReaderLoginTokens_Valid\"");
                // Owned rollback-only negative control removes both independent lifetime guards.
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_LoginRetention\" ON \"ReaderLoginTokens\"");
                checks["negative_control_detects_missing_lifetime_constraint"] = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReaderLoginTokens\" SET \"ExpiresUtc\"=\"ExpiresUtc\"+interval '1 minute' WHERE \"Id\"={outboundProof.Token.Id}") == 1;
                await tx.RollbackAsync();
            }
            checks["negative_control_rollback_restores_proof"] = await db.ReaderLoginTokens.AnyAsync(x => x.Id == outboundProof.Token.Id && x.ExpiresUtc == outboundProof.Token.ExpiresUtc);
            var race = await Sub(); var raceProof = await Issue(race);
            async Task<ReaderSessionGrant?> ConsumeRace() { await using var own = new ApplicationDbContext(options); return await new ReaderIdentityStore(own, new TransactionalEmailStore(own)).ConsumeAsync(raceProof.Token.Digest, now, default); }
            var consumeRace = ConsumeRace();
            await using (var own = new ApplicationDbContext(options)) await new SubscriberRepository(own).TryApplyAsync(race.Id, race.Version, SubscriberChange.Unsubscribe, now, default);
            var raced = await consumeRace;
            checks["exit_consume_race_cannot_leave_valid_session"] = raced is null || !await store.IsValidAsync(raced.Id, now, default);
            // Reset only this owned admission row for a dedicated distributed-IP/global-cap race.
            await db.ReaderLoginBudgets.ExecuteUpdateAsync(s => s.SetProperty(x => x.Count, 0));
            var many = new List<SubscriberSnapshot>(); for (var i = 0; i < 25; i++) many.Add(await Sub());
            await Task.WhenAll(many.Select(async sub => { await using var own = new ApplicationDbContext(options); SubscriberAddress.TryCreate(sub.EmailAddress, out var address);
                await new ReaderIdentityStore(own, new TransactionalEmailStore(own)).RequestAsync(address ?? throw new InvalidOperationException(), now, s => factory.Prepare(s, now), default); }));
            var manyIds = many.Select(s => s.Id).ToArray();
            checks["global_durable_daily_cap_twenty_under_parallel_load"] = await db.ReaderLoginTokens.CountAsync(x => manyIds.Contains(x.SubscriberId)) == 20
                && await db.ReaderLoginBudgets.AnyAsync(x => x.Count == 20);
            var nextDay = now.Date.AddDays(1);
            await store.RequestAsync(unknown ?? throw new InvalidOperationException(), nextDay, s => factory.Prepare(s, nextDay), default);
            checks["unknown_request_does_not_reset_committed_budget"] = await db.ReaderLoginBudgets.AnyAsync(x => x.Count == 20 && x.WindowStartUtc == now.Date);
            await Issue(many[0], nextDay);
            checks["utc_day_rollover_resets_budget_atomically"] = await db.ReaderLoginBudgets.AnyAsync(x => x.Count == 1 && x.WindowStartUtc == nextDay);
            var jobs = await db.TransactionalEmailJobs.CountAsync();
            SubscriberAddress.TryCreate(many[0].EmailAddress, out var cooldown);
            await store.RequestAsync(cooldown ?? throw new InvalidOperationException(), nextDay.AddMinutes(14), s => factory.Prepare(s, nextDay.AddMinutes(14)), default);
            checks["address_cooldown_survives_new_store_and_consumption"] = await db.TransactionalEmailJobs.CountAsync() == jobs;
            await Issue(many[0], nextDay.AddMinutes(15));
            checks["address_cooldown_exact_boundary_reopens"] = await db.TransactionalEmailJobs.CountAsync() == jobs + 1;
            // Inject a failure after outbox INSERT to prove the whole admission transaction rolls back.
            var rollbackSub = await Sub();
            var beforeJobs = await db.TransactionalEmailJobs.CountAsync();
            var beforeProofs = await db.ReaderLoginTokens.CountAsync();
            var beforeBudget = await db.ReaderLoginBudgets.Select(x => x.Count).SingleAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reader_fixture_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    RAISE EXCEPTION 'Synthetic admission failure' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER reader_fixture_reject BEFORE INSERT ON "ReaderLoginTokens" FOR EACH ROW EXECUTE FUNCTION reader_fixture_reject();
                """);
            var failedAfterQueue = false;
            try { await Issue(rollbackSub, nextDay.AddMinutes(16)); }
            catch (PostgresException e) when (e.SqlState == "23514") { failedAfterQueue = true; }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reader_fixture_reject ON \"ReaderLoginTokens\"; DROP FUNCTION reader_fixture_reject()"); }
            checks["staging_failure_rolls_back_outbox_proof_and_budget"] = failedAfterQueue && beforeJobs == await db.TransactionalEmailJobs.CountAsync()
                && beforeProofs == await db.ReaderLoginTokens.CountAsync() && beforeBudget == await db.ReaderLoginBudgets.Select(x => x.Count).SingleAsync();
            // Persist a genuine old-shape v1 encrypted envelope (without Purpose), then dispatch with current code.
            await db.TransactionalEmailJobs.Where(x => x.State == TransactionalEmailState.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, TransactionalEmailState.Cancelled));
            var legacySub = await Sub(false);
            var exitLinks = new SubscriberExitLinks(provider, links);
            var legacyDraft = new SubscriberVerificationMessageFactory(protector, links, exitLinks).Prepare(legacySub, now);
            var legacyProtector = provider.CreateProtector("AetherNotes.TransactionalEmail.v1", legacyDraft.Email.Id.ToString("N"),
                legacySub.Id.ToString("N"), legacySub.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(legacyProtector.Unprotect(Convert.FromBase64String(legacyDraft.Email.ProtectedPayload)))?.AsObject()
                ?? throw new InvalidOperationException();
            legacyJson.Remove("Purpose");
            var legacyBytes = System.Text.Encoding.UTF8.GetBytes(legacyJson.ToJsonString());
            var legacyEnvelope = new PreparedTransactionalEmail(legacyDraft.Email.Id, legacySub.Id, legacySub.Version,
                Convert.ToBase64String(legacyProtector.Protect(legacyBytes)), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(legacyBytes)),
                now, legacyDraft.Email.ExpiresUtc);
            await q.EnqueueAsync(legacyEnvelope, default);
            var legacySender = new Sender(null);
            await new TransactionalEmailDispatcher(q, protector, config, legacySender, new Clock(now)).RunOnceAsync(default);
            checks["old_v1_encrypted_envelope_without_purpose_still_dispatches"] = legacySender.Calls == 1
                && await db.TransactionalEmailJobs.AnyAsync(x => x.Id == legacyEnvelope.Id && x.Purpose == TransactionalEmailPurpose.Verification && x.State == TransactionalEmailState.Accepted);
            await using var restart = new ApplicationDbContext(options);
            checks["new_context_preserves_revocation_and_consumption"] = !await new ReaderIdentityStore(restart, new TransactionalEmailStore(restart)).IsValidAsync(grant1.Id, now, default)
                && await restart.ReaderLoginTokens.AnyAsync(x => x.Id == first.Token.Id && x.ConsumedUtc == now);
            return Report(checks);
        }
        catch (Exception e)
        {
            checks["completed"] = false;
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, count = checks.Count, checks, errorType = e.GetType().Name,
                databaseCode = e is PostgresException pg ? pg.SqlState : null })); return 1;
        }
    }
    private static int Report(Dictionary<string,bool> checks) { var passed = checks.Values.All(x => x); Console.WriteLine(JsonSerializer.Serialize(new { passed, count = checks.Count, checks })); return passed ? 0 : 1; }
    private sealed class Clock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private sealed class Sender(IEmailSender? actual) : IEmailSender
    {
        public int Calls { get; private set; }
        public Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken ct)
        { Calls++; return actual?.SendAsync(request, ct) ?? Task.FromResult(new EmailSendResult(EmailSendOutcome.Accepted, EmailSendReason.None, ProviderMessageId: Guid.NewGuid())); }
    }
}
