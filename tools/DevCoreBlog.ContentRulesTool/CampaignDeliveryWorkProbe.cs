using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static partial class CampaignDeliveryProbe
{
    private static async Task WorkTests(Fixture f)
    {
        await f.Single();var lease=await f.Claim();var frozen=await f.Job();f.Time.Advance(TimeSpan.FromMinutes(3));
        f.Check("claimed_but_unstarted_crash_is_recovered_by_actual_restart",await f.Child(1)&&(await f.Job()).State==CampaignDeliveryState.Accepted&&(await f.Job()).AttemptCount==1);
        f.Check("claim_recovery_preserves_exact_ciphertext_and_hash",(await f.Job()).ProtectedPayload==frozen.ProtectedPayload&&(await f.Job()).PayloadHash==frozen.PayloadHash);
        f.Check("stale_claim_owner_cannot_start_after_new_owner",await new CampaignDeliveryWorkStore(f.Db).StartAsync(lease,"unused@example.test",f.Time.Now,default)is null);
        await f.Single();lease=await f.Claim();var start=await f.Start(lease);f.Time.Advance(TimeSpan.FromMinutes(3));
        f.Check("expired_sender_cannot_finish_even_before_recovery",!await f.Finish(lease,start,new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200))
            &&(await f.Job()).State==CampaignDeliveryState.Sending);
        f.Check("sending_crash_restarts_as_held_with_zero_replay",await f.Child(0)&&(await f.Job()).State==CampaignDeliveryState.Held
            &&(await f.Job()).Problem==CampaignDeliveryProblem.LeaseLost&&(await f.Job()).AttemptCount==1);
        f.Check("recovered_attempt_is_uncertain_and_stale_completion_rejected",await f.Db.CampaignDeliveryAttempts.AnyAsync(a=>a.JobId==lease.Id&&a.Outcome==EmailSendOutcome.Uncertain)
            &&!await f.Finish(lease,start,new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200)));
        await f.Single();lease=await f.Claim();
        await f.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION attempt_fixture_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic attempt failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER attempt_fixture_fail BEFORE INSERT ON "CampaignDeliveryAttempts" FOR EACH ROW EXECUTE FUNCTION attempt_fixture_fail();
            """);
        var failed=false;
        try{await f.Start(lease);}catch(PostgresException e)when(e.SqlState=="23514"){failed=true;}
        finally{await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER attempt_fixture_fail ON \"CampaignDeliveryAttempts\"; DROP FUNCTION attempt_fixture_fail()");}
        f.Check("durable_start_fault_rolls_back_sending_counter_and_attempt",failed&&(await f.Job()).State==CampaignDeliveryState.Claimed&&(await f.Job()).AttemptCount==0
            &&!await f.Db.CampaignDeliveryAttempts.AnyAsync(a=>a.JobId==lease.Id));
        f.Time.Advance(TimeSpan.FromMinutes(3));f.Check("start_fault_restart_makes_only_one_later_attempt",await f.Child(1));
        await f.Single();var sender=new Sender();
        await f.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION finish_fixture_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic finish failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER finish_fixture_fail BEFORE UPDATE ON "CampaignDeliveryAttempts" FOR EACH ROW EXECUTE FUNCTION finish_fixture_fail();
            """);
        failed=false;
        try{await f.Dispatcher(f.Db,sender).RunOnceAsync(default);}catch(PostgresException e)when(e.SqlState=="23514"){failed=true;}
        finally{await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER finish_fixture_fail ON \"CampaignDeliveryAttempts\"; DROP FUNCTION finish_fixture_fail()");}
        f.Check("provider_acceptance_followed_by_db_fault_remains_sending",failed&&sender.Requests.Count==1&&(await f.Job()).State==CampaignDeliveryState.Sending&&(await f.Job()).ProviderMessageId is null);
        f.Time.Advance(TimeSpan.FromMinutes(3));f.Check("acceptance_db_fault_restart_is_held_never_synthetic_success_or_replay",await f.Child(0)&&(await f.Job()).State==CampaignDeliveryState.Held);
        await f.Single();sender=new Sender();sender.Results.Enqueue(new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,null,429,TimeSpan.FromSeconds(120)));
        await f.Dispatcher(f.Db,sender).RunOnceAsync(default);var retry=await f.Job();
        f.Check("definitive_429_schedules_bounded_retry_without_immediate_replay",retry.State==CampaignDeliveryState.Pending&&retry.DueUtc==f.Time.Now.AddSeconds(120)
            &&!await f.Dispatcher(f.Db,sender).RunOnceAsync(default)&&sender.Requests.Count==1);
        f.Time.Now=retry.DueUtc;await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
        var first=sender.Requests[0];var second=sender.Requests[1];
        f.Check("retry_preserves_payload_key_first_attempt_and_both_exit_links",first.Html==second.Html&&first.Text==second.Text&&first.Subject==second.Subject
            &&first.IdempotencyKey==second.IdempotencyKey&&first.FirstAttemptUtc==second.FirstAttemptUtc&&first.Unsubscribe==second.Unsubscribe
            &&first.Purpose==EmailPurpose.Newsletter&&first.Html.Contains("/abonelik/cik#t=",StringComparison.Ordinal)&&first.Unsubscribe?.AbsolutePath=="/abonelik/tek-tik"
            &&(await f.Job()).AttemptCount==2&&(await f.Job()).State==CampaignDeliveryState.Accepted);
        var token=first.Unsubscribe?.Query[3..];
        f.Check("oneclick_and_human_proofs_are_purpose_isolated",f.One.TryRead(token,out var proof)&&proof is not null&&!f.Human.TryRead(token,out _));
        await f.Single();sender=new Sender();for(var i=0;i<5;i++)sender.Results.Enqueue(new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,null,429));
        for(var i=0;i<5;i++){await f.Dispatcher(f.Db,sender).RunOnceAsync(default);if((await f.Job()).State==CampaignDeliveryState.Pending)f.Time.Now=(await f.Job()).DueUtc;}
        f.Check("five_attempt_limit_never_generates_sixth_call",sender.Requests.Count==5&&(await f.Job()).State==CampaignDeliveryState.Held
            &&(await f.Job()).Problem==CampaignDeliveryProblem.AttemptsExhausted&&!await f.Dispatcher(f.Db,sender).RunOnceAsync(default));
        await f.Single();sender=new Sender();sender.Results.Enqueue(new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,null,429));
        await f.Dispatcher(f.Db,sender).RunOnceAsync(default);f.Time.Advance(TimeSpan.FromHours(23));await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
        f.Check("original_23_hour_key_window_prevents_late_retry",sender.Requests.Count==1&&(await f.Job()).State==CampaignDeliveryState.Held);
        foreach(var result in new[]{new EmailSendResult(EmailSendOutcome.Uncertain,EmailSendReason.Transport),new(EmailSendOutcome.Accepted,EmailSendReason.None),
            new(EmailSendOutcome.Retryable,EmailSendReason.MonthlyQuota,null,429),new(EmailSendOutcome.Rejected,EmailSendReason.Validation,null,422),
            new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,null,429,TimeSpan.MaxValue)})
        {
            await f.Single();sender=new Sender();sender.Results.Enqueue(result);await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
            var expected=result.Outcome==EmailSendOutcome.Rejected?CampaignDeliveryState.Rejected:result.RetryAfter==TimeSpan.MaxValue?CampaignDeliveryState.Expired:CampaignDeliveryState.Held;
            f.Check("provider_"+result.Outcome+"_"+result.Reason+"_"+(result.RetryAfter==TimeSpan.MaxValue?"overflow":"normal"),sender.Requests.Count==1&&(await f.Job()).State==expected
                &&!await f.Dispatcher(f.Db,sender).RunOnceAsync(default));
        }
        await f.Single();sender=new Sender{ThrowCancellation=true};await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
        f.Check("transport_timeout_cancellation_holds_instead_of_replaying",sender.Requests.Count==1&&(await f.Job()).State==CampaignDeliveryState.Held&&!await f.Dispatcher(f.Db,sender).RunOnceAsync(default));
        await f.Single();sender=new Sender();using(var cancelled=new CancellationTokenSource())
        {cancelled.Cancel();var stopped=false;try{await f.Dispatcher(f.Db,sender).RunOnceAsync(cancelled.Token);}catch(OperationCanceledException){stopped=true;}
            f.Check("cancellation_before_claim_creates_no_attempt",stopped&&sender.Requests.Count==0&&(await f.Job()).AttemptCount==0);}
        var changed=new ResendOptions("re_changed","sender@example.test","Synthetic");await f.Dispatcher(f.Db,sender,changed).RunOnceAsync(default);
        f.Check("credential_rotation_holds_frozen_work_without_transport",sender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Held);
        await f.Single();sender=new Sender();await f.Dispatcher(f.Db,sender,keys:f.UnrelatedKeys()).RunOnceAsync(default);
        f.Check("missing_keyring_holds_instead_of_rebuilding_message",sender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Held);
        await f.Single();f.Time.Advance(TimeSpan.FromDays(1));sender=new Sender();await f.Dispatcher(f.Db,sender).RunOnceAsync(default);
        f.Check("expired_work_makes_no_provider_call",sender.Requests.Count==0&&(await f.Job()).State==CampaignDeliveryState.Expired);
        await f.Single();lease=await f.Claim();start=await f.Start(lease);
        f.Check("start_without_attempt_and_fake_acceptance_cannot_commit",await f.Reject($"UPDATE \"CampaignDeliveryJobs\" SET \"State\"=3,\"ProviderMessageId\"={Guid.NewGuid()},\"LeaseId\"=NULL,\"LeaseUntilUtc\"=NULL,\"Version\"=\"Version\"+1 WHERE \"Id\"={lease.Id}"));
        f.Time.Advance(TimeSpan.FromMinutes(3));await new CampaignDeliveryWorkStore(f.Db).RecoverAsync(f.Time.Now,default);
        f.Check("held_history_cannot_be_reactivated_rekeyed_or_rewritten",await f.Reject($"UPDATE \"CampaignDeliveryJobs\" SET \"State\"=0,\"Version\"=\"Version\"+1 WHERE \"Id\"={lease.Id}")
            &&await f.Reject($"UPDATE \"CampaignDeliveryAttempts\" SET \"Outcome\"=0,\"ProviderMessageId\"={Guid.NewGuid()} WHERE \"JobId\"={lease.Id}"));
    }
}
