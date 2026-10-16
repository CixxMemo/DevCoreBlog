#!/usr/bin/env python3
"""Real owned PostgreSQL/MVC aggregate acceptance. Synthetic durable attempts/events, no provider or real secret."""
import argparse,concurrent.futures,datetime,getpass,html,http.cookiejar,json,os,re,socket,subprocess,tempfile,time,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
from campaign_metrics_fixture import SOURCE
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55536');p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
def run(cmd,extra=None,input=None,required=True,timeout=150):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},input=input,capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned metrics command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(n,ok):
 checks[n]=bool(ok);print(n+'='+str(bool(ok)).lower(),flush=True)
 if not ok:raise RuntimeError('Acceptance failed: '+n)
for port in (int(a.port),15236):
 with socket.socket() as s:
  if s.connect_ex(('127.0.0.1',port))==0:raise SystemExit('Owned metrics fixture port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-campaign-metrics-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);process=None;pg_started=False;log=f/'web.log';output=log.open('w');base='http://127.0.0.1:15236';database='campaign_metrics'
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
   document=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'Private F41 body'}]}]}})
   sql('INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (9001,\'Metrics fixture\',\'metrics-fixture\',\'2026-01-01Z\',true,1)')
   sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes")
    VALUES (9001,'Metrik <b> & konu','metrics-fixture','Özet','','Legacy',9001,'2026-01-01Z',true,false,'2030-01-01Z','',7,1,4,1,1,'''+"'"+document+"','Private F41 body',3,1)")
   sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version","PeriodId")
    SELECT gen_random_uuid(),'f41-'||n||'@example.test','f41-'||n||'@example.test',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid() FROM generate_series(1,8) n''')
   capture=f/'sql.txt';gate=Path(str(capture)+'.block');ready=Path(str(capture)+'.ready');session=str(uuid.uuid4())
   def patch(source):
    file=source/'Program.cs';s=file.read_text();assert s.count('options.UseNpgsql(')==1;file.write_text(s.replace('options.UseNpgsql(','options.AddInterceptors(new MetricsSqlCapture()).UseNpgsql('))
    (source/'F41MetricsFixture.cs').write_text(SOURCE)
   source,packet,control,clock,transport_gate=build_fixture_host(root,f,env,patch)
   now=datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0)+datetime.timedelta(seconds=1)
   def move(seconds):clock.write_text((now+datetime.timedelta(seconds=seconds)).isoformat())
   move(0)
   password=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f41-synthetic-password\n').stdout.strip()
   settings={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test','SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password,'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),'NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'true','RESEND_API_KEY':'re_synthetic','RESEND_FROM_EMAIL':'sender@example.test','RESEND_FROM_NAME':'Synthetic','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','READER_LOGIN_ENABLED':'false','SUBSCRIPTIONS_ENABLED':'false','TEST_EMAIL_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false','F41_READER_SESSION':session,'F41_SQL_FILE':str(capture),'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command':'Trace','Logging__LogLevel__Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware':'Trace','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
   class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
   jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar));anon=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));reader=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
   def request(path,method=None,data=None,opener=None):
    req=urllib.request.Request(base+path,method=method,data=urllib.parse.urlencode(data).encode() if data is not None else None)
    try:
     with (opener or client).open(req,timeout=20) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
    except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
   def fixture(name):
    r=request('/fixture-metrics/'+name,method='POST',data={})
    if r[0] not in (200,204):raise RuntimeError('Owned fixture '+name+' status='+str(r[0]))
    return json.loads(r[2]) if r[2] else None
   def values(body):return {k:int(v) for k,v in re.findall(r'data-metric="([^"]+)">([0-9]+)',body)}
   tables=('Subscribers','Posts','NewsletterCampaigns','NewsletterRecipientSelections','NewsletterRecipients','CampaignDispatches','CampaignDeliveryJobs','CampaignDeliveryAttempts','ProviderEmailEvents','ProviderEmailObservations','ProviderAddressSuppressions','ReaderSessions','TransactionalEmailJobs')
   def snapshot():return '\n'.join(sql('SELECT to_jsonb(t)::text FROM "'+t+'" t ORDER BY 1') for t in tables)
   process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(source),'--urls',base],cwd=source,env=settings,stdout=output,stderr=subprocess.STDOUT)
   for _ in range(150):
    if process.poll() is not None:raise RuntimeError('Owned metrics app failed to start')
    try:
     if request('/fixture-admin/login')[0]==200:break
    except OSError:pass
    time.sleep(.1)
   else:raise RuntimeError('Owned metrics readiness failed')
   form=Fields(request('/fixture-admin/login')[2]).values;form.update(username='fixture-admin',password='f41-synthetic-password');check('actual_admin_cookie_login',request('/fixture-admin/login',data=form)[0]==302)
   ids=fixture('setup');main=ids['main'];empty=ids['empty'];path='/AdminCampaignMetrics?id='+main
   initial=request(path);check('pending_retry_is_one_recipient_one_attempt',initial[0]==200 and values(initial[2])['pending']==1 and values(initial[2])['retrying']==1)
   move(30);fixture('retry');move(150);fixture('retry')
   blank=request('/AdminCampaignMetrics?id='+empty);check('unstarted_campaign_has_real_zeros_not_health_claim',blank[0]==200 and all(v==0 for v in values(blank[2]).values()) and 'Henüz sağlayıcı kabulü yok' in html.unescape(blank[2]))
   before_events=request(path);check('accepted_without_observations_is_unknown_not_delivered',values(before_events[2])['accepted']==3 and values(before_events[2])['delivered']==0 and values(before_events[2])['delivery-unknown']==3 and 'bilinmiyor' in html.unescape(before_events[2]))
   fixture('events');response=request(path);v=values(response[2]);expected={'selected':8,'jobs':8,'pending':0,'claimed':1,'sending':1,'accepted':3,'rejected':1,'held':2,'uncertain-held':1,'expired':0,'cancelled':0,'retrying':0,'attempts':8,'unfinished':1,'attempt-accepted':3,'attempt-rejected':1,'attempt-retryable':2,'attempt-uncertain':1,'attempt-unconfigured':0,'delivered':2,'bounced':1,'complained':1,'temporary':1,'delivery-unknown':1}
   check('real_durable_attempt_and_state_matrix',response[0]==200 and v==expected)
   check('three_attempts_are_one_recipient',sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "AttemptCount"=3')=='1' and v['selected']==8 and v['attempts']==8)
   check('provider_join_requires_message_and_frozen_address',sql('SELECT count(*) FROM "ProviderEmailObservations" WHERE "DeliveredUtc" IS NOT NULL')=='4' and v['delivered']==2 and v['delivery-unknown']==1)
   check('independent_delivered_bounce_complaint_facts_overlap',v['delivered']+v['bounced']+v['complained']>v['accepted'])
   fixture('replay');check('duplicate_receipt_and_distinct_repeated_event_do_not_inflate',values(request(path)[2])==v and sql('SELECT count(*) FROM "ProviderEmailEvents"')=='8')
   # A valid reader remains distinct from administrator authorization.
   sql(f'''INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc") SELECT '{session}',"Id","Version","PeriodId",now(),now()+interval '7 days' FROM "Subscribers" WHERE "Suppression"=0 ORDER BY "Id" LIMIT 1''')
   check('valid_reader_can_access_archive',request('/fixture-metrics/reader',opener=reader)[0]==204 and request('/bultenler',opener=reader)[0]==200)
   check('anonymous_and_valid_reader_cannot_read_metrics',all(request(path,opener=o)[0]==404 and not values(request(path,opener=o)[2]) for o in (anon,reader)))
   before=snapshot();variants=[request(path),request(path,method='HEAD'),request('/admincampaignmetrics/?id='+main),request('/AdminCampaignMetrics?id=bad'),request('/AdminCampaignMetrics?id='+str(uuid.uuid4())),request('/AdminCampaignMetrics/Absent')]
   check('success_head_and_errors_keep_private_headers',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') and r[1].get('referrer-policy')=='no-referrer' for r in variants))
   check('invalid_400_missing_404_and_no_post_operation',request('/AdminCampaignMetrics')[0]==400 and variants[3][0]==400 and variants[4][0]==404 and request(path,method='POST',data={})[0] in (400,404,405))
   detail=request('/AdminCampaign/Details/'+main);check('existing_details_links_to_separate_metrics',bool(re.search(r'href="/AdminCampaignMetrics(?:/Index)?(?:/|\?)',detail[2])))
   check('readonly_get_head_preserve_all_lifecycle_and_send_tables',snapshot()==before and not packet.exists())
   check('encoded_subject_no_address_body_or_provider_id_in_page', '&lt;b&gt;' in response[2] and '<b> & konu' not in response[2] and all(x not in response[2] for x in ('f41-','@example.test','Private F41 body','ProtectedPayload')) and not re.search(r'data-metric="(?:opened|read)"',response[2]))
   commands=capture.read_text();check('sql_only_aggregates_metadata_no_sensitive_row_materialization',bool(commands) and all(x not in commands for x in ('"ProtectedPayload"','"ProtectedContext"','"EmailAddress"','"DocumentJson"','"Text"','"SenderEmail"')) and '"AddressKey"' in commands and '"ProviderMessageId"' in commands)
   # The separate writer commits after this request has read campaign metadata.
   gate.touch()
   with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
    future=pool.submit(request,path)
    for _ in range(300):
     if ready.exists():break
     time.sleep(.01)
    if not ready.exists():raise RuntimeError('Metrics concurrency gate not reached')
    fixture('late');gate.unlink();old=future.result(timeout=15)
   check('concurrent_event_cannot_split_one_repeatable_read_observation',values(old[2])==v and values(request(path)[2])['delivered']==3 and values(request(path)[2])['delivery-unknown']==0)
   # Existing frozen bindings remain valid when source lifecycle is later changed.
   sql('UPDATE "Subscribers" SET "Status"=2,"UnsubscribedUtc"=now(),"UpdatedUtc"=greatest(now(),"UpdatedUtc"),"Version"="Version"+1 WHERE "Id"=(SELECT "SubscriberId" FROM "CampaignDeliveryJobs" WHERE "State"=3 ORDER BY "Id" LIMIT 1)')
   check('later_source_exit_does_not_rewrite_frozen_delivery_counts',values(request(path)[2])['delivered']==3 and values(request(path)[2])['accepted']==3)
   if a.browser_module and a.browser_executable:
    r=run(['node',str(root/'scripts/verification/campaign_metrics_browser_probe.cjs')],{'DEVCORE_PLAYWRIGHT_MODULE':str(a.browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(a.browser_executable),'F41_BASE':base,'F41_ID':main,'F41_REPORT_DIR':str(a.report.parent)},required=False)
    if r.returncode:print(r.stdout[-1500:],flush=True)
    browser=json.loads((a.report.parent/'metrics-browser.json').read_text());check('actual_chrome_desktop_mobile_keyboard',r.returncode==0 and browser['passed'])
   cancelled=fixture('cancelled')['id'];cv=values(request('/AdminCampaignMetrics?id='+cancelled)[2]);check('cancelled_work_is_not_counted_as_failure_or_attempt',cv['cancelled']==cv['selected'] and cv['selected']>0 and cv['attempts']==0 and cv['rejected']==0)
   move(86401);fixture('recover')
   form=Fields(request('/fixture-admin/login')[2]).values;form.update(username='fixture-admin',password='f41-synthetic-password')
   check('fresh_admin_session_after_fixture_clock_advanced_one_day',request('/fixture-admin/login',data=form)[0]==302)
   expired=request(path);check('expired_work_remains_separate_from_rejection_and_acceptance',expired[0]==200 and values(expired[2])['expired']==1 and values(expired[2])['accepted']==3 and values(expired[2])['rejected']==1)
   before=snapshot()
   locker=subprocess.Popen(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',database,'-v','ON_ERROR_STOP=1','-c','BEGIN; LOCK TABLE "ProviderEmailObservations" IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(8); ROLLBACK;'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   try:
    for _ in range(100):
     if int(sql('SELECT count(*) FROM pg_locks WHERE relation=\'"ProviderEmailObservations"\'::regclass AND mode=\'AccessExclusiveLock\' AND granted'))>0:break
     time.sleep(.02)
    else:raise RuntimeError('Owned metrics lock not acquired')
    begin=time.monotonic();locked=request(path);elapsed=time.monotonic()-begin
    check('real_sql_lock_deadline_503_without_fake_zeros',locked[0]==503 and not values(locked[2]) and elapsed<6)
   finally:
    locker.wait(timeout=12)
   check('failed_read_rolls_back_without_changing_history',snapshot()==before)
   stop();output.flush();check('trace_overrides_do_not_log_subject_body_address_or_sql',all(x not in log.read_text() for x in ('f41-','Private F41 body','Metrik <b>','SELECT ','F41 correlated')))
   # Restart only the same source copy, then cut the actual database connection.
   process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(source),'--urls',base],cwd=source,env=settings,stdout=output,stderr=subprocess.STDOUT)
   for _ in range(100):
    try:
     if request(path)[0]==200:break
    except OSError:pass
    time.sleep(.1)
   run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop']);pg_started=False;outage=request(path)
   check('real_database_outage_503_without_metrics',outage[0]==503 and not values(outage[2]) and 'no-store' in outage[1].get('cache-control',''))
  finally:
   stop();output.close()
   if pg_started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
 check('owned_cluster_host_and_private_artifacts_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 result={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real isolated PG16/MVC/cookies/read-only SQL/deadline/concurrency. Synthetic durable starts/outcomes/events use existing stores with all guards enabled, no external provider request.'};a.report.write_text(json.dumps(result,indent=2)+'\n')
raise SystemExit(0 if result['passed'] else 1)
