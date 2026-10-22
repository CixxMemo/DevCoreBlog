using System.Security.Cryptography;
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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Real proof/lifecycle/outbox transactions and concurrent consumption on disposable PostgreSQL.</summary>
internal static class SubscriberVerificationProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, bool seed, bool pendingOnly = false, bool live = false)
    {
        var keyDir = Environment.GetEnvironmentVariable("OUTBOX_TEST_KEYS") ?? throw new InvalidOperationException();
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
        if (live) return await LiveAsync(options, provider);
        var config = new ResendOptions("re_synthetic", "sender@example.test", "Synthetic");
        var now = seed ? DateTime.UtcNow : new DateTime(2026,10,14,12,0,0,DateTimeKind.Utc);
        now = now.AddTicks(-(now.Ticks % 10));
        var clock = new Clock(now);
        await using var db = new ApplicationDbContext(options);
        var repo = new SubscriberRepository(db); var queue = new TransactionalEmailStore(db);
        var store = new SubscriberVerificationStore(db, repo, queue);
        var protector = new TransactionalEmailProtector(provider, config);
        var links = new SubscriberVerificationLinks("https://aether.example.test");
        var issuer = new SubscriberVerificationIssuer(repo, store, new SubscriberVerificationMessageFactory(protector, links, new(provider, links)), config, clock);
        var consumer = new SubscriberVerificationConsumer(store, clock);
        var checks = new Dictionary<string,bool>();
        async Task<SubscriberSnapshot> Sub()
        {
            if (!SubscriberAddress.TryCreate(Guid.NewGuid().ToString("N")+"@example.test",out var address) || address is null) throw new InvalidOperationException();
            return await repo.GetOrCreatePendingAsync(address,now,default);
        }
        async Task<string> Capture(SubscriberSnapshot subscriber)
        {
            if (await issuer.IssueAsync(subscriber.Id,default) != VerificationStageOutcome.Queued) throw new InvalidOperationException();
            var transport = new CaptureSender();
            await new TransactionalEmailDispatcher(queue,protector,config,transport,clock).RunOnceAsync(default);
            return transport.Token ?? throw new InvalidOperationException("Proof not captured in fixture transport.");
        }
        Task<SubscriberVerificationToken> Proof(Guid id) => db.SubscriberVerificationTokens.AsNoTracking().SingleAsync(x=>x.SubscriberId==id);
        if (seed)
        {
            var subscriber = await Sub();
            if (pendingOnly)
            {
                var outcome = await issuer.IssueAsync(subscriber.Id, default);
                Console.WriteLine("{\"passed\":true}");
                return outcome == VerificationStageOutcome.Queued ? 0 : 1;
            }
            var token = await Capture(subscriber);
            var path = Environment.GetEnvironmentVariable("VERIFICATION_TEST_PROOF_FILE") ?? throw new InvalidOperationException();
            // Only the owned fixture receives a transient synthetic proof, never reports/stdout.
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new { token, subscriberId=subscriber.Id }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            Console.WriteLine("{\"passed\":true}"); return 0;
        }
        checks["invalid_canonical_proofs_rejected_without_lookup"] = new string?[] {null,"",new string('a',42),new string('a',44),new string('=',43),new string('a',42)+"B"}.All(x=>!SubscriberVerificationConsumer.TryDigest(x,out _));
        var sub=await Sub(); var raw=await Capture(sub); var proof=await Proof(sub.Id);
        checks["cryptographic_256bit_canonical_token"] = WebEncoders.Base64UrlDecode(raw).Length==32 && raw.Length==43;
        checks["purpose_bound_digest_only"] = proof.Digest!=raw && proof.Digest==SubscriberVerificationRules.Digest(WebEncoders.Base64UrlDecode(raw))
            && proof.Digest!=SubscriberVerificationRules.Digest(WebEncoders.Base64UrlDecode(raw),2);
        checks["exact_86400_expiry"] = proof.ExpiresUtc-proof.CreatedUtc==TimeSpan.FromSeconds(86400);
        checks["address_and_version_bound"] = proof.AddressBinding==SubscriberVerificationRules.AddressBinding(sub.AddressKey) && proof.SubscriberVersion==sub.Version;
        checks["mail_and_proof_single_identity"] = await db.TransactionalEmailJobs.AnyAsync(x=>x.Id==proof.Id && x.SubscriberId==sub.Id);
        checks["fragment_url_not_query"] = links.Create(raw).Contains("#t=") && !links.Create(raw).Contains('?');
        checks["before_expiry_and_single_use"] = await new SubscriberVerificationConsumer(store,new Clock(now.AddSeconds(86399))).ConfirmAsync(raw,default)==VerificationConsumeOutcome.Confirmed
            && await consumer.ConfirmAsync(raw,default)==VerificationConsumeOutcome.Invalid;
        var active=await repo.FindAsync(sub.Id,default);
        checks["confirmed_is_atomic_active_revision"] = active?.Status==SubscriptionStatus.Active && active.Version==2 && (await Proof(sub.Id)).ConsumedUtc==now.AddSeconds(86399);
        checks["active_issuer_does_not_stage_more"] = await issuer.IssueAsync(sub.Id,default)==VerificationStageOutcome.Ineligible;
        var expiredSub=await Sub(); var expired=await Capture(expiredSub);
        checks["exact_expiry_boundary_rejected"] = await new SubscriberVerificationConsumer(store,new Clock(now.AddSeconds(86400))).ConfirmAsync(expired,default)==VerificationConsumeOutcome.Invalid
            && (await repo.FindAsync(expiredSub.Id,default))?.Status==SubscriptionStatus.PendingVerification;
        checks["before_creation_rejected"] = await new SubscriberVerificationConsumer(store,new Clock(now.AddSeconds(-1))).ConfirmAsync(expired,default)==VerificationConsumeOutcome.Invalid;
        var wrong=await Sub(); var wrongRaw=await Capture(wrong); var wrongProof=await Proof(wrong.Id);
        checks["wrong_purpose_rejected"] = await store.ConsumeAsync(wrongProof.Digest ?? throw new InvalidOperationException("Fixture proof missing."),2,now,default)==VerificationConsumeOutcome.Invalid;
        checks["wrong_token_rejected"] = await consumer.ConfirmAsync(WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),default)==VerificationConsumeOutcome.Invalid;
        var bindingRejected = false;
        try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriberVerificationTokens\" SET \"AddressBinding\"={new string('A',64)} WHERE \"Id\"={wrongProof.Id}"); }
        catch (PostgresException error) when (error.SqlState == "23514") { bindingRejected = true; }
        checks["immutable_address_binding_cannot_be_corrupted"] = bindingRejected;
        var suppressedSub=await Sub(); var suppressed=await Capture(suppressedSub);
        await repo.TryApplyAsync(suppressedSub.Id,1,SubscriberChange.SuppressManual,now,default);
        checks["suppression_invalidates_proof"] = await consumer.ConfirmAsync(suppressed,default)==VerificationConsumeOutcome.Invalid;
        var exitedSub=await Sub(); var exited=await Capture(exitedSub);
        await repo.TryApplyAsync(exitedSub.Id,1,SubscriberChange.Unsubscribe,now,default);
        await repo.TryApplyAsync(exitedSub.Id,2,SubscriberChange.RequestReconfirmation,now,default);
        checks["old_version_cannot_confirm_reopened_subscription"] = await consumer.ConfirmAsync(exited,default)==VerificationConsumeOutcome.Invalid;
        var race=await Sub(); var raceRaw=await Capture(race);
        var barrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<VerificationConsumeOutcome> ConsumeRace()
        {
            await using var ctx=new ApplicationDbContext(options);
            var work=new TransactionalEmailStore(ctx); var lifecycle=new SubscriberRepository(ctx);
            var tokens=new SubscriberVerificationStore(ctx,lifecycle,work);
            await barrier.Task;
            return await new SubscriberVerificationConsumer(tokens,clock).ConfirmAsync(raceRaw,default);
        }
        var contenders=Enumerable.Range(0,12).Select(_=>ConsumeRace()).ToArray();barrier.SetResult();
        var results=await Task.WhenAll(contenders);
        checks["twelve_consumers_one_atomic_winner"] = results.Count(x=>x==VerificationConsumeOutcome.Confirmed)==1 && results.Count(x=>x==VerificationConsumeOutcome.Invalid)==11;
        checks["race_increments_revision_once"] = (await repo.FindAsync(race.Id,default))?.Version==2;
        var issueRace=await Sub();
        async Task<VerificationStageOutcome> IssueRace()
        {
            await using var ctx=new ApplicationDbContext(options);
            var work=new TransactionalEmailStore(ctx);var lifecycle=new SubscriberRepository(ctx);
            return await new SubscriberVerificationIssuer(lifecycle,new SubscriberVerificationStore(ctx,lifecycle,work),new SubscriberVerificationMessageFactory(protector,links,new(provider,links)),config,clock).IssueAsync(issueRace.Id,default);
        }
        var issued=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>IssueRace()));
        checks["ten_issuers_one_mail_and_token"] = issued.Count(x=>x==VerificationStageOutcome.Queued)==1
            && await db.TransactionalEmailJobs.CountAsync(x=>x.SubscriberId==issueRace.Id)==1 && await db.SubscriberVerificationTokens.CountAsync(x=>x.SubscriberId==issueRace.Id)==1;
        checks["duplicates_do_not_replace_live_proof"] = issued.Count(x=>x==VerificationStageOutcome.AlreadyPending)==9;
        // Force token INSERT failure after outbox INSERT and prove both are rolled back.
        var rollbackSub=await Sub();var rollbackId=Guid.NewGuid();
        var draft=new SubscriberVerificationDraft(rollbackId,rollbackSub.Id,1,proof.Digest ?? throw new InvalidOperationException("Fixture proof missing."),SubscriberVerificationRules.AddressBinding(rollbackSub.AddressKey),now);
        var mail=protector.Prepare(rollbackId,rollbackSub,"Synthetic","<p>rollback</p>","rollback",now,now.AddDays(1));
        var rejected=false;
        try { await store.StageAsync(draft,mail,default); }
        catch (PostgresException error) when (error.SqlState=="23505") {rejected=true;}
        checks["token_failure_rolls_back_outbox"] = rejected && !await db.TransactionalEmailJobs.AnyAsync(x=>x.Id==rollbackId) && !await db.SubscriberVerificationTokens.AnyAsync(x=>x.Id==rollbackId);
        async Task<bool> Reject(FormattableString sql)
        { try{await db.Database.ExecuteSqlInterpolatedAsync(sql);return false;}catch(PostgresException error)when(error.SqlState=="23514"){return true;} }
        checks["db_rejects_wrong_purpose"] = await Reject($"UPDATE \"SubscriberVerificationTokens\" SET \"Purpose\"=2 WHERE \"Id\"={proof.Id}");
        checks["db_rejects_extended_expiry"] = await Reject($"UPDATE \"SubscriberVerificationTokens\" SET \"ExpiresUtc\"=\"ExpiresUtc\"+interval '1 second' WHERE \"Id\"={proof.Id}");
        checks["db_rejects_consumption_at_expiry"] = await Reject($"UPDATE \"SubscriberVerificationTokens\" SET \"ConsumedUtc\"=\"ExpiresUtc\" WHERE \"Id\"={proof.Id}");
        checks["no_plaintext_token_in_database_envelope"] = ((await db.TransactionalEmailJobs.AsNoTracking().SingleAsync(x=>x.Id==proof.Id)).ProtectedPayload ?? throw new InvalidOperationException("Fixture envelope missing.")).Contains(raw) == false;
        Console.WriteLine(JsonSerializer.Serialize(new {passed=checks.Values.All(x=>x),count=checks.Count,checks}));
        return checks.Values.All(x=>x)?0:1;
    }
    private static async Task<int> LiveAsync(DbContextOptions<ApplicationDbContext> options, IDataProtectionProvider provider)
    {
        var config=new ResendOptions(Environment.GetEnvironmentVariable("RESEND_API_KEY"),"onboarding@resend.dev","Aether Letter Test");
        using var services=new ServiceCollection().AddLogging().AddResendEmail(config).BuildServiceProvider();
        var sender=new CaptureSender(services.GetRequiredService<IEmailSender>());
        await using var db=new ApplicationDbContext(options);
        var subscribers=new SubscriberRepository(db);var queue=new TransactionalEmailStore(db);
        var tokens=new SubscriberVerificationStore(db,subscribers,queue);
        var protection=new TransactionalEmailProtector(provider,config);
        if(!SubscriberAddress.TryCreate("delivered@resend.dev",out var address)||address is null)return 2;
        var sub=await subscribers.GetOrCreatePendingAsync(address,DateTime.UtcNow,default);
        var issuer=new SubscriberVerificationIssuer(subscribers,tokens,new SubscriberVerificationMessageFactory(protection,new SubscriberVerificationLinks("https://aether.example.test"),new(provider,new("https://aether.example.test"))),config,TimeProvider.System);
        var checks=new Dictionary<string,bool>();
        checks["real_verification_proof_and_mail_staged"]=await issuer.IssueAsync(sub.Id,default)==VerificationStageOutcome.Queued;
        await new TransactionalEmailDispatcher(queue,protection,config,sender,TimeProvider.System).RunOnceAsync(default);
        checks["real_resend_acceptance_journaled"]=await db.TransactionalEmailJobs.AnyAsync(x=>x.SubscriberId==sub.Id&&x.State==TransactionalEmailState.Accepted)
            && await db.TransactionalEmailAttempts.AnyAsync(x=>x.Outcome==EmailSendOutcome.Accepted);
        checks["sending_does_not_confirm_ownership"]=(await subscribers.FindAsync(sub.Id,default))?.Status==SubscriptionStatus.PendingVerification;
        // The synthetic transport observer uses the submitted proof locally; no inbox access or delivery claim.
        var consumer=new SubscriberVerificationConsumer(tokens,TimeProvider.System);
        checks["submitted_proof_consumes_once_in_real_database"]=await consumer.ConfirmAsync(sender.Token,default)==VerificationConsumeOutcome.Confirmed
            &&await consumer.ConfirmAsync(sender.Token,default)==VerificationConsumeOutcome.Invalid;
        Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Values.All(x=>x),count=checks.Count,checks,
            scope="One real Resend fixed simulator API call; local synthetic proof observer, no real inbox delivery claim."}));
        return checks.Values.All(x=>x)?0:1;
    }
    private sealed class Clock(DateTime now):TimeProvider {public override DateTimeOffset GetUtcNow()=>new(now);}
    private sealed class CaptureSender(IEmailSender? transport = null):IEmailSender
    {
        public string? Token{get;private set;}
        public Task<EmailSendResult> SendAsync(EmailSendRequest request,CancellationToken cancellationToken=default)
        {Token=Regex.Match(request.Text,@"#t=([A-Za-z0-9_-]{43})").Groups[1].Value;return transport is null ? Task.FromResult(new EmailSendResult(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200)) : transport.SendAsync(request,cancellationToken);}
    }
}
