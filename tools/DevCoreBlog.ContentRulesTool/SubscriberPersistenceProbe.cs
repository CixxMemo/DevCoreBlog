using System.Globalization;
using System.Text.Json;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Exercises lifecycle and SQL invariants on synthetic identities in a disposable database.</summary>
internal static class SubscriberPersistenceProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var checks = new Dictionary<string, bool>();
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new ApplicationDbContext(options);
        var repo = new SubscriberRepository(db);
        SubscriberAddress Address(string value) => SubscriberAddress.TryCreate(value, out var result) && result is not null
            ? result : throw new InvalidOperationException("Invalid synthetic address.");
        checks["strict_single_mailbox_rejects_ambiguous_or_oversized_input"] = new string?[]
        {
            null, "", " a@example.test", "a@example.test ", "a\r\n@example.test", "Name <a@example.test>",
            "a@example.test,b@example.test", "a(comment)@example.test", "\"a\"@example.test", "a..b@example.test",
            ".a@example.test", "a.@example.test", "a@@example.test", "a@-example.test", "a@example-.test",
            "a@local", "a@ex ample.test", "ü@example.test", "a@örnek.test",
            new string('a',65)+"@example.test", "a@"+new string('x',64)+".test", new string('a',255)
        }.All(s => !SubscriberAddress.TryCreate(s, out _));
        // DNS-looking numeric labels are also valid domain syntax; address literals remain forbidden.
        checks["ip_literal_not_supported"] = !SubscriberAddress.TryCreate("a@[127.0.0.1]", out _);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            checks["identity_is_culture_independent_and_preserves_provider_aliases"] = Address("I.Name+Tag@EXAMPLE.TEST").Key == "i.name+tag@example.test"
                && Address("i.name+tag@example.test").Key != Address("iname@example.test").Key;
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var maximum = new string('a',64)+"@"+new string('b',63)+"."+new string('c',63)+"."+new string('d',61);
        checks["exact_254_address_boundary"] = maximum.Length == 254 && SubscriberAddress.TryCreate(maximum,out _)
            && !SubscriberAddress.TryCreate(maximum+"d",out _);
        checks["max_address_round_trip"] = (await repo.GetOrCreatePendingAsync(Address(maximum),now,CancellationToken.None)).AddressKey==maximum;
        var domain = Subscriber.CreatePending(Address("domain@example.test"), now);
        checks["domain_pending_never_sends"] = !domain.IsSendEligible;
        checks["domain_unknown_command_and_nonutc_rejected"] = domain.Apply((SubscriberChange)99,now)==SubscriberTransition.InvalidTransition
            && domain.Apply(SubscriberChange.ConfirmOwnership,DateTime.SpecifyKind(now,DateTimeKind.Unspecified))==SubscriberTransition.InvalidTime
            && domain.Version==1;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SubscriberSnapshot> Register(int index)
        {
            await using var competitor = new ApplicationDbContext(options);
            await start.Task;
            return await new SubscriberRepository(competitor).GetOrCreatePendingAsync(
                Address(index%2==0 ? "Race.Name+tag@example.test" : "race.name+TAG@EXAMPLE.TEST"),now,CancellationToken.None);
        }
        var registrations = Enumerable.Range(0,16).Select(Register).ToArray();
        start.SetResult();
        var identities = await Task.WhenAll(registrations);
        var row = identities[0];
        checks["parallel_registration_produces_one_identity"] = identities.All(x => x.Id == row.Id)
            && await db.Subscribers.CountAsync(x => x.AddressKey==row.AddressKey)==1;
        checks["new_identity_is_pending_unverified_unsuppressed"] = row.Status==SubscriptionStatus.PendingVerification
            && row.VerifiedUtc is null && row.Version==1 && row.Suppression==SendingSuppression.None && !row.IsSendEligible;
        async Task<SubscriberSnapshot> Read() => await repo.FindAsync(row.Id,CancellationToken.None)
            ?? throw new InvalidOperationException("Missing synthetic row.");
        async Task<SubscriberWriteOutcome> Change(SubscriberChange command,long? version=null,DateTime? time=null) =>
            await repo.TryApplyAsync(row.Id,version ?? (await Read()).Version,command,time ?? now.AddMinutes(1),CancellationToken.None);
        string Serialized(SubscriberSnapshot value)=>JsonSerializer.Serialize(value);
        async Task<string> PhysicalVersion()
        {
            await using var conn=new NpgsqlConnection(db.Database.GetConnectionString());await conn.OpenAsync();
            await using var cmd=new NpgsqlCommand("SELECT xmin::text FROM \"Subscribers\" WHERE \"Id\"=@id",conn);
            cmd.Parameters.AddWithValue("id",row.Id);
            return (string?)await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException();
        }
        var physicalBefore=await PhysicalVersion();
        var before=Serialized(await Read());
        checks["repeated_registration_does_not_change_existing_row"] = (await repo.GetOrCreatePendingAsync(Address(row.AddressKey),now.AddDays(1),CancellationToken.None)).Id==row.Id
            && before==Serialized(await Read()) && physicalBefore==await PhysicalVersion();
        checks["confirmation_is_only_activation_transition"] = await Change(SubscriberChange.ConfirmOwnership)==SubscriberWriteOutcome.Changed
            && (await Read()).IsSendEligible && (await Read()).VerifiedUtc==now.AddMinutes(1);
        before=Serialized(await Read());physicalBefore=await PhysicalVersion();
        checks["confirmation_replay_has_no_write"] = await Change(SubscriberChange.ConfirmOwnership,time:now.AddMinutes(2))==SubscriberWriteOutcome.Unchanged
            && before==Serialized(await Read()) && physicalBefore==await PhysicalVersion();
        checks["public_registration_cannot_disable_active_subscription"] = await Change(SubscriberChange.RequestReconfirmation)==SubscriberWriteOutcome.InvalidTransition
            && before==Serialized(await Read());
        checks["stale_confirmation_conflicts"] = await Change(SubscriberChange.ConfirmOwnership,1)==SubscriberWriteOutcome.Conflict;
        checks["bounce_suppresses_verified_address"] = await Change(SubscriberChange.SuppressBounce)==SubscriberWriteOutcome.Changed
            && !(await Read()).IsSendEligible && (await Read()).Status==SubscriptionStatus.Active;
        checks["complaint_and_manual_flags_accumulate"] = await Change(SubscriberChange.SuppressComplaint)==SubscriberWriteOutcome.Changed
            && await Change(SubscriberChange.SuppressManual)==SubscriberWriteOutcome.Changed
            && (await Read()).Suppression==(SendingSuppression)7;
        before=Serialized(await Read());
        checks["suppression_replay_does_not_advance_revision"] = await Change(SubscriberChange.SuppressBounce)==SubscriberWriteOutcome.Unchanged
            && before==Serialized(await Read());
        checks["unsubscribe_removes_send_eligibility_and_keeps_verification_history"] = await Change(SubscriberChange.Unsubscribe)==SubscriberWriteOutcome.Changed
            && (await Read()).Status==SubscriptionStatus.Unsubscribed && (await Read()).VerifiedUtc.HasValue;
        before=Serialized(await Read());
        checks["unsubscribe_replay_is_noop"] = await Change(SubscriberChange.Unsubscribe)==SubscriberWriteOutcome.Unchanged && before==Serialized(await Read());
        checks["old_confirmation_cannot_reopen_unsubscribed"] = await Change(SubscriberChange.ConfirmOwnership)==SubscriberWriteOutcome.InvalidTransition
            && before==Serialized(await Read());
        checks["reregistration_alone_does_not_reopen_unsubscribed"] = (await repo.GetOrCreatePendingAsync(Address(row.AddressKey),now.AddDays(1),CancellationToken.None)).Status==SubscriptionStatus.Unsubscribed;
        checks["explicit_reconfirmation_clears_proof_preserves_suppression_and_exit"] = await Change(SubscriberChange.RequestReconfirmation)==SubscriberWriteOutcome.Changed
            && (await Read()).VerifiedUtc is null && (await Read()).UnsubscribedUtc.HasValue && (await Read()).Suppression==(SendingSuppression)7;
        checks["renewed_ownership_does_not_clear_suppression"] = await Change(SubscriberChange.ConfirmOwnership)==SubscriberWriteOutcome.Changed && !(await Read()).IsSendEligible;
        before=Serialized(await Read());
        checks["backward_clock_and_unknown_transition_preserve_row"] = await Change(SubscriberChange.Unsubscribe,time:now)==SubscriberWriteOutcome.InvalidTime
            && await Change((SubscriberChange)99)==SubscriberWriteOutcome.InvalidTransition && before==Serialized(await Read());
        checks["missing_identity_is_explicit"] = await repo.FindAsync(Guid.NewGuid(),CancellationToken.None) is null
            && await repo.TryApplyAsync(Guid.NewGuid(),1,SubscriberChange.Unsubscribe,now,CancellationToken.None)==SubscriberWriteOutcome.NotFound;
        // Two distinct updates compete for the same revision. The loser retries only after reloading.
        async Task<SubscriberWriteOutcome[]> Compete(Guid id,long version,SubscriberChange a,SubscriberChange b)
        {
            var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<SubscriberWriteOutcome> Write(SubscriberChange change)
            {
                await using var ctx=new ApplicationDbContext(options);await gate.Task;
                return await new SubscriberRepository(ctx).TryApplyAsync(id,version,change,now.AddHours(1),CancellationToken.None);
            }
            var first=Write(a);var second=Write(b);gate.SetResult();return await Task.WhenAll(first,second);
        }
        var candidate=await repo.GetOrCreatePendingAsync(Address("confirm-exit@example.test"),now,CancellationToken.None);
        var results=await Compete(candidate.Id,1,SubscriberChange.ConfirmOwnership,SubscriberChange.Unsubscribe);
        checks["confirmation_exit_race_has_one_winner"] = results.Count(x=>x==SubscriberWriteOutcome.Changed)==1 && results.Count(x=>x==SubscriberWriteOutcome.Conflict)==1;
        var final=await repo.FindAsync(candidate.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        await repo.TryApplyAsync(candidate.Id,final.Version,SubscriberChange.Unsubscribe,now.AddHours(2),CancellationToken.None);
        var exitedRow=await repo.FindAsync(candidate.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        checks["exit_event_retry_converges_without_active_send"] = !exitedRow.IsSendEligible;
        var active=await repo.GetOrCreatePendingAsync(Address("bounce-exit@example.test"),now,CancellationToken.None);
        await repo.TryApplyAsync(active.Id,1,SubscriberChange.ConfirmOwnership,now,CancellationToken.None);
        results=await Compete(active.Id,2,SubscriberChange.SuppressComplaint,SubscriberChange.Unsubscribe);
        final=await repo.FindAsync(active.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        var loser=results[0]==SubscriberWriteOutcome.Conflict ? SubscriberChange.SuppressComplaint : SubscriberChange.Unsubscribe;
        var retry=await repo.TryApplyAsync(active.Id,final.Version,loser,now.AddHours(2),CancellationToken.None);
        final=await repo.FindAsync(active.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        checks["suppression_exit_race_preserves_both_after_retry"] = results.Count(x=>x==SubscriberWriteOutcome.Changed)==1
            && results.Count(x=>x==SubscriberWriteOutcome.Conflict)==1 && retry==SubscriberWriteOutcome.Changed
            && final.Status==SubscriptionStatus.Unsubscribed && final.Suppression==SendingSuppression.Complaint && !final.IsSendEligible;
        foreach(var assignment in new[]{"\"Status\"=99","\"Suppression\"=8","\"Version\"=0","\"AddressKey\"='mismatch'",
            "\"Status\"=1,\"VerifiedUtc\"=NULL","\"Status\"=0,\"VerifiedUtc\"=\"CreatedUtc\"",
            "\"Status\"=2,\"UnsubscribedUtc\"=NULL","\"UpdatedUtc\"=\"CreatedUtc\"-interval '1 day'",
            "\"EmailAddress\"='a..b@example.test',\"AddressKey\"='a..b@example.test'",
            "\"CreatedUtc\"='-infinity'","\"EmailAddress\"='a@-bad.test',\"AddressKey\"='a@-bad.test'"})
        {
            before=Serialized(await Read());var rejected=false;
            // Test-only SQL fragments are hard-coded above; identity remains a parameter.
            await using var conn=new NpgsqlConnection(db.Database.GetConnectionString());await conn.OpenAsync();
            await using var cmd=new NpgsqlCommand("UPDATE \"Subscribers\" SET "+assignment+" WHERE \"Id\"=@id",conn);
            cmd.Parameters.AddWithValue("id",row.Id);
            try{await cmd.ExecuteNonQueryAsync();}
            catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.CheckViolation){rejected=true;}
            checks["db_constraint_"+checks.Count]=rejected && before==Serialized(await Read());
        }
        var tagged=await repo.GetOrCreatePendingAsync(Address("O'Neil+tag@example.test"),now,CancellationToken.None);
        var distinct=await repo.GetOrCreatePendingAsync(Address("oneil@example.test"),now,CancellationToken.None);
        checks["parameterized_apostrophe_and_distinct_aliases"] = tagged.Id!=distinct.Id && tagged.EmailAddress=="O'Neil+tag@example.test";
        var pendingExit=await repo.GetOrCreatePendingAsync(Address("pending-exit@example.test"),now,CancellationToken.None);
        checks["unverified_exit_cannot_become_active"] = await repo.TryApplyAsync(pendingExit.Id,1,SubscriberChange.Unsubscribe,now,CancellationToken.None)==SubscriberWriteOutcome.Changed
            && (await repo.FindAsync(pendingExit.Id,CancellationToken.None)) is {Status:SubscriptionStatus.Unsubscribed,VerifiedUtc:null,IsSendEligible:false};
        var pendingRace=await repo.GetOrCreatePendingAsync(Address("confirm-bounce@example.test"),now,CancellationToken.None);
        results=await Compete(pendingRace.Id,1,SubscriberChange.ConfirmOwnership,SubscriberChange.SuppressBounce);
        final=await repo.FindAsync(pendingRace.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        loser=results[0]==SubscriberWriteOutcome.Conflict ? SubscriberChange.ConfirmOwnership : SubscriberChange.SuppressBounce;
        await repo.TryApplyAsync(pendingRace.Id,final.Version,loser,now.AddHours(2),CancellationToken.None);
        final=await repo.FindAsync(pendingRace.Id,CancellationToken.None) ?? throw new InvalidOperationException();
        checks["confirm_suppression_race_cannot_lose_block"] = results.Count(x=>x==SubscriberWriteOutcome.Changed)==1
            && results.Count(x=>x==SubscriberWriteOutcome.Conflict)==1 && final.Status==SubscriptionStatus.Active
            && final.Suppression==SendingSuppression.Bounce && !final.IsSendEligible;
        // Lifecycle writes compose with a caller-owned transaction (future verification token consumption).
        before=Serialized(await Read());
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            await Change(SubscriberChange.Unsubscribe,time:now.AddDays(1));
            await transaction.RollbackAsync();
        }
        checks["caller_transaction_rollback_restores_whole_state"] = before==Serialized(await Read());
        using var canceled=new CancellationTokenSource();canceled.Cancel();var canceledCount=0;
        try{await repo.FindAsync(row.Id,canceled.Token);}catch(OperationCanceledException){canceledCount++;}
        try{await repo.GetOrCreatePendingAsync(Address("canceled@example.test"),now,canceled.Token);}catch(OperationCanceledException){canceledCount++;}
        try{await repo.TryApplyAsync(row.Id,(await Read()).Version,SubscriberChange.Unsubscribe,now,canceled.Token);}catch(OperationCanceledException){canceledCount++;}
        checks["all_io_propagates_cancellation"] = canceledCount==3 && !await db.Subscribers.AnyAsync(x=>x.AddressKey=="canceled@example.test");
        var report=new {passed=checks.Values.All(x=>x),count=checks.Count,checks};
        Console.WriteLine(JsonSerializer.Serialize(report));
        return report.passed ? 0 : 1;
    }
}
