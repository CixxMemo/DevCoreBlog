#!/usr/bin/env python3
"""Owned PG/MVC settings CAS, migration and sender approval. No real settings, credentials or provider calls."""
import argparse,concurrent.futures,datetime,getpass,html,http.cookiejar,json,os,re,socket,subprocess,tempfile,time,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
from html.parser import HTMLParser
class SettingsForm(HTMLParser):
 def __init__(self,body):super().__init__();self.values={};self.active=False;self.textarea=None;self.feed(body)
 def handle_starttag(self,tag,attrs):
  d=dict(attrs)
  if tag=='form':self.active=d.get('action','').endswith('/AdminPublication/Save')
  if self.active and tag=='input' and 'name' in d and 'disabled' not in d and (d.get('type')!='checkbox' or 'checked' in d):self.values[d['name']]=d.get('value','')
  if self.active and tag=='textarea':self.textarea=d.get('name');self.values[self.textarea]=''
 def handle_data(self,data):
  if self.active and self.textarea:self.values[self.textarea]+=data
 def handle_endtag(self,tag):
  if tag=='textarea':self.textarea=None
  if tag=='form':self.active=False
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55537');p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
prior='20261015065456_TestEmailSubmissions';current='20261016061853_PublicationPreferences'
def run(cmd,extra=None,input=None,required=True,timeout=150):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},input=input,capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned publication command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(n,ok):
 checks[n]=bool(ok);print(n+'='+str(bool(ok)).lower(),flush=True)
 if not ok:raise RuntimeError('Acceptance failed: '+n)
for port in (int(a.port),15237):
 with socket.socket() as s:
  if s.connect_ex(('127.0.0.1',port))==0:raise SystemExit('Owned publication port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-publication-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);process=None;pg_started=False;log=f/'web.log';output=log.open('w');base='http://127.0.0.1:15237';database='publication_prior'
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(q,db=None,required=True):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db or database,'-At','-v','ON_ERROR_STOP=1','-c',q],required=required)
  def query(q,db=None):return sql(q,db).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required=required)
  def stop():
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);pg_started=True
   for db in (database,'publication_empty'):run(['createdb','-h','127.0.0.1','-p',a.port,db])
   migrate('publication_empty',current);check('real_pg16_empty_schema_upgrade',query('SHOW server_version').startswith('16.') and query('SELECT count(*) FROM "PublicationSettings"','publication_empty')=='0')
   migrate('publication_empty',prior);check('empty_down_removes_only_new_schema',query("SELECT to_regclass('\"PublicationSettings\"') IS NULL",'publication_empty')=='t');migrate('publication_empty',current)
   migrate(database,prior)
   document=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'Private F42 body'}]}]}})
   query('INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (9001,\'Settings fixture\',\'settings-fixture\',\'2026-01-01Z\',true,1)')
   query('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes")
    VALUES (9001,'Sentetik bülten','settings-fixture','Özet','','Legacy',9001,'2026-01-01Z',true,false,'2030-01-01Z','',7,1,4,1,1,'''+"'"+document+"','Private F42 body',3,1)")
   session=str(uuid.uuid4())
   def patch(source):
    (source/'Controllers/F42ReaderController.cs').write_text('''using System.Security.Claims;using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Authentication;using DevCoreBlog.Security;
public sealed class F42ReaderController:Controller{[HttpGet("/fixture-publication/reader")] public async Task<IActionResult> Index(){var id=Environment.GetEnvironmentVariable("F42_READER_SESSION")??throw new InvalidOperationException();var p=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ReaderCookieAuthenticationEvents.SessionClaim,id.Replace("-",""))},ReaderCookieAuthenticationEvents.Scheme));await HttpContext.SignInAsync(ReaderCookieAuthenticationEvents.Scheme,p,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1),IsPersistent=true,AllowRefresh=false});return NoContent();}}''')
   source,packet,control,clock,transport_gate=build_fixture_host(root,f,env,patch)
   password=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f42-synthetic-password\n').stdout.strip()
   settings={**env,'DB_CONNECTION_STRING':connection(database),'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test','SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password,'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),'NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','RESEND_API_KEY':'re_synthetic','RESEND_FROM_EMAIL':'sender@example.test','RESEND_FROM_NAME':'Synthetic','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','READER_LOGIN_ENABLED':'false','SUBSCRIPTIONS_ENABLED':'false','TEST_EMAIL_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false','F42_READER_SESSION':session,'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command':'Trace','Logging__LogLevel__Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware':'Trace','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
   class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
   jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar));anon=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));reader=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
   def request(path,data=None,method=None,opener=None,raw=None,mime=None):
    req=urllib.request.Request(base+path,method=method,data=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None,headers={'Content-Type':mime} if mime else {})
    try:
     with (opener or client).open(req,timeout=20) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
    except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
   def start(extra=None):
    global process
    stop();jar.clear();process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(source),'--urls',base],cwd=source,env={**settings,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
    for _ in range(150):
     if process.poll() is not None:raise RuntimeError('Owned publication app failed to start')
     try:
      if request('/fixture-admin/login')[0]==200:break
     except OSError:pass
     time.sleep(.1)
    else:raise RuntimeError('Owned publication readiness failed')
    login=Fields(request('/fixture-admin/login')[2]).values;login.update(username='fixture-admin',password='f42-synthetic-password');check('admin_login_'+str(len(checks)),request('/fixture-admin/login',data=login)[0]==302)
   def form():
    r=request('/AdminPublication');v=SettingsForm(r[2]).values
    if r[0]!=200 or 'Cadence' not in v:raise RuntimeError('Owned settings GET did not return a form: '+str(r[0]))
    return v
   def save(data):return request('/AdminPublication/Save',data=data)
   def compose():
    r=request('/AdminCampaign/Compose?postId=9001');v=Fields(r[2]).values;v['Decision']='prepare';return v
   def prepare(v=None):return request('/AdminCampaign/Prepare',data=v or compose())
   def snapshot():return '\n'.join(query('SELECT to_jsonb(t)::text FROM "'+table+'" t ORDER BY 1') for table in ('Posts','NewsletterCampaigns','Subscribers','CampaignDispatches','CampaignDeliveryJobs','CampaignDeliveryAttempts','TransactionalEmailJobs','ReaderSessions'))
   start();legacy_form=compose();legacy=prepare(legacy_form);check('prior_schema_runtime_prepares_legacy_snapshot',legacy[0]==302)
   before=snapshot();history=query('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines();stop();migrate(database,current)
   check('previous_schema_upgrade_preserves_content_and_campaigns',snapshot()==before and query('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
   check('both_new_database_guards_enabled',query("SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_PublicationSettings_Guard','TR_NewsletterCampaigns_PublicationSender') AND tgenabled='O'")=='2')
   start();before=snapshot();page=request('/AdminPublication');check('get_defaults_are_readonly_not_false_provider_verification',page[0]==200 and query('SELECT count(*) FROM "PublicationSettings"')=='0' and snapshot()==before and 'doğrulaması veya gerçek teslim kanıtı değildir' in html.unescape(page[2]))
   f0=form();check('no_csrf_or_get_cannot_save',save({k:v for k,v in f0.items() if k!='__RequestVerificationToken'})[0]==400 and request('/AdminPublication/Save')[0] in (404,405) and query('SELECT count(*) FROM "PublicationSettings"')=='0')
   anti=Fields(request('/fixture-admin/login',opener=anon)[2]).values['__RequestVerificationToken'];check('anonymous_with_valid_csrf_cannot_save',request('/AdminPublication/Save',data={**f0,'__RequestVerificationToken':anti},opener=anon)[0]==404)
   # New and existing row races must not produce two rows or lose one editor's work.
   with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:races=list(pool.map(lambda n:save({**f0,'Name':'Aether & Deney '+str(n),'ApproveSender':'true'}),range(2)))
   check('parallel_first_save_one_row_one_conflict',sorted(r[0] for r in races)==[302,409] and query('SELECT count(*) FROM "PublicationSettings"')=='1' and query('SELECT "Version" FROM "PublicationSettings"')=='1')
   f1=form()
   with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:races=list(pool.map(lambda n:save({**f1,'Cadence':'Hazır olduğunda '+str(n)}),range(2)))
   check('parallel_updates_use_version_CAS',sorted(r[0] for r in races)==[302,409] and query('SELECT "Version" FROM "PublicationSettings"')=='2')
   stable=query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t');same=form();check('unchanged_post_preserves_version_and_timestamp',save(same)[0]==302 and query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t')==stable)
   check('stale_form_rejected_without_overwrite',save({**f1,'Name':'Stale title'})[0]==409 and query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t')==stable)
   bad=[{'Name':''},{'Name':'x'*81},{'Name':'<script>alert(1)</script>'},{'Cadence':'a\nb'},{'Cadence':'x'*201},{'Version':'-1'},{'Version':str(2**63-1)},{'SenderEmail':'intruder@example.test'},{'SenderName':'Forged header'},{'RESEND_API_KEY':'fake-not-a-key'},{'SITE_URL':'https://untrusted.example.test'}]
   rejected=[save({**same,**change})[0] for change in bad];check('invalid_and_forged_sender_secret_origin_inputs_are_400',all(s==400 for s in rejected) and query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t')==stable)
   check('unsupported_json_rejected_before_save',request('/AdminPublication/Save',method='POST',raw=b'{}',mime='application/json')[0] in (400,415))
   approved=prepare();check('approved_configured_sender_allows_new_campaign',approved[0]==302)
   frozen=query('SELECT to_jsonb(t)::text FROM "NewsletterCampaigns" t ORDER BY "Id"');v=form();v.pop('ApproveSender',None);check('revoking_approval_only_updates_preferences',save(v)[0]==302 and query('SELECT to_jsonb(t)::text FROM "NewsletterCampaigns" t ORDER BY "Id"')==frozen)
   revoked=prepare();replay=prepare({**legacy_form,'__RequestVerificationToken':compose()['__RequestVerificationToken']})
   print('revoked_new_status='+str(revoked[0])+' legacy_replay_status='+str(replay[0]),flush=True)
   check('revoked_approval_blocks_new_intent_but_replays_old_key',revoked[0]==503 and replay[0]==302)
   v=form();v['ApproveSender']='true';check('current_sender_can_be_approved_again',save(v)[0]==302)
   # Actual uncommitted revoke holds the exclusive advisory lock. The campaign waits and must see its committed value.
   writer=subprocess.Popen(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',database,'-v','ON_ERROR_STOP=1','-c','BEGIN; SELECT pg_advisory_xact_lock(422026); UPDATE "PublicationSettings" SET "SenderEmail"=NULL,"SenderName"=NULL,"Version"="Version"+1,"UpdatedUtc"=greatest(now(),"UpdatedUtc"); SELECT pg_sleep(2); COMMIT;'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   try:
    for _ in range(100):
     if int(query("SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND objid=422026 AND mode='ExclusiveLock' AND granted"))>0:break
     time.sleep(.02)
    else:raise RuntimeError('Publication revoke lock not acquired')
    check('concurrent_committed_revoke_prevents_late_campaign_insert',prepare()[0]==503)
   finally:writer.wait(timeout=6)
   v=form();v['ApproveSender']='true';check('approval_after_race_is_explicit',save(v)[0]==302)
   # Reader identity is valid, but never grants administrative settings access.
   query('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version","PeriodId") VALUES (gen_random_uuid(),'f42-reader@example.test','f42-reader@example.test',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid())''')
   query(f'''INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc") SELECT '{session}',"Id","Version","PeriodId",now(),now()+interval '7 days' FROM "Subscribers"''')
   check('valid_reader_archive_200_but_settings_denied',request('/fixture-publication/reader',opener=reader)[0]==204 and request('/bultenler',opener=reader)[0]==200 and request('/AdminPublication',opener=reader)[0]==404)
   before=snapshot();responses=[request('/AdminPublication'),request('/AdminPublication',method='HEAD'),request('/adminpublication/'),request('/AdminPublication/Absent'),save({**form(),'Name':'<b>'})]
   check('success_head_case_errors_and_post_errors_are_private',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') and r[1].get('referrer-policy')=='no-referrer' for r in responses))
   check('saves_and_reads_do_not_publish_or_create_send_work',snapshot()==before and not packet.exists())
   if a.browser_module and a.browser_executable:
    r=run(['node',str(root/'scripts/verification/publication_settings_browser_probe.cjs')],{'DEVCORE_PLAYWRIGHT_MODULE':str(a.browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(a.browser_executable),'F42_BASE':base,'F42_REPORT_DIR':str(a.report.parent)},required=False)
    if r.returncode:print(r.stdout[-1500:],flush=True)
    report=json.loads((a.report.parent/'publication-browser.json').read_text());check('real_chrome_desktop_mobile_keyboard',r.returncode==0 and report['passed'])
   preserved=query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t');start({'RESEND_API_KEY':''});missing=request('/AdminPublication');v=form();v.pop('ApproveSender',None)
   check('missing_provider_visible_and_editorial_save_still_works','data-sender-state="missing"' in missing[2] and save({**v,'Name':'Aether & Kayıt'})[0]==302)
   v=form();check('missing_provider_cannot_be_confirmed',save({**v,'ApproveSender':'true'})[0]==400)
   start({'RESEND_FROM_EMAIL':'rotated@example.test'});check('rotated_sender_requires_fresh_approval',prepare()[0]==503)
   v=form();v['ApproveSender']='true';check('configured_rotated_sender_can_be_bound_without_changing_old_snapshots',save(v)[0]==302 and prepare()[0]==302 and query('SELECT count(*) FROM "NewsletterCampaigns" WHERE "SenderEmail"=\'rotated@example.test\'')=='1' and query('SELECT "SnapshotHash" FROM "NewsletterCampaigns" WHERE "StartKey"=\''+legacy_form['StartKey']+"'")!='')
   check('metadata_has_no_credentials_origin_or_enable_switch',query("SELECT count(*) FROM information_schema.columns WHERE table_name='PublicationSettings' AND column_name IN ('ApiKey','Binding','SiteUrl','WorkerEnabled','DeliveryReady')")=='0')
   before=query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t');down=migrate(database,prior,required=False);check('nonempty_down_refuses_without_losing_preferences_or_guards',down.returncode!=0 and query('SELECT to_jsonb(t)::text FROM "PublicationSettings" t')==before and query("SELECT count(*) FROM pg_trigger WHERE tgname='TR_NewsletterCampaigns_PublicationSender' AND tgenabled='O'")=='1')
   check('database_guard_rejects_delete_and_version_rewind',sql('DELETE FROM "PublicationSettings"',required=False).returncode!=0 and sql('UPDATE "PublicationSettings" SET "Version"=1',required=False).returncode!=0)
   v=form();rate=[save({**v,'Name':''}) for _ in range(32)];check('bounded_post_rate_returns_private_429',any(r[0]==429 and 'no-store' in r[1].get('cache-control','') for r in rate))
   stop();output.flush();check('trace_override_logs_no_settings_body_sender_or_SQL',all(s not in log.read_text() for s in ('sender@example.test','rotated@example.test','Private F42 body','Aether &','SELECT ','fake-not-a-key')))
   start({'RESEND_FROM_EMAIL':'rotated@example.test'});run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop']);pg_started=False;r=request('/AdminPublication');check('database_outage_503_does_not_fabricate_default_settings',r[0]==503 and 'name="Name"' not in r[2] and 'no-store' in r[1].get('cache-control',''))
  finally:
   stop();output.close()
   if pg_started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
 check('owned_cluster_host_keys_and_private_files_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 result={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real isolated PG16/MVC/migrations/admin+reader cookies/CAS/approval race. Fixture signer and sender exist only in owned source copy. No provider request.'};a.report.write_text(json.dumps(result,indent=2)+'\n')
raise SystemExit(0 if result['passed'] else 1)
