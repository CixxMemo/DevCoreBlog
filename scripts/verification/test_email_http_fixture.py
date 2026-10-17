"""Real authenticated MVC/SQL journey; only owned source-copy transport faults are simulated."""
import concurrent.futures,datetime,html,http.cookiejar,json,re,signal,socket,stat,subprocess,time,urllib.error,urllib.parse,urllib.request,uuid
from pathlib import Path
from html.parser import HTMLParser
from test_email_fixture_host import build_fixture_host

class Fields(HTMLParser):
 def __init__(self,body):super().__init__();self.values={};self.feed(body)
 def handle_starttag(self,tag,attrs):
  d=dict(attrs)
  if tag=='input' and 'name' in d:self.values[d['name']]=d.get('value','')

def run_http_fixture(root,fixture,base_env,connection,pg_port,report_dir,browser_module,browser_executable,env_file,checks):
 from runtime_schema import ensure_runtime_schema
 ensure_runtime_schema(root,base_env,connection)
 source,packet,control,clock,gate=build_fixture_host(root,fixture,base_env);base='http://127.0.0.1:15233';log=fixture/'web.log';current=datetime.datetime.now(datetime.timezone.utc)
 for port in (15233,):
  with socket.socket() as s:
   if s.connect_ex(('127.0.0.1',port))==0:raise RuntimeError('Owned test-email HTTP port occupied')
 def command(cmd,extra=None,required=True,timeout=40,input=None):
  r=subprocess.run(cmd,cwd=root,env={**base_env,**(extra or {})},capture_output=True,text=True,timeout=timeout,input=input)
  if required and r.returncode:raise RuntimeError('Owned test-email HTTP command failed: '+cmd[0])
  return r
 def sql(q,db='test_email_http'):return command(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
 def snapshot():return sql('SELECT to_jsonb(t)::text FROM "Posts" t ORDER BY 1;SELECT to_jsonb(t)::text FROM "Subscribers" t ORDER BY 1;SELECT to_jsonb(t)::text FROM "NewsletterCampaigns" t ORDER BY 1;SELECT to_jsonb(t)::text FROM "TransactionalEmailJobs" t ORDER BY 1;')
 def count():return len(packet.read_text().splitlines()) if packet.exists() else 0
 def check(name,ok):checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
 def move(seconds):
  nonlocal current
  current+=datetime.timedelta(seconds=seconds);clock.write_text(current.isoformat())
 move(0)
 hashed=command(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f38-synthetic-password\n')
 settings={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test',
  'ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hashed.stdout.strip(),'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),
  'RESEND_API_KEY':'re_synthetic','RESEND_FROM_EMAIL':'sender@example.test','RESEND_FROM_NAME':'Synthetic','TEST_EMAIL_ENABLED':'true','TEST_EMAIL_RECIPIENT':'operator@example.test',
  'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false',
  'RESEND_WEBHOOK_ENABLED':'false','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
 jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
 def request(path,data=None,method=None,raw=None,mime=None,opener=None,headers=None):
  payload=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
  req=urllib.request.Request(base+path,data=payload,method=method,headers={**({'Content-Type':mime} if mime else {}),**(headers or {})})
  try:
   with (opener or client).open(req,timeout=30) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
  except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
 def fields(body):return {k:v for k,v in Fields(body).values.items() if k.startswith("Input.") or k=="__RequestVerificationToken"}
 def login():
  form=fields(request('/fixture-admin/login')[2]);form.update(username='fixture-admin',password='f38-synthetic-password')
  if request('/fixture-admin/login',form)[0]!=200:raise RuntimeError('Owned test-email login failed')
 doc=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'F38 private synthetic Türkçe 👋 <script> yalnız metin'}]}]}},ensure_ascii=False)
 def preview():
  token=fields(request('/AdminWriting/Create')[2])['__RequestVerificationToken']
  return request('/AdminEmail/Preview',{'Input.DocumentJson':doc,'Input.FromEditor':'true','__RequestVerificationToken':token})
 def prepare():
  prior=preview();form=fields(prior[2]);return request('/AdminTestEmail/Prepare',{'Input.DocumentJson':doc,'__RequestVerificationToken':form['__RequestVerificationToken']})
 def form():
  page=prepare()
  if page[0]!=200:raise RuntimeError('Owned test-email preparation failed')
  result=fields(page[2])
  if 'Input.RequestId' not in result:raise RuntimeError('Owned test-email form not ready')
  return result
 process=None;output=log.open('w')
 def stop():
  nonlocal process
  if process is not None and process.poll() is None:
   process.terminate()
   try:process.wait(timeout=10)
   except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
 def start(extra=None,real=False):
  nonlocal process
  stop();assembly=root/'bin/Debug/net10.0/DevCoreBlog.dll' if real else source/'bin/Debug/net10.0/DevCoreBlog.dll'
  process=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(source),'--urls',base],cwd=source,env={**settings,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
  for _ in range(100):
   if process.poll() is not None:raise RuntimeError('Owned test-email application failed to start')
   try:
    if request('/fixture-admin/login')[0]==200:return
   except OSError:pass
   time.sleep(.1)
  raise RuntimeError('Owned test-email application readiness failed')
 try:
  start();login();before=snapshot();initial=count();p=preview();prepared=prepare();ready=fields(prepared[2])
  check('http_preview_and_preparation_do_not_send_or_modify_source',p[0]==200 and prepared[0]==200 and count()==initial and snapshot()==before)
  check('http_single_readonly_recipient_and_explicit_post_form','operator@example.test' in prepared[2] and 'name="Input.Recipient"' not in prepared[2] and 'Test e-postası gönder' in prepared[2])
  for path in ('/AdminTestEmail/Prepare','/AdminTestEmail/Send'):
   r=request(path,{'Input.DocumentJson':doc});check('http_csrf_required_'+path,r[0]==400 and count()==initial and 'no-store' in r[1].get('cache-control',''))
  check('http_get_send_cannot_mutate',request('/AdminTestEmail/Send',method='GET')[0] in (404,405) and count()==initial)
  anon=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()));anon_form=fields(request('/fixture-admin/login',opener=anon)[2])
  check('http_auth_required_even_with_valid_anonymous_csrf',request('/AdminTestEmail/Send',{**ready,'__RequestVerificationToken':anon_form['__RequestVerificationToken']},opener=anon)[0]==404 and count()==initial)
  start();login();ready=form();ready.update({'Input.Recipient':'attacker@example.test','Bcc':'attacker@example.test'});gate.touch()
  with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
   first=pool.submit(request,'/AdminTestEmail/Send',ready)
   for _ in range(100):
    if count()>initial:break
    time.sleep(.025)
   second=request('/AdminTestEmail/Send',ready);gate.unlink();accepted=first.result(timeout=15)
  check('http_concurrency_is_one_with_real_429',second[0]==429 and accepted[0]==200 and count()==initial+1)
  sent=json.loads(packet.read_text().splitlines()[-1]);check('http_overposting_cannot_change_fixed_recipient_or_headers',sent['Recipient']=='operator@example.test' and sent['unsubscribe'] is None and sent['Purpose']==0 and sent['Subject'].startswith('[TEST] ') and '<script>' not in sent['Html'])
  check('http_acceptance_is_explicitly_not_delivery','kabul etti' in accepted[2] and 'teslim edildiği veya okunduğu anlamına gelmez' in html.unescape(accepted[2]))
  replay=request('/AdminTestEmail/Send',ready);check('http_refresh_replay_does_not_resend',replay[0]==200 and count()==initial+1 and 'yeni bir e-posta gönderilmedi' in replay[2])
  check('http_all_test_responses_private_and_strict_csp',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') and "style-src-attr 'none'" in r[1].get('content-security-policy','') for r in (prepared,accepted,replay)))
  check('http_reused_intent_changed_subject_is_conflict',request('/AdminTestEmail/Send',{**ready,'Input.Subject':'Changed'})[0]==409 and count()==initial+1)
  start();login();fresh=form()
  check('http_invalid_document_subject_id_and_content_type_rejected',request('/AdminTestEmail/Send',{**fresh,'Input.DocumentJson':'{'})[0]==400
   and request('/AdminTestEmail/Send',{**fresh,'Input.Subject':'bad\r\nsubject'})[0]==400 and request('/AdminTestEmail/Send',{**fresh,'Input.RequestId':'not-a-guid'})[0]==400
   and request('/AdminTestEmail/Send',{**fresh,'Input.DocumentJson':'x'*1048577})[0]==413
   and request('/AdminTestEmail/Send',raw=b'{}',mime='application/json',headers={'X-CSRF-TOKEN':fresh['__RequestVerificationToken']})[0] in (400,415) and count()==initial+1)
  limited=request('/AdminTestEmail/Send',fresh);check('http_persistent_cooldown_real_429_and_retry_after',limited[0]==429 and int(limited[1].get('retry-after','0'))>0 and count()==initial+1)
  start({'TEST_EMAIL_ENABLED':'false'});login();disabled=prepare();check('http_default_off_has_no_send_button',disabled[0]==200 and 'henüz etkin değil' in disabled[2] and 'Test e-postası gönder</button>' not in disabled[2])
  check('http_disabled_direct_send_fails_closed',request('/AdminTestEmail/Send',fresh)[0]==503 and count()==initial+1)
  move(61);control.write_text('uncertain');start();login();uncertain_form=form();uncertain=request('/AdminTestEmail/Send',uncertain_form)
  check('http_uncertain_result_is_503_and_never_auto_replayed',uncertain[0]==503 and 'kesinleşmedi' in html.unescape(uncertain[2])
   and request('/AdminTestEmail/Send',uncertain_form)[0]==503 and count()==initial+2)
  move(61);control.write_text('accepted');start();login()
  if browser_module:
   browser_env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable) if browser_executable else '',
    'F38_BASE':base,'F38_REPORT_DIR':str(report_dir),'F38_PACKET_FILE':str(packet),'F38_PG_PORT':str(pg_port)}
   result=command(['node',str(root/'scripts/verification/test_email_browser_probe.cjs')],browser_env,False,90)
   browser=json.loads((report_dir/'test-email-browser.json').read_text());check('real_chrome_test_email_acceptance',result.returncode==0 and browser['passed'])
  else:
   # Normal gate measures the third action through actual MVC; Chrome remains a separate explicit acceptance.
   third=request('/AdminTestEmail/Send',form());check('http_third_distinct_test_accepted',third[0]==200)
  check('http_test_flow_has_no_source_subscriber_campaign_or_outbox_write',snapshot()==before)
  move(61);start();login();check('http_fourth_test_hits_rolling_budget',request('/AdminTestEmail/Send',form())[0]==429 and count()==initial+3)
  move(90000);start();login();fault=form();sql('''CREATE FUNCTION f38_finish_fault() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic test finish fault' USING ERRCODE='23514'; END $$; CREATE TRIGGER f38_finish_fault BEFORE UPDATE ON "TestEmailSubmissions" FOR EACH ROW EXECUTE FUNCTION f38_finish_fault();''')
  try:check('http_provider_acceptance_then_storage_fault_is_unknown',request('/AdminTestEmail/Send',fault)[0]==503 and count()==initial+4)
  finally:sql('DROP TRIGGER f38_finish_fault ON "TestEmailSubmissions"; DROP FUNCTION f38_finish_fault()')
  move(61);start();login();check('http_restart_with_incomplete_result_does_not_resend',request('/AdminTestEmail/Send',fault)[0]==503 and count()==initial+4)
  outage=form();command(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'])
  try:check('http_database_outage_is_safe_503_without_provider_call',request('/AdminTestEmail/Send',outage)[0]==503 and count()==initial+4)
  finally:command(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'])
  check('http_outage_does_not_erase_durable_test_results',sql('SELECT count(*) FROM "TestEmailSubmissions"')=='4')
  sql('''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (9801,'Synthetic writing','f38-writing',now(),true,1)''')
  source_count=int(sql('SELECT count(*) FROM "Posts"'));transport_count=count()
  for writing in ('AdminWriting','AdminNewsletter'):
   token=fields(request('/'+writing+'/Create')[2])['__RequestVerificationToken']
   saved=request('/'+writing+'/Create',{'Id':'0','Title':'Synthetic '+writing,'CategoryId':'9801','DocumentJson':doc,'IsActive':'true',
    'PublishDate':'2026-12-01T12:00','SaveAction':'SaveDraft','__RequestVerificationToken':token})
   source_count+=1
   check('http_'+writing+'_save_never_sends_email',saved[0]==200 and int(sql('SELECT count(*) FROM "Posts"'))==source_count and count()==transport_count)
  if env_file:
   if stat.S_IMODE(env_file.stat().st_mode)!=0o600:raise RuntimeError('Owner-only simulator key source required')
   key=None
   for line in env_file.read_text().splitlines():
    k,sep,v=line.partition('=')
    if sep and k.strip()=='RESEND_API_KEY':key=v.strip().strip('\"\'')
   if not key or not key.startswith('re_'):raise RuntimeError('Simulator sending key absent')
   command(['createdb','-h','127.0.0.1','-p',str(pg_port),'test_email_live'])
   live_connection=connection.replace('Database=test_email_http;','Database=test_email_live;')
   command(['dotnet','ef','database','update','20261015065456_TestEmailSubmissions','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':live_connection},timeout=90)
   start({'DB_CONNECTION_STRING':live_connection,'RESEND_API_KEY':key,'RESEND_FROM_EMAIL':'onboarding@resend.dev','TEST_EMAIL_RECIPIENT':'delivered@resend.dev'},real=True);login();live=form()
   response=request('/AdminTestEmail/Send',live);stored=sql('SELECT count(*) FROM "TestEmailSubmissions" WHERE "Outcome"=0 AND "ProviderMessageId" IS NOT NULL','test_email_live')
   check('real_resend_single_simulator_message_through_authenticated_http',response[0]==200 and stored=='1')
   check('real_resend_replay_reuses_saved_result_without_second_submission',request('/AdminTestEmail/Send',live)[0]==200 and sql('SELECT count(*) FROM "TestEmailSubmissions"','test_email_live')=='1')
 finally:
  if gate.exists():gate.unlink()
  stop();output.close()
 raw=log.read_text();check('http_logs_do_not_contain_document_recipient_subject_or_credentials',all(value not in raw for value in ('F38 private synthetic','operator@example.test','attacker@example.test','re_synthetic',doc)))
