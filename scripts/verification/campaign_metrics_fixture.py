"""Test-only C# inserted into an owned source copy, never compiled into the application checkout."""
SOURCE = r'''
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Services.Newsletters;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using DevCoreBlog.Security;
using System.Security.Claims;
using System.Data.Common;

public sealed class FixtureMetricsController(NewsletterCampaignService campaigns, CampaignRecipientService recipients,
    CampaignPreparationService preparation, ICampaignDeliveryWorkStore work, IProviderEmailEventStore events,
    ApplicationDbContext db, TimeProvider clock) : Controller
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<Guid> Campaign()
    {
        var r = await campaigns.PrepareAsync(9001,1,Guid.NewGuid(),default);
        return r.Intent?.Id ?? throw new InvalidOperationException("Fixture campaign failed");
    }
    private async Task<Guid> Stage()
    {
        var id=await Campaign();
        if((await recipients.AdvanceAsync(id,default)).Status!=RecipientBatchStatus.Complete
            || await preparation.ArmAsync(id,default)!=CampaignArmOutcome.Armed || !await preparation.PrepareOnceAsync(default))
            throw new InvalidOperationException("Fixture staging failed");
        return id;
    }
    private async Task<CampaignDeliveryStart> Start(CampaignDeliveryLease lease)
    {
        var address=await db.NewsletterRecipients.Where(x=>x.CampaignId==lease.CampaignId && x.SubscriberId==lease.SubscriberId).Select(x=>x.AddressKey).SingleAsync();
        return await work.StartAsync(lease,address,Now,default) ?? throw new InvalidOperationException("Fixture start failed");
    }
    private async Task Finish(CampaignDeliveryLease lease,CampaignDeliveryStart start,EmailSendResult result)
    {
        var decision=CampaignDeliveryRetryPolicy.Decide(lease,start,result,Now);
        if(!await work.CompleteAsync(lease,start,decision,Now,default))throw new InvalidOperationException("Fixture finish failed");
    }
    [HttpPost("/fixture-metrics/setup"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Setup()
    {
        var empty=await Campaign();var main=await Stage();
        var leases=new List<CampaignDeliveryLease>();
        for(int i=0;i<8;i++)leases.Add(await work.ClaimAsync(Now,default) ?? throw new InvalidOperationException());
        for(int i=0;i<7;i++)
        {
            var lease=leases[i];
            if(i==5){await work.HoldAsync(lease,CampaignDeliveryProblem.ConfigurationChanged,Now,default);continue;}
            var start=await Start(lease);
            if(i==6)continue;
            var result=i switch {
                0=>new EmailSendResult(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,HttpStatus:429),
                3=>new(EmailSendOutcome.Rejected,EmailSendReason.Validation,HttpStatus:422),
                4=>new(EmailSendOutcome.Uncertain,EmailSendReason.Timeout),
                _=>new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200)};
            await Finish(lease,start,result);
        }
        return Json(new{empty,main});
    }
    [HttpPost("/fixture-metrics/retry"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Retry()
    {
        var lease=await work.ClaimAsync(Now,default) ?? throw new InvalidOperationException();var start=await Start(lease);
        await Finish(lease,start,lease.AttemptCount==1 ? new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,HttpStatus:429)
            :new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200));return NoContent();
    }
    private async Task<(Guid MessageId,string AddressKey)[]> Accepted()
    {
        var rows=await (from j in db.CampaignDeliveryJobs join r in db.NewsletterRecipients on new{j.CampaignId,j.SubscriberId} equals new{r.CampaignId,r.SubscriberId}
            where j.ProviderMessageId!=null orderby j.Id select new{j.ProviderMessageId,r.AddressKey}).ToArrayAsync();
        return rows.Select(x=>(x.ProviderMessageId ?? throw new InvalidOperationException(),x.AddressKey)).ToArray();
    }
    private Task<ProviderEventOutcome> Event(string id,Guid message,string address,ProviderEmailEventKind kind) =>
        events.ApplyAsync(ProviderEmailEvent.Create(id,new string('A',64),message,address,kind,Now,Now),default);
    [HttpPost("/fixture-metrics/events"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Events()
    {
        var a=await Accepted();if(a.Length!=3)throw new InvalidOperationException();
        await Event("delivered_a",a[0].MessageId,a[0].AddressKey,ProviderEmailEventKind.Delivered);
        await Event("complained_a",a[0].MessageId,a[0].AddressKey,ProviderEmailEventKind.Complained);
        await Event("temporary_a",a[0].MessageId,a[0].AddressKey,ProviderEmailEventKind.TemporaryBounce);
        await Event("bounced_b",a[1].MessageId,a[1].AddressKey,ProviderEmailEventKind.Bounced);
        await Event("delivered_b",a[1].MessageId,a[1].AddressKey,ProviderEmailEventKind.Delivered);
        await Event("wrong_address",a[2].MessageId,"unrelated@example.test",ProviderEmailEventKind.Delivered);
        await Event("wrong_message",Guid.NewGuid(),a[2].AddressKey,ProviderEmailEventKind.Delivered);
        return NoContent();
    }
    [HttpPost("/fixture-metrics/replay"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Replay()
    {
        var a=await Accepted();
        if(await Event("delivered_a",a[0].MessageId,a[0].AddressKey,ProviderEmailEventKind.Delivered)!=ProviderEventOutcome.Duplicate)throw new InvalidOperationException();
        await Event("delivered_a_again",a[0].MessageId,a[0].AddressKey,ProviderEmailEventKind.Delivered);return NoContent();
    }
    [HttpPost("/fixture-metrics/late"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Late(){var a=await Accepted();await Event("late",a[2].MessageId,a[2].AddressKey,ProviderEmailEventKind.Delivered);return NoContent();}
    [HttpPost("/fixture-metrics/cancelled"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancelled()
    {
        var id=await Stage();await campaigns.CancelAsync(id,1,default);await work.RecoverAsync(Now,default);return Json(new{id});
    }
    [HttpPost("/fixture-metrics/recover"),IgnoreAntiforgeryToken]
    public async Task<IActionResult> Recover(){await work.RecoverAsync(Now,default);return NoContent();}
    [HttpGet("/fixture-metrics/reader")]
    public async Task<IActionResult> Reader()
    {
        var id=Environment.GetEnvironmentVariable("F41_READER_SESSION") ?? throw new InvalidOperationException();
        var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ReaderCookieAuthenticationEvents.SessionClaim,id.Replace("-",""))},ReaderCookieAuthenticationEvents.Scheme));
        await HttpContext.SignInAsync(ReaderCookieAuthenticationEvents.Scheme,principal,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1),IsPersistent=true,AllowRefresh=false});return NoContent();
    }
}
internal sealed class MetricsSqlCapture : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
    {
        var path=Environment.GetEnvironmentVariable("F41_SQL_FILE") ?? throw new InvalidOperationException();
        if(command.CommandText.Contains("F41 "))await File.AppendAllTextAsync(path,command.CommandText+"\n--NEXT--\n",ct);
        if(command.CommandText.Contains("F41 correlated") && File.Exists(path+".block")){
            await File.WriteAllTextAsync(path+".ready","ready",ct);while(File.Exists(path+".block"))await Task.Delay(10,ct);
        }
        return result;
    }
}
'''
