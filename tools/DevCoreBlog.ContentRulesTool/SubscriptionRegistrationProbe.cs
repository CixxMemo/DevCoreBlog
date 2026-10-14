using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Email;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Infrastructure;
using DevCoreBlog.Services.Email;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Durable public admission races and transaction rollback on owned PostgreSQL; BCL limiter checks are separate.</summary>
internal static class SubscriptionRegistrationProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var provider=DataProtectionProvider.Create(new DirectoryInfo(Environment.GetEnvironmentVariable("OUTBOX_TEST_KEYS")??throw new InvalidOperationException()));
        var submission=new ResendOptions("re_synthetic","sender@example.test","Synthetic");
        var messages=new SubscriberVerificationMessageFactory(new(provider,submission),new("https://aether.example.test"),new(provider,new("https://aether.example.test")));
        var now=new DateTime(2026,10,14,12,0,0,DateTimeKind.Utc);
        var checks=new Dictionary<string,bool>();
        await using var db=new ApplicationDbContext(options);
        var repo=new SubscriberRepository(db);var queue=new TransactionalEmailStore(db);var proof=new SubscriberVerificationStore(db,repo,queue);
        var store=new SubscriptionRegistrationStore(db,repo,proof);
        SubscriberAddress Address(string email)=>SubscriberAddress.TryCreate(email,out var value)&&value is not null?value:throw new InvalidOperationException();
        Task<RegistrationWriteOutcome> Register(string email,DateTime? time=null)=>store.RegisterAsync(Address(email),time??now,s=>messages.Prepare(s,time??now),default);
        Task<int> Count()=>db.TransactionalEmailJobs.CountAsync();
        var service=new SubscriptionRegistrationService(store,messages,new(true,true,submission),new Clock(now));
        var baseCount=await Count();
        checks["closed_ingress_does_not_write"]=await new SubscriptionRegistrationService(store,messages,new(false,true,submission),new Clock(now)).RegisterAsync("closed@example.test",default)==SubscriptionRegistrationOutcome.Unavailable&&await Count()==baseCount;
        checks["missing_worker_stays_closed"]=!new SubscriptionRegistrationAvailability(true,false,submission).CanRegister;
        checks["missing_provider_stays_closed"]=!new SubscriptionRegistrationAvailability(true,true,new ResendOptions(null,null,null)).CanRegister;
        checks["invalid_input_no_identity_or_mail"]=await service.RegisterAsync("Name <invalid@example.test>",default)==SubscriptionRegistrationOutcome.InvalidAddress&&await Count()==baseCount;
        var first=await Register("Case.Name+tag@example.test");
        var pending=await db.Subscribers.AsNoTracking().SingleAsync(x=>x.AddressKey=="case.name+tag@example.test");
        checks["first_request_atomic_pending_and_mail"]=first==RegistrationWriteOutcome.Queued&&pending.Status==SubscriptionStatus.PendingVerification&&!pending.IsSendEligible
            &&await db.SubscriberVerificationTokens.AnyAsync(x=>x.SubscriberId==pending.Id)&&await db.TransactionalEmailJobs.AnyAsync(x=>x.SubscriberId==pending.Id);
        var oldCount=await Count();
        checks["case_folded_repeat_no_duplicate"]=await Register("CASE.NAME+TAG@EXAMPLE.TEST")==RegistrationWriteOutcome.NoAction&&await Count()==oldCount;
        var barrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RegistrationWriteOutcome> Race(string email)
        {
            await using var ctx=new ApplicationDbContext(options);var life=new SubscriberRepository(ctx);var work=new TransactionalEmailStore(ctx);
            var tokens=new SubscriberVerificationStore(ctx,life,work);var registration=new SubscriptionRegistrationStore(ctx,life,tokens);
            await barrier.Task;
            return await registration.RegisterAsync(Address(email),now,s=>messages.Prepare(s,now),default);
        }
        var contenders=Enumerable.Range(0,12).Select(_=>Race("same-race@example.test")).ToArray();barrier.SetResult();
        var results=await Task.WhenAll(contenders);
        checks["twelve_public_requests_one_job"]=results.Count(x=>x==RegistrationWriteOutcome.Queued)==1&&results.Count(x=>x==RegistrationWriteOutcome.NoAction)==11;
        checks["twelve_requests_one_identity"]=await db.Subscribers.CountAsync(x=>x.AddressKey=="same-race@example.test")==1;
        var active=await repo.GetOrCreatePendingAsync(Address("active@example.test"),now,default);
        await repo.TryApplyAsync(active.Id,1,SubscriberChange.ConfirmOwnership,now,default);
        checks["active_public_response_generic"]=await service.RegisterAsync("active@example.test",default)==SubscriptionRegistrationOutcome.Accepted;
        checks["active_not_disabled_or_emailed"]=(await repo.FindAsync(active.Id,default))?.Version==2&&!await db.TransactionalEmailJobs.AnyAsync(x=>x.SubscriberId==active.Id);
        var suppressed=await repo.GetOrCreatePendingAsync(Address("suppressed@example.test"),now,default);
        await repo.TryApplyAsync(suppressed.Id,1,SubscriberChange.SuppressComplaint,now,default);
        checks["suppressed_public_response_generic"]=await service.RegisterAsync("suppressed@example.test",default)==SubscriptionRegistrationOutcome.Accepted;
        checks["suppression_never_cleared"]=(await repo.FindAsync(suppressed.Id,default))?.Suppression==SendingSuppression.Complaint&&!await db.TransactionalEmailJobs.AnyAsync(x=>x.SubscriberId==suppressed.Id);
        var exited=await repo.GetOrCreatePendingAsync(Address("exited@example.test"),now,default);
        await repo.TryApplyAsync(exited.Id,1,SubscriberChange.Unsubscribe,now,default);
        checks["explicit_reapplication_only_reopens_pending"]=await Register("exited@example.test")==RegistrationWriteOutcome.Queued
            &&(await repo.FindAsync(exited.Id,default)) is {Status:SubscriptionStatus.PendingVerification,Version:3,VerifiedUtc:null,UnsubscribedUtc:not null};
        // A lifecycle change must not reset the rolling per-address issuance limit.
        await repo.TryApplyAsync(pending.Id,1,SubscriberChange.Unsubscribe,now,default);
        checks["address_limit_survives_lifecycle_version"]=await Register("case.name+tag@example.test")==RegistrationWriteOutcome.NoAction
            &&(await repo.FindAsync(pending.Id,default))?.Status==SubscriptionStatus.Unsubscribed;
        // Failure after creating the pending subscriber rolls back identity, proof, mail and budget together.
        var beforeBudget=await db.SubscriptionRegistrationBudgets.AsNoTracking().SingleAsync();
        var rolled=false;
        try{await store.RegisterAsync(Address("rollback@example.test"),now,_=>throw new InvalidOperationException("Synthetic preparation failure"),default);}
        catch(InvalidOperationException){rolled=true;}
        checks["preparation_failure_rolls_back_identity_and_quota"]=rolled&&!await db.Subscribers.AnyAsync(x=>x.AddressKey=="rollback@example.test")
            &&(await db.SubscriptionRegistrationBudgets.AsNoTracking().SingleAsync()).Count==beforeBudget.Count;
        var budgetBeforeRace=(await db.SubscriptionRegistrationBudgets.AsNoTracking().SingleAsync()).Count;
        var flood=await Task.WhenAll(Enumerable.Range(0,40).Select(i=>Race("budget-"+i+"@example.test")));
        var budget=await db.SubscriptionRegistrationBudgets.AsNoTracking().SingleAsync();
        checks["forty_concurrent_requests_cannot_exceed_daily_twenty"]=budget.Count==20&&flood.Count(x=>x==RegistrationWriteOutcome.Queued)==20-budgetBeforeRace;
        checks["quota_denial_creates_no_extra_identities"]=await db.Subscribers.CountAsync(x=>x.AddressKey.StartsWith("budget-"))==20-budgetBeforeRace;
        var subscribersBefore=await db.Subscribers.CountAsync();
        checks["quota_public_response_generic"]=await service.RegisterAsync("over-budget@example.test",default)==SubscriptionRegistrationOutcome.Accepted
            &&await db.Subscribers.CountAsync()==subscribersBefore;
        await using(var fresh=new ApplicationDbContext(options))
        {
            var life=new SubscriberRepository(fresh);var work=new TransactionalEmailStore(fresh);
            var persisted=new SubscriptionRegistrationStore(fresh,life,new SubscriberVerificationStore(fresh,life,work));
            checks["new_context_cannot_reset_persistent_budget"]=await persisted.RegisterAsync(Address("restart@example.test"),now,s=>messages.Prepare(s,now),default)==RegistrationWriteOutcome.NoAction;
        }
        checks["next_utc_day_and_exact_address_cooldown"]=await Register("case.name+tag@example.test",now.AddDays(1))==RegistrationWriteOutcome.Queued
            &&(await db.SubscriptionRegistrationBudgets.AsNoTracking().SingleAsync()).Count==1;
        checks["reopened_is_still_unverified"]=(await repo.FindAsync(pending.Id,default)) is {Status:SubscriptionStatus.PendingVerification,Version:3,VerifiedUtc:null};
        checks["backward_clock_does_not_reopen_quota"]=await Register("clock-back@example.test")==RegistrationWriteOutcome.NoAction;
        var reject=false;
        try{await db.Database.ExecuteSqlRawAsync("UPDATE \"SubscriptionRegistrationBudgets\" SET \"Count\"=21");}
        catch(PostgresException error)when(error.SqlState=="23514"){reject=true;}
        checks["db_daily_count_constraint"]=reject;
        // Exercise the actual linked Web limiter configuration using BCL leases, not an HTTP/network claim.
        var limits=new RateLimiterOptions();SubscriptionRegistrationRateLimits.Configure(limits);
        using(var limiter=limits.GlobalLimiter??throw new InvalidOperationException())
        {
            DefaultHttpContext Context(bool registration)
            {
                var ctx=new DefaultHttpContext();ctx.Request.Method="POST";
                ctx.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new ControllerActionDescriptor{ControllerName=registration?"SubscriptionRegistration":"Other"}),"fixture"));
                return ctx;
            }
            var leases=Enumerable.Range(0,8).Select(_=>limiter.AttemptAcquire(Context(true))).ToArray();
            using var ninth=limiter.AttemptAcquire(Context(true));
            checks["bcl_global_concurrency_bound_eight"]=leases.All(x=>x.IsAcquired)&&!ninth.IsAcquired;
            foreach(var lease in leases)lease.Dispose();
            using var after=limiter.AttemptAcquire(Context(true));
            checks["bcl_completed_requests_release_capacity"]=after.IsAcquired;
            checks["bcl_other_routes_do_not_share_registration_limit"]=Enumerable.Range(0,80).All(_=>{using var lease=limiter.AttemptAcquire(Context(false));return lease.IsAcquired;});
        }
        Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Values.All(x=>x),count=checks.Count,checks}));
        return checks.Values.All(x=>x)?0:1;
    }
    private sealed class Clock(DateTime now):TimeProvider{public override DateTimeOffset GetUtcNow()=>new(now);}
}
