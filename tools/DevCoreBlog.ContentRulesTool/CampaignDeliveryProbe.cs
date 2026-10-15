using System.Diagnostics;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Subscriptions;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Email;
using DevCoreBlog.Services.Newsletters;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Real owned PostgreSQL fault/concurrency acceptance; synthetic transport faults are reported separately from provider acceptance.</summary>
internal static partial class CampaignDeliveryProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options,string[] args)
    {
        var checks=new Dictionary<string,bool>();
        try
        {
            await using var db=new ApplicationDbContext(options);
            var root=Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_TEST_ROOT")??"";
            var conn=new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            await db.Database.OpenConnectionAsync();
            using var guard=db.Database.GetDbConnection().CreateCommand();guard.CommandText="SHOW data_directory";
            if(!root.StartsWith("/tmp/devcoreblog-delivery-",StringComparison.Ordinal) || (string?)await guard.ExecuteScalarAsync()!=root+"/pg"
                || conn.Host!="127.0.0.1" || conn.Database?.StartsWith("delivery_",StringComparison.Ordinal)!=true)return 2;
            var clock=new Clock(ReaderIdentityRules.Utc(DateTime.UtcNow));
            if(Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_TEST_NOW") is {} at)clock.Now=DateTime.Parse(at,null,System.Globalization.DateTimeStyles.RoundtripKind);
            var config=new ResendOptions(args.Contains("--campaign-delivery-live")?Environment.GetEnvironmentVariable("RESEND_API_KEY"):"re_synthetic",
                args.Contains("--campaign-delivery-live")?"onboarding@resend.dev":"sender@example.test","Synthetic");
            var protection=DataProtectionProvider.Create(new DirectoryInfo(root+"/keys"),b=>b.SetApplicationName("DevCoreBlog"));
            var links=new SubscriberVerificationLinks(Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_TEST_ORIGIN")??"https://aether.example.test");
            var human=new SubscriberExitLinks(protection,links);var one=new OneClickUnsubscriptionLinks(protection,links);
            var fixture=new Fixture(options,db,clock,config,protection,human,one,checks);
            if(args.Contains("--campaign-delivery-resume"))
            {
                var sender=new Sender();for(var i=0;i<100;i++)if(!await fixture.Dispatcher(db,sender).RunOnceAsync(default))break;
                fixture.Check("restarted_process_finishes_without_replay",sender.Requests.Count==int.Parse(Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_EXPECT_CALLS")??"0"));
            }
            else if(args.Contains("--campaign-delivery-seed"))
            {
                await fixture.Source();await fixture.Subscriber();fixture.Check("previous_schema_source_and_subscriber_preserved",true);
            }
            else if(args.Contains("--campaign-delivery-http-seed"))
            {
                var subscriber=await fixture.Subscriber();
                var url=one.Create(new(subscriber.Id,subscriber.PeriodId,SubscriberVerificationRules.AddressBinding(subscriber.AddressKey)));
                var file=Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_PROOF_FILE")??"";
                if(!Path.GetFullPath(file).StartsWith(root+"/",StringComparison.Ordinal))return 2;
                await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{subscriberId=subscriber.Id,token=new Uri(url).Query[3..],humanToken=new Uri(human.Create(subscriber)).Fragment[3..]}));
                fixture.Check("owned_private_protocol_proof_created",true);
            }
            else if(args.Contains("--campaign-delivery-live"))
            {
                await fixture.Single("delivered@resend.dev");using var http=new HttpClient{BaseAddress=new Uri("https://api.resend.com"),Timeout=TimeSpan.FromSeconds(20)};
                var sent=await fixture.Dispatcher(db,new ResendEmailSender(http,config,clock)).RunOnceAsync(default);
                fixture.Check("one_simulator_message_durably_accepted",sent && (await fixture.Job()).State==CampaignDeliveryState.Accepted && (await fixture.Job()).AttemptCount==1);
                fixture.Check("acceptance_has_provider_id_without_claiming_delivery",(await fixture.Job()).ProviderMessageId is {} id && id!=Guid.Empty);
            }
            else
            {
                await PreparationTests(fixture);
                await WorkTests(fixture);
                await ConcurrencyTests(fixture);
            }
            var passed=checks.Count>0 && checks.Values.All(x=>x);Console.WriteLine(JsonSerializer.Serialize(new{passed,count=checks.Count,checks}));return passed?0:1;
        }
        catch(Exception e)
        {
            var pg=e as PostgresException??e.InnerException as PostgresException;
            Console.WriteLine(JsonSerializer.Serialize(new{passed=false,exceptionType=e.GetType().Name,sqlState=pg?.SqlState,constraint=pg?.ConstraintName,
                lastCheck=checks.LastOrDefault().Key,assertion=e is AssertionFailure?e.Message:null,checks}));return 1;
        }
    }
    private sealed class AssertionFailure(string message):Exception(message);
    private sealed class Clock(DateTime now):TimeProvider {public DateTime Now{get;set;}=now;public override DateTimeOffset GetUtcNow()=>new(Now);public void Advance(TimeSpan by)=>Now+=by;}
    private sealed class Sender: IEmailSender
    {
        public List<EmailSendRequest> Requests{get;}=[];
        public Queue<EmailSendResult> Results{get;}=[];
        public bool ThrowCancellation{get;set;}
        public Func<Task>? BeforeResult{get;set;}
        public async Task<EmailSendResult> SendAsync(EmailSendRequest request,CancellationToken ct)
        {
            Requests.Add(request);if(BeforeResult is {} action)await action();
            if(ThrowCancellation)throw new OperationCanceledException(ct);
            return Results.Count>0?Results.Dequeue():new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200);
        }
    }
    private sealed class Fixture(DbContextOptions<ApplicationDbContext> options,ApplicationDbContext db,Clock clock,ResendOptions config,
        IDataProtectionProvider protection,SubscriberExitLinks human,OneClickUnsubscriptionLinks one,Dictionary<string,bool> checks)
    {
        public DbContextOptions<ApplicationDbContext> Options=>options;
        public ApplicationDbContext Db=>db;public Clock Time=>clock;public ResendOptions Config=>config;public OneClickUnsubscriptionLinks One=>one;
        public SubscriberExitLinks Human=>human;public Guid Campaign{get;private set;}
        public void Check(string name,bool value){checks[name]=value;if(!value)throw new AssertionFailure(name);}
        public CampaignDeliveryProtector Protector(ResendOptions? setting=null,IDataProtectionProvider? keys=null)=>new(keys??protection,setting??config,setting??config,human,one);
        public IDataProtectionProvider UnrelatedKeys()=>DataProtectionProvider.Create(new DirectoryInfo((Environment.GetEnvironmentVariable("CAMPAIGN_DELIVERY_TEST_ROOT")??throw new InvalidOperationException())+"/otherkeys"));
        public CampaignDeliveryReadiness Ready(bool enabled=true,ResendOptions? setting=null)=>new(enabled,setting??config,one);
        public CampaignPreparationService Preparation(ApplicationDbContext context,ResendOptions? setting=null,IDataProtectionProvider? keys=null)=>
            new(new CampaignDispatchStore(context),Protector(setting,keys),Ready(true,setting),setting??config,clock);
        public CampaignDeliveryDispatcher Dispatcher(ApplicationDbContext context,IEmailSender sender,ResendOptions? setting=null,IDataProtectionProvider? keys=null)=>
            new(new CampaignDeliveryWorkStore(context),Preparation(context,setting,keys),Protector(setting,keys),Ready(true,setting),setting??config,sender,clock);
        public async Task<Post> Source(string? largeText=null)
        {
            var category=new Category{Name="Delivery fixture",Slug="delivery-"+Guid.NewGuid().ToString("N"),CreatedDate=clock.Now,IsActive=true};
            db.Categories.Add(category);await db.SaveChangesAsync();
            var json=largeText is null ? """{"version":1,"document":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Private synthetic newsletter"}]}]}}"""
                : "{\"version\":1,\"document\":{\"type\":\"doc\",\"content\":["+string.Join(',',Enumerable.Range(0,10).Select(_=>
                    "{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":"+JsonSerializer.Serialize(largeText,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})+"}]}"))+" ]}}";
            var text=new DocumentTextProducer();var facts=text.Produce(new ContentDocumentValidator().Validate(json).Document??throw new InvalidOperationException());
            var post=new Post{Title="Synthetic newsletter",Slug="delivery-"+Guid.NewGuid().ToString("N"),CategoryId=category.Id,CreatedDate=clock.Now,
                IsActive=true,PublishDate=clock.Now.AddDays(1),ContentKind=PostContentKind.Newsletter,AccessScope=PostAccessScope.Subscribers,Content="Synthetic legacy",
                DocumentVersion=1,DocumentJson=json,DocumentPlainText=facts.PlainText,DocumentWordCount=facts.WordCount,DocumentReadingMinutes=facts.ReadingMinutes};
            db.Posts.Add(post);await db.SaveChangesAsync();db.ChangeTracker.Clear();return post;
        }
        public async Task<SubscriberSnapshot> Subscriber(string? address=null)
        {
            if(!SubscriberAddress.TryCreate(address??"delivery"+Guid.NewGuid().ToString("N")+"@example.test",out var email)||email is null)throw new InvalidOperationException();
            var repo=new SubscriberRepository(db);var sub=await repo.GetOrCreatePendingAsync(email,clock.Now.AddSeconds(-2),default);
            await repo.TryApplyAsync(sub.Id,sub.Version,SubscriberChange.ConfirmOwnership,clock.Now.AddSeconds(-1),default);
            return await repo.FindAsync(sub.Id,default)??throw new InvalidOperationException();
        }
        public async Task<Guid> NewCampaign(Post? preparedSource=null)
        {
            var source=preparedSource??await Source();var validator=new ContentDocumentValidator();var text=new DocumentTextProducer();
            var service=new NewsletterCampaignService(new NewsletterCampaignStore(db),config,new NewsletterCampaignOrigin("https://aether.example.test"),validator,text,new DocumentEmailRenderer(text),clock);
            Campaign=(await service.PrepareAsync(source.Id,1,Guid.NewGuid(),default)).Intent?.Id??throw new InvalidOperationException();return Campaign;
        }
        public async Task Select()
        {while((await new CampaignRecipientStore(db).AdvanceAsync(Campaign,clock.Now,default)).Status==RecipientBatchStatus.Advanced){} }
        public async Task Stage()
        {await Preparation(db).ArmAsync(Campaign,default);for(var i=0;i<100;i++)if(!await Preparation(db).PrepareOnceAsync(default))break;}
        public async Task<SubscriberSnapshot> Single(string? address=null)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            foreach(var old in await db.NewsletterCampaigns.AsNoTracking().Where(c=>c.State==NewsletterCampaignState.Prepared).Select(c=>new{c.Id,c.Version}).ToListAsync())
                await new NewsletterCampaignStore(db).TryCancelAsync(old.Id,old.Version,clock.Now,default);
            for(var i=0;i<100;i++)if(await new CampaignDeliveryWorkStore(db).RecoverAsync(clock.Now,default)==0)break;
            await new CampaignDispatchStore(db).ReadPreparationAsync(clock.Now,default);
            foreach(var sub in await db.Subscribers.AsNoTracking().Where(s=>s.Status!=SubscriptionStatus.Unsubscribed).Select(s=>new{s.Id,s.Version}).ToListAsync())
                await new SubscriberRepository(db).TryApplyAsync(sub.Id,sub.Version,SubscriberChange.Unsubscribe,clock.Now,default);
            var current=await Subscriber(address);await NewCampaign();await Select();await Stage();return current;
        }
        public Task<CampaignDeliveryJob> Job()=>db.CampaignDeliveryJobs.AsNoTracking().SingleAsync(j=>j.CampaignId==Campaign);
        public async Task<CampaignDeliveryLease> Claim()=>await new CampaignDeliveryWorkStore(db).ClaimAsync(clock.Now,default)??throw new InvalidOperationException();
        public async Task<CampaignDeliveryStart> Start(CampaignDeliveryLease lease)=>await new CampaignDeliveryWorkStore(db).StartAsync(lease,
            await db.NewsletterRecipients.Where(r=>r.CampaignId==Campaign && r.SubscriberId==lease.SubscriberId).Select(r=>r.AddressKey).SingleAsync(),clock.Now,default)??throw new InvalidOperationException();
        public Task<bool> Finish(CampaignDeliveryLease lease,CampaignDeliveryStart start,EmailSendResult result)=>new CampaignDeliveryWorkStore(db)
            .CompleteAsync(lease,start,CampaignDeliveryRetryPolicy.Decide(lease,start,result,clock.Now),clock.Now,default);
        public async Task<bool> Child(int expected)
        {
            var si=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true};si.ArgumentList.Add(typeof(CampaignDeliveryProbe).Assembly.Location);
            si.ArgumentList.Add("--campaign-delivery-resume");si.Environment["CAMPAIGN_DELIVERY_TEST_NOW"]=clock.Now.ToString("O");si.Environment["CAMPAIGN_DELIVERY_EXPECT_CALLS"]=expected.ToString();
            using var child=Process.Start(si)??throw new InvalidOperationException();var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));await child.WaitForExitAsync(deadline.Token);
                using var report=JsonDocument.Parse(await output);await error;return child.ExitCode==0&&report.RootElement.GetProperty("passed").GetBoolean();}
            finally{if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();}}
        }
        public async Task<bool> Reject(FormattableString statement,string state="23514")
        {await using var own=new ApplicationDbContext(options);try{await own.Database.ExecuteSqlInterpolatedAsync(statement);return false;}catch(PostgresException e)when(e.SqlState==state){return true;}}
    }
}
