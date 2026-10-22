#!/usr/bin/env python3
"""Owned real PostgreSQL/MVC/worker acceptance. Transport faults stay in an owned source-copy, never production switches."""
import argparse,concurrent.futures,datetime,getpass,html,http.cookiejar,json,os,re,socket,subprocess,tempfile,time,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55534');p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path);p.add_argument('--env-file',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
def check(n,ok):checks[n]=bool(ok);print(n+'='+str(bool(ok)).lower(),flush=True)
def run(cmd,extra=None,required=True,input=None,timeout=150):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,input=input,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned campaign-admin command failed: '+cmd[0])
 return r
for port in (int(a.port),15234):
 with socket.socket() as s:
  if s.connect_ex(('127.0.0.1',port))==0:raise SystemExit('Owned campaign-admin port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-campaign-admin-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);pg_started=False;process=None;web_log=f/'web.log';output=web_log.open('w');base='http://127.0.0.1:15234'
  database='campaign_admin'
  connection=f'Host=127.0.0.1;Port={a.port};Database=campaign_admin;Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',database,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def stop():
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);pg_started=True
   run(['createdb','-h','127.0.0.1','-p',a.port,'campaign_admin']);run(['dotnet','ef','database','update','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection})
   check('real_postgresql_16_and_current_schema',sql('SHOW server_version').startswith('16.') and sql("SELECT to_regclass('\"CampaignDeliveryJobs\"') IS NOT NULL AND to_regclass('\"TestEmailSubmissions\"') IS NOT NULL")=='t')
   # This canonical synthetic source is SQL-seeded into a new empty database, never a user database.
   document=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'Sentetik bülten <script> 👋'}]}]}},ensure_ascii=False)
   sql('INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (9001,\'Admin fixture\',\'admin-fixture\',\'2026-01-01Z\',true,1)')
   sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes")
    VALUES (9001,'Sentetik konu','admin-fixture','Özet','','Legacy',9001,'2026-01-01Z',true,false,'2030-01-01Z','',7,1,4,1,1,''' + "'"+document.replace("'","''")+"','Sentetik bülten <script> 👋',3,1)")
   def patch(source):
    file=source/'F38Fixture.cs';s=file.read_text().replace('"rejected" =>','"retryable" => new(EmailSendOutcome.Retryable,EmailSendReason.RateLimit,HttpStatus:429,RetryAfter:TimeSpan.FromSeconds(30)),\n            "rejected" =>');file.write_text(s)
   source,packet,control,clock,gate=build_fixture_host(root,f,env,patch);current=datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(seconds=1)
   def move(seconds=0):
    clock.write_text((current+datetime.timedelta(seconds=seconds)).isoformat())
   move()
   hashed=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f39-synthetic-password\n').stdout.strip()
   settings={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test','SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hashed,'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),'RESEND_API_KEY':'re_synthetic','RESEND_FROM_EMAIL':'sender@example.test','RESEND_FROM_NAME':'Sentetik Aether','NEWSLETTER_WORKER_ENABLED':'true','NEWSLETTER_DELIVERY_READY':'true','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','TEST_EMAIL_ENABLED':'false','SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
   class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
   jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar))
   def request(path,data=None,method=None,opener=None,raw=None,mime=None):
    body=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
    req=urllib.request.Request(base+path,data=body,method=method,headers={'Content-Type':mime} if mime else {})
    try:
     with (opener or client).open(req,timeout=30) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
    except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
   def fields(body):return Fields(body).values
   def start(extra=None,real=False):
    global process
    stop();jar.clear();assembly=(root if real else source)/'bin/Debug/net10.0/DevCoreBlog.dll';process=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(source),'--urls',base],cwd=source,env={**settings,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
    for _ in range(150):
     if process.poll() is not None:raise RuntimeError('Owned campaign-admin app did not start')
     try:
      if request('/fixture-admin/login')[0]==200:break
     except OSError:pass
     time.sleep(.1)
    else:raise RuntimeError('Owned campaign-admin app readiness failed')
    form=fields(request('/fixture-admin/login')[2]);form.update(username='fixture-admin',password='f39-synthetic-password')
    if request('/fixture-admin/login',form)[0]!=302:raise RuntimeError('Owned admin sign-in failed')
   def calls():return len(packet.read_text().splitlines()) if packet.exists() else 0
   def wait(predicate):
    for _ in range(160):
     if predicate():return True
     time.sleep(.1)
    return False
   def snapshot():return sql('SELECT to_jsonb(p)::text FROM "Posts" p ORDER BY "Id"')
   def compose():
    r=request('/AdminCampaign/Compose?postId=9001')
    if r[0]!=200:raise RuntimeError('Owned Compose failed '+str(r[0]))
    form=fields(r[2]);form['Decision']='prepare';return form
   def prepare():
    r=request('/AdminCampaign/Prepare',compose())
    if r[0]!=302:raise RuntimeError('Owned Prepare failed '+str(r[0]))
    identity=urllib.parse.parse_qs(urllib.parse.urlparse(r[1]['location']).query).get('id')
    return identity[0] if identity else r[1]['location'].split('/')[-1]
   def detail(id):return request('/AdminCampaign/Details/'+id)
   def action(id,decision='start'):
    return {'Id':id,'Version':'1','Decision':decision,'Confirm':'true','__RequestVerificationToken':fields(detail(id)[2])['__RequestVerificationToken']}
   start();before=snapshot();page=request('/AdminCampaign/Index');form=compose();check('get_and_compose_do_not_write_or_send',calls()==0 and sql('SELECT count(*) FROM "NewsletterCampaigns"')=='0' and before==snapshot())
   inventory=request('/AdminNewsletter');writing=request('/AdminNewsletter/Create')
   check('newsletter_history_and_compose_urls_preserve_readonly_separation',snapshot()==before and 'Abonelere gönder' not in writing[2] and '/AdminCampaign/Compose?postId=' in inventory[2] and bool(re.search(r'href="/AdminCampaign(?:/Index)?\?postId=[1-9]\d*"',inventory[2])) and 'E-posta gönderimi: Başlatılmadı' not in inventory[2])
   # Valid anonymous anti-forgery cannot cross the separate admin policy.
   anon=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));anti=fields(request('/fixture-admin/login',opener=anon)[2])['__RequestVerificationToken']
   check('anonymous_with_valid_csrf_is_denied',request('/AdminCampaign/Prepare',{**form,'__RequestVerificationToken':anti},opener=anon)[0]==404)
   check('all_mutations_require_csrf',all(request('/AdminCampaign/'+p,{} )[0]==400 for p in ('Prepare','Start','Retry','Cancel')))
   check('get_commands_cannot_mutate',all(request('/AdminCampaign/'+p)[0] in (404,405) for p in ('Prepare','Start','Retry','Cancel')))
   check('missing_prepare_decision_cannot_create',request('/AdminCampaign/Prepare',{**form,'Decision':''})[0]==400 and sql('SELECT count(*) FROM "NewsletterCampaigns"')=='0')
   with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:responses=list(pool.map(lambda _:request('/AdminCampaign/Prepare',form),range(2)))
   print('parallel_prepare_statuses='+','.join(str(r[0]) for r in responses),flush=True)
   if any(r[0]!=302 for r in responses):
    (a.report.parent/'prepare-diagnostic.json').write_text(json.dumps({'status':[r[0] for r in responses],'body':[html.unescape(r[2])[html.unescape(r[2]).find('<section class="campaign-page"'):][:1200] for r in responses]}))
   check('parallel_prepare_one_frozen_campaign',all(r[0]==302 for r in responses) and len({r[1].get('location') for r in responses})==1 and sql('SELECT count(*) FROM "NewsletterCampaigns"')=='1')
   id=sql('SELECT "Id" FROM "NewsletterCampaigns"');frozen=sql('SELECT "SnapshotHash" FROM "NewsletterCampaigns"');r=detail(id)
   check('snapshot_private_archive_url_and_encoded_text',r[0]==200 and '/bulten/admin-fixture' in html.unescape(r[2]) and '&lt;script&gt;' in r[2] and '<script> 👋' not in r[2])
   check('preparing_snapshot_creates_no_recipients_dispatch_or_attempts',sql('SELECT (SELECT count(*) FROM "NewsletterRecipients")+(SELECT count(*) FROM "CampaignDispatches")+(SELECT count(*) FROM "CampaignDeliveryAttempts")')=='0' and calls()==0 and snapshot()==before)
   check('missing_start_decision_or_confirmation_cannot_send',request('/AdminCampaign/Start',{**action(id),'Decision':''})[0]==400 and request('/AdminCampaign/Start',{**action(id),'Confirm':'false'})[0]==400 and calls()==0)
   # Preserve immutable snapshot through a real source edit; the source stays a site draft.
   sql('UPDATE "Posts" SET "Title"=\'Düzenlenen konu\',"EditVersion"=2 WHERE "Id"=9001');check('stale_prepare_is_conflict',request('/AdminCampaign/Prepare',{**form,'StartKey':str(uuid.uuid4())})[0]==409)
   sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version","PeriodId")
    SELECT gen_random_uuid(),'f39-'||n||'@example.test','f39-'||n||'@example.test',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid() FROM generate_series(1,2) n''')
   gate.touch();command=action(id)
   with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:responses=list(pool.map(lambda _:request('/AdminCampaign/Start',command),range(2)))
   check('parallel_start_one_dispatch_and_unique_recipient_work',all(r[0]==302 for r in responses) and sql('SELECT count(*) FROM "CampaignDispatches"')=='1' and wait(lambda:calls()==1))
   cancelled=request('/AdminCampaign/Cancel',action(id,'cancel'));gate.unlink();check('cancel_blocks_next_claim_but_preserves_started_call',cancelled[0]==302 and wait(lambda:sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "State"=3')=='1'))
   time.sleep(2);check('cancelled_history_no_new_calls_or_source_rewrite',calls()==1 and sql('SELECT "SnapshotHash" FROM "NewsletterCampaigns" WHERE "Id"=\''+id+"'")==frozen and sql('SELECT "IsPublished" FROM "Posts" WHERE "Id"=9001')=='f')
   r=detail(id);check('cancelled_status_distinguishes_calls_attempts_and_acceptance','İptal edildi' in html.unescape(r[2]) and 'Toplam çağrı denemesi' in html.unescape(r[2]) and 'teslim veya okunma kanıtı değildir' in html.unescape(r[2]))
   start();check('process_restart_and_cancel_replay_do_not_resend',request('/AdminCampaign/Cancel',action(id,'cancel'))[0]==302 and request('/AdminCampaign/Retry',action(id,'retry'))[0]==409 and calls()==1)
   # One recipient for terminal uncertainty; replay must never bypass F37's conservative rules.
   sql('UPDATE "Subscribers" SET "Status"=2,"UnsubscribedUtc"=now(),"UpdatedUtc"=now(),"Version"="Version"+1 WHERE "EmailAddress"=\'f39-2@example.test\'')
   current=datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(seconds=1);move();control.write_text('uncertain');id2=prepare();r=request('/AdminCampaign/Start',action(id2));check('uncertain_work_is_held',r[0]==302 and wait(lambda:sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "State"=5')=='1'))
   total=calls();control.write_text('accepted');start();check('retry_never_rearms_held_or_changes_keys',request('/AdminCampaign/Retry',action(id2,'retry'))[0]==302 and calls()==total and sql('SELECT count(*) FROM "CampaignDispatches"')=='2')
   # Definitive Retryable is retried only by the worker after the original due time with the same body/key.
   current=datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(seconds=1);move();control.write_text('retryable');id3=prepare();r=request('/AdminCampaign/Start',action(id3));check('known_retryable_waits_for_original_schedule',r[0]==302 and wait(lambda:sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "CampaignId"=\''+id3+'\' AND "State"=0 AND "AttemptCount"=1')=='1'))
   count_before=calls();request('/AdminCampaign/Retry',action(id3,'retry'));time.sleep(1);check('manual_retry_does_not_bypass_due_time',calls()==count_before)
   control.write_text('accepted');move(31);check('safe_retry_preserves_same_message_and_key',wait(lambda:sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "CampaignId"=\''+id3+'\' AND "State"=3 AND "AttemptCount"=2')=='1') and len(packet.read_text().splitlines())==count_before+1 and (lambda rows:all(rows[-1][k]==rows[-2][k] for k in ('IdempotencyKey','Html','Text','Recipient','Subject')))([json.loads(l) for l in packet.read_text().splitlines()]))
   # More than one fixed selection page must continue the same campaign, with no early dispatch.
   sql("INSERT INTO \"Subscribers\" (\"Id\",\"EmailAddress\",\"AddressKey\",\"Status\",\"Suppression\",\"CreatedUtc\",\"UpdatedUtc\",\"Version\",\"PeriodId\") SELECT gen_random_uuid(),'pending-'||n||'@example.test','pending-'||n||'@example.test',0,0,'2026-01-01Z','2026-01-01Z',1,gen_random_uuid() FROM generate_series(1,250) n")
   long_id=prepare();r=request('/AdminCampaign/Start',action(long_id));check('selection_is_bounded_before_dispatch',r[0]==302 and sql('SELECT \"ScannedCount\" FROM \"NewsletterRecipientSelections\" WHERE \"CampaignId\"=\''+long_id+"'")=='250' and sql('SELECT count(*) FROM \"CampaignDispatches\" WHERE \"CampaignId\"=\''+long_id+"'")=='0')
   check('explicit_retry_continues_original_selection',request('/AdminCampaign/Retry',action(long_id,'retry'))[0]==302 and sql('SELECT \"ScannedCount\" FROM \"NewsletterRecipientSelections\" WHERE \"CampaignId\"=\''+long_id+"'")=='252' and sql('SELECT count(*) FROM \"CampaignDispatches\" WHERE \"CampaignId\"=\''+long_id+"'")=='1')
   request('/AdminCampaign/Cancel',action(long_id,'cancel'))
   start({'NEWSLETTER_WORKER_ENABLED':'false'});id4=prepare();check('disabled_worker_disables_ui_and_direct_start', 'Abonelere gönderimi başlat</button>' not in detail(id4)[2] and request('/AdminCampaign/Start',action(id4))[0]==503)
   check('disabled_sending_still_allows_cancel',request('/AdminCampaign/Cancel',action(id4,'cancel'))[0]==302)
   check('bounded_paging_and_honest_missing_rows',request('/AdminCampaign/Index?page=0')[0]==400 and request('/AdminCampaign/Index?page=1001')[0]==400 and request('/AdminCampaign/Index?page=2')[0]==404 and detail(str(uuid.uuid4()))[0]==404)
   # Reader cookie names never authorize admin routes; no admin auth yields 404.
   check('anonymous_cannot_read_admin_history',request('/AdminCampaign/Index',opener=anon)[0]==404)
   check('private_headers_survive_success_and_errors',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') for r in (detail(id),request('/AdminCampaign/Start',{}),request('/AdminCampaign/Details/not-guid'),request('/AdminCampaign/NoSuchAction'))))
   attempts_before=sql('SELECT count(*) FROM "CampaignDeliveryAttempts"');limited_form=compose();limited_form['Decision']=''
   rate_responses=[request('/AdminCampaign/Prepare',limited_form) for _ in range(31)]
   check('bounded_post_budget_returns_private_429_without_work',any(r[0]==429 and 'no-store' in r[1].get('cache-control','') for r in rate_responses) and all(r[0] in (400,429) for r in rate_responses) and sql('SELECT count(*) FROM "CampaignDeliveryAttempts"')==attempts_before)
   # An actual browser follows native forms and tests keyboard/mobile/CSP; sender remains simulated.
   if a.browser_module and a.browser_executable:
    start();br=run(['node',str(root/'scripts/verification/campaign_admin_browser_probe.cjs')],{'DEVCORE_PLAYWRIGHT_MODULE':str(a.browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(a.browser_executable),'F39_BASE':base,'F39_REPORT_DIR':str(a.report.parent)},required=False)
    data=json.loads((a.report.parent/'campaign-admin-browser.json').read_text());
    if br.returncode:print(br.stdout[-2000:],flush=True)
    check('actual_chrome_desktop_mobile_keyboard_csp',br.returncode==0 and data['passed'] and all(data['checks'].values()))
   if a.env_file is not None:
    # Existing authorization is only for this one fixed simulator recipient, in a separate owned empty DB.
    import stat
    if a.env_file.is_symlink() or stat.S_IMODE(a.env_file.stat().st_mode)!=0o600:raise RuntimeError('Owner-only local credential file required')
    values={}
    for line in a.env_file.read_text().splitlines():
     if line.startswith('RESEND_API_KEY='):values['RESEND_API_KEY']=line.split('=',1)[1].strip().strip('"').strip("'")
    if not values.get('RESEND_API_KEY','').startswith('re_'):raise RuntimeError('Resend credential unavailable')
    preserved={t:sql('SELECT to_jsonb(t)::text FROM "'+t+'" t WHERE "Id"=9001') for t in ('Categories','Posts')}
    stop();run(['createdb','-h','127.0.0.1','-p',a.port,'campaign_admin_live']);database='campaign_admin_live';connection=connection.replace('Database=campaign_admin;','Database=campaign_admin_live;')
    run(['dotnet','ef','database','update','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection})
    for table,row in preserved.items():sql('INSERT INTO "'+table+'" SELECT * FROM json_populate_record(NULL::"'+table+'",'+"'"+row.replace("'","''")+"')")
    sql("INSERT INTO \"Subscribers\" (\"Id\",\"EmailAddress\",\"AddressKey\",\"Status\",\"Suppression\",\"CreatedUtc\",\"UpdatedUtc\",\"VerifiedUtc\",\"Version\",\"PeriodId\") VALUES (gen_random_uuid(),'delivered@resend.dev','delivered@resend.dev',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid())")
    start({**values,'DB_CONNECTION_STRING':connection,'RESEND_FROM_EMAIL':'onboarding@resend.dev','RESEND_FROM_NAME':'Aether Letter Test'},real=True)
    live_id=prepare();live_form=action(live_id);response=request('/AdminCampaign/Start',live_form)
    check('real_resend_fixed_simulator_campaign_accepted',response[0]==302 and wait(lambda:sql('SELECT count(*) FROM "CampaignDeliveryJobs" WHERE "State"=3 AND "ProviderMessageId" IS NOT NULL')=='1') and sql('SELECT count(*) FROM "CampaignDeliveryAttempts"')=='1')
    check('real_resend_http_replay_reuses_one_dispatch_and_attempt',request('/AdminCampaign/Start',live_form)[0]==302 and sql('SELECT count(*) FROM "CampaignDispatches"')=='1' and sql('SELECT count(*) FROM "CampaignDeliveryAttempts"')=='1')
    stop();values.clear();settings['DB_CONNECTION_STRING']=connection
   stop();output.flush();logs=web_log.read_text();check('no_body_address_key_or_credential_in_framework_logs',all(s not in logs for s in ('re_synthetic','f39-1@example.test','Sentetik bülten','Retained synthetic','ProtectedPayload','SELECT ')))
   # Database outage cannot render fabricated zero metrics.
   start({'NEWSLETTER_WORKER_ENABLED':'false'});run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop']);pg_started=False
   r=detail(id);check('database_outage_is_honest_503',r[0]==503 and 'Toplam çağrı denemesi' not in html.unescape(r[2]))
  finally:
   stop();output.close()
   if pg_started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
 check('owned_cluster_and_host_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 result={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real owned PG16, MVC/antiforgery/auth/native worker/concurrency/restart/cancel boundaries. Synthetic transport faults; optional fixed one-message real Resend simulator acceptance, never mailbox delivery or DKIM evidence.'};a.report.write_text(json.dumps(result,indent=2)+'\n')
raise SystemExit(0 if result['passed'] else 1)
