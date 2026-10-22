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

/// <summary>Real PostgreSQL lifecycle races; outbound faults use an explicit observer, live mode only the fixed simulator.</summary>
internal static class SubscriberUnsubscriptionProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, string[] args)
    {
        var directory = new DirectoryInfo(Environment.GetEnvironmentVariable("OUTBOX_TEST_KEYS") ?? throw new InvalidOperationException());
        var provider = DataProtectionProvider.Create(directory, builder => builder.SetApplicationName("DevCoreBlog"));
        var origin = new SubscriberVerificationLinks("https://aether.example.test");
        var links = new SubscriberExitLinks(provider, origin);
        var now = DateTime.UtcNow; now = now.AddTicks(-(now.Ticks % 10));
        var clock = new Clock(now);
        await using var db = new ApplicationDbContext(options);
        var repo = new SubscriberRepository(db);
        var store = new SubscriberUnsubscriptionStore(db, repo);
        var consumer = new SubscriberUnsubscriptionService(links, store, clock);
        var config = new ResendOptions("re_synthetic", "sender@example.test", "Synthetic");
        var protection = new TransactionalEmailProtector(provider, config);
        var factory = new SubscriberVerificationMessageFactory(protection, origin, links);
        var queue = new TransactionalEmailStore(db);
        var proofs = new SubscriberVerificationStore(db, repo, queue);
        var checks = new Dictionary<string, bool>();
        string Token(SubscriberSnapshot sub) => new Uri(links.Create(sub)).Fragment[3..];
        async Task<SubscriberSnapshot> Sub(string? email = null)
        {
            if (!SubscriberAddress.TryCreate(email ?? Guid.NewGuid().ToString("N") + "@example.test", out var address) || address is null) throw new InvalidOperationException();
            return await repo.GetOrCreatePendingAsync(address, now, default);
        }
        async Task<SubscriberSnapshot> Read(Guid id) => await repo.FindAsync(id, default) ?? throw new InvalidOperationException();
        if (args.Contains("--unsubscription-seed"))
        {
            var sub = await Sub();
            await repo.TryApplyAsync(sub.Id, sub.Version, SubscriberChange.ConfirmOwnership, now, default);
            sub = await Read(sub.Id);
            var file = Environment.GetEnvironmentVariable("UNSUBSCRIPTION_TEST_PROOF_FILE") ?? throw new InvalidOperationException();
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { subscriberId = sub.Id, token = Token(sub) }));
            Console.WriteLine("{\"passed\":true}"); return 0;
        }
        if (args.Contains("--unsubscription-live"))
        {
            config = new(Environment.GetEnvironmentVariable("RESEND_API_KEY"), "onboarding@resend.dev", "Aether Letter Test");
            using var services = new ServiceCollection().AddLogging().AddResendEmail(config).BuildServiceProvider();
            var sender = new Sender(services.GetRequiredService<IEmailSender>());
            protection = new(provider, config);
            factory = new(protection, origin, links);
            var sub = await Sub("delivered@resend.dev");
            var prepared = factory.Prepare(sub, now);
            checks["real_simulator_mail_staged"] = await proofs.StageAsync(prepared.Token, prepared.Email, default) == VerificationStageOutcome.Queued;
            var dispatcher = new TransactionalEmailDispatcher(queue, protection, config, sender, TimeProvider.System);
            await dispatcher.RunOnceAsync(default);
            checks["real_resend_accepted_exit_link_mail"] = sender.Calls == 1 && sender.ExitToken is not null &&
                await db.TransactionalEmailJobs.AnyAsync(x => x.Id == prepared.Email.Id && x.State == TransactionalEmailState.Accepted);
            var liveExit = new SubscriberUnsubscriptionService(links, store, TimeProvider.System);
            checks["submitted_exit_proof_changes_real_database"] = await liveExit.UnsubscribeAsync(sender.ExitToken, default) == UnsubscriptionOutcome.Unsubscribed;
            checks["submitted_verification_proof_cannot_reactivate"] = await new SubscriberVerificationConsumer(proofs, TimeProvider.System)
                .ConfirmAsync(sender.VerificationToken, default) == VerificationConsumeOutcome.Invalid;
            await dispatcher.RunOnceAsync(default);
            checks["exit_replay_no_additional_provider_call"] = await liveExit.UnsubscribeAsync(sender.ExitToken, default) == UnsubscriptionOutcome.Unsubscribed && sender.Calls == 1;
            return Report(checks);
        }
        var initial = await Sub(); var initialToken = Token(initial);
        checks["encrypted_fragment_contains_no_clear_identity_or_address"] = !links.Create(initial).Contains(initial.EmailAddress) && !initialToken.Contains(initial.Id.ToString("N"));
        checks["malformed_oversized_and_noncanonical_tokens_rejected"] = !links.TryRead(null, out _) && !links.TryRead(new string('x', 513), out _)
            && !links.TryRead(initialToken + "=", out _) && !links.TryRead("!" + initialToken[1..], out _);
        checks["tampered_proof_rejected"] = !links.TryRead((initialToken[0] == 'A' ? "B" : "A") + initialToken[1..], out _);
        checks["cross_purpose_proof_rejected"] = !links.TryRead(provider.CreateProtector("OtherPurpose").Protect("synthetic"), out _);
        checks["keyring_restart_preserves_links"] = new SubscriberExitLinks(DataProtectionProvider.Create(directory, builder => builder.SetApplicationName("DevCoreBlog")), origin).TryRead(initialToken, out _);
        checks["wrong_keyring_rejected"] = !new SubscriberExitLinks(new EphemeralDataProtectionProvider(), origin).TryRead(initialToken, out _);
        links.TryRead(initialToken, out var claims);
        if (claims is null) throw new InvalidOperationException();
        checks["unknown_identity_and_wrong_address_rejected"] = await store.UnsubscribeAsync(claims with { SubscriberId = Guid.NewGuid() }, now, default) == UnsubscriptionOutcome.Invalid
            && await store.UnsubscribeAsync(claims with { AddressBinding = new string('A', 64) }, now, default) == UnsubscriptionOutcome.Invalid;
        await repo.TryApplyAsync(initial.Id, initial.Version, SubscriberChange.ConfirmOwnership, now, default);
        var active = await Read(initial.Id);
        checks["confirmation_preserves_period"] = active.PeriodId == initial.PeriodId && active.IsSendEligible;
        await repo.TryApplyAsync(active.Id, active.Version, SubscriberChange.SuppressBounce, now, default);
        var suppressed = await Read(active.Id);
        checks["suppression_preserves_exit_link"] = suppressed.PeriodId == active.PeriodId && await consumer.UnsubscribeAsync(initialToken, default) == UnsubscriptionOutcome.Unsubscribed;
        var exited = await Read(active.Id);
        checks["exit_preserves_suppression_and_ownership_evidence"] = exited.Status == SubscriptionStatus.Unsubscribed && !exited.IsSendEligible
            && exited.Suppression == SendingSuppression.Bounce && exited.VerifiedUtc == active.VerifiedUtc && exited.UnsubscribedUtc == now;
        checks["repeat_does_not_rewrite_version_or_time"] = await consumer.UnsubscribeAsync(initialToken, default) == UnsubscriptionOutcome.Unsubscribed && exited == await Read(active.Id);
        await repo.TryApplyAsync(exited.Id, exited.Version, SubscriberChange.RequestReconfirmation, now, default);
        var reopened = await Read(exited.Id);
        checks["reapplication_rotates_period_even_at_identical_timestamp"] = reopened.PeriodId != exited.PeriodId && reopened.Status == SubscriptionStatus.PendingVerification && reopened.Suppression == SendingSuppression.Bounce;
        checks["old_link_cannot_cancel_new_pending_period"] = await consumer.UnsubscribeAsync(initialToken, default) == UnsubscriptionOutcome.Invalid && reopened == await Read(exited.Id);
        await repo.TryApplyAsync(reopened.Id, reopened.Version, SubscriberChange.ConfirmOwnership, now, default);
        checks["old_link_cannot_cancel_new_confirmed_period"] = await consumer.UnsubscribeAsync(initialToken, default) == UnsubscriptionOutcome.Invalid && (await Read(exited.Id)).Status == SubscriptionStatus.Active;
        checks["new_period_link_still_works"] = await consumer.UnsubscribeAsync(Token(await Read(exited.Id)), default) == UnsubscriptionOutcome.Unsubscribed;
        var race = await Sub(); var raceToken = Token(race);
        async Task<UnsubscriptionOutcome> ConcurrentExit(string token)
        {
            await using var ctx = new ApplicationDbContext(options);
            return await new SubscriberUnsubscriptionService(links, new SubscriberUnsubscriptionStore(ctx, new(ctx)), clock).UnsubscribeAsync(token, default);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ConcurrentExit(raceToken)));
        checks["twelve_parallel_exits_one_revision"] = results.All(x => x == UnsubscriptionOutcome.Unsubscribed) && (await Read(race.Id)).Version == race.Version + 1;
        var pending = await Sub(); var preparedMail = factory.Prepare(pending, now);
        await proofs.StageAsync(preparedMail.Token, preparedMail.Email, default);
        var lease = await queue.ClaimAsync(now, default) ?? throw new InvalidOperationException();
        await consumer.UnsubscribeAsync(Token(pending), default);
        checks["exit_after_claim_blocks_attempt_start"] = await queue.StartAsync(lease, pending.AddressKey, now, default) is null
            && !await db.TransactionalEmailAttempts.AnyAsync(x => x.JobId == preparedMail.Email.Id);
        checks["queued_mail_cancelled_by_current_eligibility"] = await db.TransactionalEmailJobs.AnyAsync(x => x.Id == preparedMail.Email.Id && x.State == TransactionalEmailState.Cancelled);
        var queued = await Sub(); var staged = factory.Prepare(queued, now); await proofs.StageAsync(staged.Token, staged.Email, default);
        await consumer.UnsubscribeAsync(Token(queued), default);
        var observer = new Sender();
        await new TransactionalEmailDispatcher(queue, protection, config, observer, clock).RunOnceAsync(default);
        checks["dispatcher_makes_no_provider_call_after_exit"] = observer.Calls == 0;
        // Confirmation and exit use the same row lock; either ordering leaves sending disabled.
        var confirmRace = await Sub(); var confirmMail = factory.Prepare(confirmRace, now); await proofs.StageAsync(confirmMail.Token, confirmMail.Email, default);
        var confirmDigest = confirmMail.Token.Digest;
        async Task ConfirmRace()
        {
            await using var ctx = new ApplicationDbContext(options);
            await new SubscriberVerificationStore(ctx, new(ctx), new(ctx)).ConsumeAsync(confirmDigest, 1, now, default);
        }
        await Task.WhenAll(ConfirmRace(), ConcurrentExit(Token(confirmRace)));
        checks["parallel_confirmation_and_exit_never_reactivates"] = (await Read(confirmRace.Id)).Status == SubscriptionStatus.Unsubscribed;
        // Issue and exit the prior period, then race its proof with new-period ownership confirmation.
        var rejoin = await Sub(); var old = Token(rejoin); await consumer.UnsubscribeAsync(old, default);
        var prior = await Read(rejoin.Id); await repo.TryApplyAsync(prior.Id, prior.Version, SubscriberChange.RequestReconfirmation, now, default);
        var fresh = await Read(rejoin.Id); var freshMail = factory.Prepare(fresh, now); await proofs.StageAsync(freshMail.Token, freshMail.Email, default);
        async Task<VerificationConsumeOutcome> Reconfirm()
        {
            await using var ctx = new ApplicationDbContext(options);
            return await new SubscriberVerificationStore(ctx, new(ctx), new(ctx)).ConsumeAsync(freshMail.Token.Digest, 1, now, default);
        }
        var oldExitTask = ConcurrentExit(old); var confirmTask = Reconfirm(); await Task.WhenAll(oldExitTask, confirmTask);
        checks["parallel_old_exit_and_reconfirmation_preserves_new_subscription"] = await oldExitTask == UnsubscriptionOutcome.Invalid
            && await confirmTask == VerificationConsumeOutcome.Confirmed && (await Read(rejoin.Id)).IsSendEligible;
        return Report(checks);
    }
    private static int Report(Dictionary<string, bool> checks)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Values.All(x => x), count = checks.Count, checks }));
        return checks.Values.All(x => x) ? 0 : 1;
    }
    private sealed class Clock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private sealed class Sender(IEmailSender? transport = null) : IEmailSender
    {
        public int Calls { get; private set; }
        public string? ExitToken { get; private set; }
        public string? VerificationToken { get; private set; }
        public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            ExitToken = Regex.Match(request.Text, @"/abonelik/cik#t=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value;
            VerificationToken = Regex.Match(request.Text, @"/abonelik/dogrula#t=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value;
            return transport is null ? new(EmailSendOutcome.Accepted, EmailSendReason.None, Guid.NewGuid(), 200) : await transport.SendAsync(request, cancellationToken);
        }
    }
}
