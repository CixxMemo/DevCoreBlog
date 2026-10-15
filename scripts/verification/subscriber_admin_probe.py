#!/usr/bin/env python3
"""Real owned PostgreSQL/MVC reader-admin boundary and coherent current counts. No provider calls or real credentials."""
import argparse,concurrent.futures,datetime,getpass,html,http.cookiejar,json,os,re,socket,subprocess,tempfile,time,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55535');p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
def run(cmd,extra=None,input=None,required=True,timeout=150):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},input=input,capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned subscriber command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(n,ok):
 checks[n]=bool(ok);print(n+'='+str(bool(ok)).lower(),flush=True)
 if not ok:raise RuntimeError('Acceptance failed: '+n)
for port in (int(a.port),15235):
 with socket.socket() as s:
  if s.connect_ex(('127.0.0.1',port))==0:raise SystemExit('Owned subscriber fixture port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-subscriber-admin-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);process=None;pg_started=False;log=f/'web.log';output=log.open('w');base='http://127.0.0.1:15235';database='subscriber_admin'
  connection=f'Host=127.0.0.1;Port={a.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',database,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def stop():
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);pg_started=True
   run(['createdb','-h','127.0.0.1','-p',a.port,database]);run(['dotnet','ef','database','update','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection})
   check('real_postgresql16_current_schema',sql('SHOW server_version').startswith('16.'))
   capture=f/'sql.txt';gate=f/'growth.block';ready=f/'growth.ready';session=str(uuid.uuid4())
   def patch(source):
    file=source/'Program.cs';text=file.read_text();assert text.count('options.UseNpgsql(')==1
    file.write_text(text.replace('options.UseNpgsql(','options.AddInterceptors(new SubscriberSqlCapture()).UseNpgsql('))
    code='''using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
internal sealed class SubscriberSqlCapture : DbCommandInterceptor {
 public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default) {
  if(command.CommandText.Contains("F40 subscriber")) await File.AppendAllTextAsync(CAPTURE,command.CommandText+"\\n--NEXT--\\n",ct);
  if(command.CommandText.Contains("F40 subscriber growth") && File.Exists(GATE)) {
   await File.WriteAllTextAsync(READY,"ready",ct);while(File.Exists(GATE))await Task.Delay(10,ct);
  } return result;
 }
}'''
    for key,value in [('CAPTURE',capture),('GATE',gate),('READY',ready)]:code=code.replace(key,json.dumps(str(value)))
    (source/'SubscriberSqlCapture.cs').write_text(code)
    (source/'Controllers/FixtureReaderController.cs').write_text('''using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using DevCoreBlog.Security;
namespace DevCoreBlog.Controllers;
public sealed class FixtureReaderController : Controller {
 [HttpGet("/fixture-reader-cookie")]
 public async Task<IActionResult> Index() {
 var id=Environment.GetEnvironmentVariable("F40_READER_SESSION")??throw new InvalidOperationException();
 var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ReaderCookieAuthenticationEvents.SessionClaim,id.Replace("-",""))},ReaderCookieAuthenticationEvents.Scheme));
 await HttpContext.SignInAsync(ReaderCookieAuthenticationEvents.Scheme,principal,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1),IsPersistent=true,AllowRefresh=false});return NoContent();
 }
}''')
   source,packet,control,clock,transport_gate=build_fixture_host(root,f,env,patch)
   password=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f40-synthetic-password\n').stdout.strip()
   settings={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test','SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password,'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),'NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','READER_LOGIN_ENABLED':'false','SUBSCRIPTIONS_ENABLED':'false','TEST_EMAIL_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false','F40_READER_SESSION':session,'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command':'Trace','Logging__LogLevel__Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware':'Trace'}
   class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
   jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar));reader=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
   def request(path,method=None,data=None,opener=None):
    req=urllib.request.Request(base+path,method=method,data=urllib.parse.urlencode(data).encode() if data is not None else None)
    try:
     with (opener or client).open(req,timeout=20) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
    except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
   def start(extra=None):
    global process
    stop();jar.clear();process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(source),'--urls',base],cwd=source,env={**settings,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
    for _ in range(120):
     if process.poll() is not None:raise RuntimeError('Owned subscriber app failed to start')
     try:
      if request('/fixture-admin/login')[0]==200:break
     except OSError:pass
     time.sleep(.1)
    else:raise RuntimeError('Owned subscriber app readiness failed')
    form=Fields(request('/fixture-admin/login')[2]).values;form.update(username='fixture-admin',password='f40-synthetic-password')
    if request('/fixture-admin/login',data=form)[0]!=302:raise RuntimeError('Owned admin login failed')
   def values(body):return {k:int(v) for k,v in re.findall(r'data-total="([^"]+)">([0-9]+)',body)}
   def addresses(body):return re.findall(r'class="subscriber-address">([^<]+)',body)
   def new_count(body):return int(re.search(r'data-new-addresses>([0-9]+)',body).group(1))
   def snapshot():return '\n'.join(sql('SELECT to_jsonb(t)::text FROM "'+table+'" t ORDER BY 1') for table in ('Subscribers','ProviderAddressSuppressions','ReaderSessions','TransactionalEmailJobs','CampaignDispatches'))
   path='/AdminSubscriber';range_query='?from=2026-10-02&to=2026-10-02'
   start();empty=request(path);check('empty_database_reports_real_zeros',empty[0]==200 and all(n==0 for n in values(empty[2]).values()) and not addresses(empty[2]) and new_count(empty[2])==0)
   # Three current states, source suppression, unchanged old creation after reapplication, and exact local-day boundaries.
   sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","UnsubscribedUtc","Version","PeriodId")
    SELECT gen_random_uuid(),'f40-'||n||'@example.test','f40-'||n||'@example.test',n%3,CASE WHEN n<=3 THEN 1 ELSE 0 END,'2026-01-01Z','2026-10-01Z',CASE WHEN n%3=1 THEN '2026-10-01Z'::timestamptz ELSE NULL END,CASE WHEN n%3=2 THEN '2026-09-01Z'::timestamptz ELSE NULL END,2,gen_random_uuid() FROM generate_series(1,30) n''')
   times=['2026-10-01T20:59:59.999999Z','2026-10-01T21:00:00Z','2026-10-02T20:59:59.999999Z','2026-10-02T21:00:00Z']
   for i,t in enumerate(times):sql(f'''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version","PeriodId") VALUES (gen_random_uuid(),'edge-{i}@example.test','edge-{i}@example.test',0,0,'{t}','{t}',1,gen_random_uuid())''')
   sql('''INSERT INTO "ProviderAddressSuppressions" ("AddressKey","Flags","UpdatedUtc") VALUES ('f40-4@example.test',2,'2026-10-01Z')''')
   sql(f'''INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc") SELECT '{session}',"Id","Version","PeriodId",now(),now()+interval '7 days' FROM "Subscribers" WHERE "EmailAddress"='f40-7@example.test' ''')
   before=snapshot();page=request(path+range_query);totals=values(page[2]);check('global_current_status_partition_and_eligible_counts',page[0]==200 and totals=={'all':34,'pending':14,'active':10,'unsubscribed':10,'blocked':4,'eligible':8})
   check('site_day_exact_start_included_end_excluded',new_count(page[2])==2 and 'net büyüme ölçümü değildir' in html.unescape(page[2]))
   check('old_reapplied_address_not_counted_as_new',sql('SELECT count(*) FROM "Subscribers" WHERE "CreatedUtc"=\'2026-01-01Z\' AND "UpdatedUtc"=\'2026-10-01Z\'')=='30' and new_count(page[2])==2)
   check('paging_bounded_stable_and_disjoint',len(addresses(page[2]))==25 and len(addresses(request(path+range_query+'&page=2')[2]))==9 and not set(addresses(page[2]))&set(addresses(request(path+range_query+'&page=2')[2])) and addresses(page[2])==addresses(request(path+range_query)[2]))
   for status,expected in [('Pending',14),('Active',10),('Unsubscribed',10)]:
    response=request(path+range_query+'&status='+status);check('filter_'+status,len(addresses(response[2]))==expected and values(response[2])==totals and new_count(response[2])==2)
   check('current_source_and_ledger_suppression_shown', 'Gönderim engeli var' in html.unescape(request(path+range_query+'&status=Active')[2]) and sql('SELECT "Suppression" FROM "Subscribers" WHERE "EmailAddress"=\'f40-4@example.test\'')=='0')
   negative=['page=0','page=1001','page=bad','status=999','status=Other','from=bad','from=0001-01-01','to=0001-01-01','to=9999-12-31','from=2026-10-03&to=2026-10-02','from=2024-01-01&to=2026-01-01','from=2026-02-30','from=','from=2026-1-01']
   rejected={q:request(path+'?'+q)[0] for q in negative}
   (a.report.parent/'invalid-inputs.json').write_text(json.dumps(rejected,indent=2)+'\n')
   check('invalid_inputs_are_400_without_write',all(code==400 for code in rejected.values()) and snapshot()==before)
   check('missing_page_404',request(path+'?page=3')[0]==404)
   anon=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
   check('anonymous_has_no_addresses_or_counts',request(path,opener=anon)[0]==404 and 'f40-' not in request(path,opener=anon)[2])
   check('valid_reader_cookie_can_access_archive',request('/fixture-reader-cookie',opener=reader)[0]==204 and request('/bultenler',opener=reader)[0]==200)
   check('valid_reader_cookie_cannot_access_subscriber_inventory',request(path,opener=reader)[0]==404 and 'f40-' not in request(path,opener=reader)[2])
   check('success_head_and_errors_are_private',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') and r[1].get('referrer-policy')=='no-referrer' for r in (page,request(path,method='HEAD'),request(path+'?page=0'),request(path+'/NoSuchAction'))))
   check('get_head_do_not_change_lifecycle_or_send_work',snapshot()==before and not packet.exists())
   sql_text=capture.read_text();commands=[s for s in sql_text.split('--NEXT--') if 'SELECT' in s];address_reads=[s for s in commands if '"EmailAddress"' in s]
   check('sql_addresses_only_in_bounded_projection',bool(address_reads) and all('LIMIT' in s and 'OFFSET' in s and '"PeriodId"' not in s and '"Version"' not in s and '"AddressKey"' not in s.split('FROM')[0] for s in address_reads))
   # A concurrent commit between aggregates and pagination must not split one page across snapshots.
   gate.touch()
   with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
    waiting=pool.submit(request,path+range_query)
    for _ in range(200):
     if ready.exists():break
     time.sleep(.01)
    if not ready.exists():raise RuntimeError('Snapshot concurrency gate not reached')
    sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version","PeriodId") VALUES (gen_random_uuid(),'concurrent@example.test','concurrent@example.test',0,0,'2026-10-02T10:00Z','2026-10-02T10:00Z',1,gen_random_uuid())''');gate.unlink();old=waiting.result(timeout=15)
   check('real_concurrent_write_keeps_one_consistent_read_snapshot',values(old[2])==totals and new_count(old[2])==2 and 'concurrent@example.test' not in old[2] and values(request(path)[2])['all']==35)
   # A fixture-only actual valid address containing HTML metacharacters must remain plain encoded text.
   sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version","PeriodId") VALUES (gen_random_uuid(),'a&b@example.test','a&b@example.test',0,0,now(),now(),1,gen_random_uuid())''')
   check('address_encoding_and_no_email_in_navigation', 'a&amp;b' in request(path)[2] and 'a&b@example.test' in html.unescape(request(path)[2]) and all('@' not in html.unescape(h) for h in re.findall(r'<a\b[^>]*href="([^"]+)"',request(path)[2])))
   if a.browser_module and a.browser_executable:
    result=run(['node',str(root/'scripts/verification/subscriber_admin_browser_probe.cjs')],{'DEVCORE_PLAYWRIGHT_MODULE':str(a.browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(a.browser_executable),'F40_BASE':base,'F40_REPORT_DIR':str(a.report.parent)},required=False)
    if result.returncode:print(result.stdout[-1500:],flush=True)
    data=json.loads((a.report.parent/'subscriber-browser.json').read_text());check('actual_chrome_desktop_mobile_keyboard',result.returncode==0 and data['passed'])
   # New process/timezone proves range conversion, not a browser locale assumption.
   start({'SITE_TIME_ZONE':'America/New_York'});check('site_timezone_conversion_uses_local_calendar_not_utc_date',new_count(request(path+'?from=2026-10-01&to=2026-10-01')[2])==2)
   stop();output.flush();check('framework_overrides_do_not_log_addresses_or_sql',all(s not in log.read_text() for s in ('f40-','edge-','concurrent@example.test','a&b@example.test','SELECT ','F40 subscriber list')))
   start();run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop']);pg_started=False;unavailable=request(path)
   check('database_outage_503_has_no_fake_zero_metrics',unavailable[0]==503 and not values(unavailable[2]) and not addresses(unavailable[2]) and 'alınamıyor' in html.unescape(unavailable[2]))
  finally:
   stop();output.close()
   if pg_started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
 check('owned_cluster_and_host_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real isolated PostgreSQL16/MVC/cookies/snapshot concurrency/site-time boundaries. Fixture reader signer and SQL capture live only in a source copy. No provider request.'};a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
