"""Owned source copy injects a clearly simulated mailbox; production registration remains untouched."""
import http.cookiejar,json,os,re,shutil,signal,subprocess,time,urllib.error,urllib.parse,urllib.request
from pathlib import Path
from newsletter_archive_http_fixture import run_archive_checks

def run_http_fixture(root,fixture,base_env,connection,pg_port,report_dir,checks,browser_module,browser_executable):
 from runtime_schema import ensure_runtime_schema
 ensure_runtime_schema(root,base_env,connection)
 source=fixture/'source';source.mkdir();mailbox=fixture/'mailbox.jsonl';log_file=fixture/'web.log';proof_file=fixture/'proof.json'
 base='http://127.0.0.1:15226';proofs=[];addresses=[]
 excludes=['.local','.git','.codex','.agents','.auth','docs','AGENTS.md','.env*','node_modules','bin','obj','cookies*','__pycache__']
 def run(cmd,extra=None,timeout=90,cwd=None,stdin=None):
  result=subprocess.run(cmd,cwd=cwd or source,env={**base_env,**(extra or {})},input=stdin,capture_output=True,text=True,timeout=timeout)
  if result.returncode:raise RuntimeError('Owned reader HTTP command failed: '+cmd[0]+' exit='+str(result.returncode))
  return result
 run(['rsync','-a',*[arg for name in excludes for arg in ('--exclude',name)],str(root)+'/',str(source)+'/'])
 for project in ('.','DevCoreBlog.Core','DevCoreBlog.Data','DevCoreBlog.Services'):
  if (root/project/'obj').exists():shutil.copytree(root/project/'obj',source/project/'obj',dirs_exist_ok=True)
 program=source/'Program.cs';text=program.read_text();anchor='var app = builder.Build();'
 if text.count(anchor)!=1:raise RuntimeError('Mailbox registration anchor changed')
 program.write_text(text.replace(anchor,'builder.Services.AddSingleton<DevCoreBlog.Core.Email.IEmailSender, ReaderFixtureMailbox>();\n'+anchor))
 text=program.read_text()
 if text.count('options.UseNpgsql(')!=1:raise RuntimeError('Archive SQL capture anchor changed')
 program.write_text(text.replace('options.UseNpgsql(','options.AddInterceptors(new ArchiveSqlCapture()).UseNpgsql('))
 archive_controller=source/'Controllers/NewsletterArchiveController.cs'
 text=archive_controller.read_text();fault_anchor='var result = await archive.ReadAsync(await SessionAsync(), slug, cancellationToken);'
 if text.count(fault_anchor)!=1:raise RuntimeError('Archive error-path injection anchor changed')
 archive_controller.write_text(text.replace(fault_anchor,'if (slug == "f31-forced-fault") throw new InvalidOperationException("F31_FORCED_EXCEPTION_SECRET");\n        '+fault_anchor))
 (source/'ArchiveSqlCapture.cs').write_text('''using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
internal sealed class ArchiveSqlCapture : DbCommandInterceptor
{
 public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
 {
  if(command.CommandText.Contains("F31 archive"))
   File.AppendAllText(Environment.GetEnvironmentVariable("ARCHIVE_TEST_SQL")??throw new InvalidOperationException(),"--F31--\\n"+command.CommandText+"\\n");
  return ValueTask.FromResult(result);
 }
}
''')
 (source/'ReaderFixtureMailbox.cs').write_text('''using System.Text.Json;
using System.Text.RegularExpressions;
using DevCoreBlog.Core.Email;
internal sealed class ReaderFixtureMailbox : IEmailSender
{
 public async Task<EmailSendResult> SendAsync(EmailSendRequest request,CancellationToken ct=default)
 {
  var path=Environment.GetEnvironmentVariable("READER_TEST_MAILBOX")??throw new InvalidOperationException();
  var token=Regex.Match(request.Text,@"#t=([A-Za-z0-9_-]{43})").Groups[1].Value;
  await File.AppendAllTextAsync(path,JsonSerializer.Serialize(new {recipient=request.Recipient,token})+"\\n",ct);
  return new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200);
 }
}
''')
 run(['dotnet','build','DevCoreBlog.csproj','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false'],timeout=120)
 password=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],cwd=root,stdin='synthetic-reader-password\n').stdout.strip()
 env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':base,
  'SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password,'ADMIN_LOGIN_PATH':'/fixture-admin/login',
  'DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),'SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'true',
  'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'true','RESEND_API_KEY':'re_synthetic','RESEND_FROM_EMAIL':'sender@example.test',
  'RESEND_FROM_NAME':'Synthetic','READER_TEST_MAILBOX':str(mailbox),'ARCHIVE_TEST_SQL':str(fixture/'archive-sql.txt'),'Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
 class NoRedirect(urllib.request.HTTPRedirectHandler):
  def redirect_request(self,*args,**kwargs):return None
 def new_client():
  jar=http.cookiejar.CookieJar();return jar,urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar))
 jar,client=new_client()
 def request(path='/sign-in',data=None,method=None,headers=None,opener=None,raw=None):
  payload=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
  req=urllib.request.Request(base+path,data=payload,method=method,headers=headers or {})
  try:
   with (opener or client).open(req,timeout=20) as response:return response.status,{k.lower():v for k,v in response.headers.items()},response.read().decode()
  except urllib.error.HTTPError as error:return error.code,{k.lower():v for k,v in error.headers.items()},error.read().decode()
 def sql(query):return run(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','reader_http','-At','-v','ON_ERROR_STOP=1','-c',query],cwd=root).stdout.strip()
 def csrf(path='/sign-in',opener=None):
  status,h,body=request(path,opener=opener);match=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body)
  if status!=200 or not match:raise RuntimeError('Reader antiforgery unavailable: '+path+' status='+str(status))
  return match.group(1)
 def seed(active=False):
  run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--reader-active' if active else '--reader-seed'],
   {'DB_CONNECTION_STRING':connection,'READER_TEST_PROOF_FILE':str(proof_file)},cwd=root)
  data=json.loads(proof_file.read_text());proofs.extend([data['token'],data['exitToken']]);addresses.append(data['email']);return data
 def mail(email):
  for _ in range(150):
   rows=[json.loads(x) for x in mailbox.read_text().splitlines()] if mailbox.exists() else []
   found=[x for x in rows if x['recipient']==email]
   if found:proofs.append(found[-1]['token']);return found[-1]['token']
   time.sleep(.1)
  raise RuntimeError('Reader observer mailbox timed out')
 def private(h):return 'no-store' in h.get('cache-control','') and h.get('referrer-policy')=='no-referrer' and 'noindex' in h.get('x-robots-tag','')
 process=None
 def stop():
  nonlocal process
  if process and process.poll() is None:
   process.terminate()
   try:process.wait(timeout=10)
   except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
  process=None
 def start(extra=None,ready_headers=None):
  nonlocal process
  stop();process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--urls',base],cwd=source,env={**env,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
  for _ in range(100):
   if process.poll() is not None:raise RuntimeError('Reader application startup failed')
   try:
    if request(headers=ready_headers)[0]==200:return
   except OSError:time.sleep(.1)
  raise RuntimeError('Reader application readiness timed out')
 def login(token,opener=None,return_url='/abonelik/oturum'):
  anti=csrf('/abonelik/giris',opener)
  return request('/abonelik/giris',{'Token':token,'ReturnUrl':return_url,'__RequestVerificationToken':anti},opener=opener)
 with log_file.open('w') as output:
  try:
   for name,setting in [('disabled',{'READER_LOGIN_ENABLED':'false'}),('worker_off',{'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false'}),('sender_missing',{'RESEND_FROM_EMAIL':''})]:
    start(setting);_,h,body=request();before=sql('SELECT count(*) FROM "TransactionalEmailJobs"')
    anti=csrf('/abonelik/giris');status,h,body=request(data={'Email':'unavailable@example.test','__RequestVerificationToken':anti})
    checks['http_'+name+'_fails_closed']=status==503 and private(h) and sql('SELECT count(*) FROM "TransactionalEmailJobs"')==before
   start();active=seed(True);before=sql('SELECT count(*) FROM "ReaderSessions"')
   status,h,body=request('/sign-in');checks['http_login_form_and_private_headers']=status==200 and private(h) and 'name="Email"' in body and 'şifre' in body.lower()
   checks['http_request_requires_csrf']=request(data={'Email':active['email']})[0]==400
   anti=csrf();status,h,body=request(data={'Email':active['email'],'__RequestVerificationToken':anti})
   token=mail(active['email']);checks['http_request_stages_encrypted_mail_without_session']=status==200 and private(h) and sql('SELECT count(*) FROM "ReaderSessions"')==before
   generic=body
   status,_,body=request(data={'Email':'unknown@example.test','__RequestVerificationToken':anti})
   # Antiforgery strings can rotate; compare the visible generic response rather than framework-generated hidden tokens.
   visible=lambda b:re.sub(r'<[^>]+>',' ',b).split()
   checks['http_unknown_and_active_share_generic_response']=status==200 and visible(body)==visible(generic)
   checks['http_invalid_address_is_400']=request(data={'Email':'invalid','__RequestVerificationToken':anti})[0]==400
   status,h,body=request('/abonelik/giris#t='+token)
   checks['http_open_link_and_head_never_sign_in']=status==200 and request('/abonelik/giris',method='HEAD')[0]==200 and sql('SELECT count(*) FROM "ReaderSessions"')==before
   checks['http_proof_page_is_private_no_proof_echo']=private(h) and token not in body and active['email'] not in body and 'value=""' in body
   checks['http_proof_requires_csrf']=request('/abonelik/giris',{'Token':token})[0]==400
   checks['http_external_return_rejected_before_consumption']=login(token,return_url='https://evil.example/') [0]==400 and sql('SELECT count(*) FROM "ReaderSessions"')==before
   checks['http_protocol_relative_and_encoded_return_rejected']=all(request('/abonelik/giris?returnUrl='+urllib.parse.quote(value,safe=''))[0]==400 for value in ['//evil.example/','/bulten/%2f%2fevil','/bulten/\\evil','/bulten?x=1'])
   status,h,body=login(token);checks['http_confirm_issues_reader_cookie_and_local_redirect']=status==302 and h.get('location')=='/abonelik/oturum' and any(c.name=='AetherNotes.Reader' for c in jar)
   reader_cookie=next(c for c in jar if c.name=='AetherNotes.Reader');saved=reader_cookie.value
   checks['http_reader_cookie_httponly_lax_absolute_expiry']=reader_cookie.expires is not None and any(k.lower()=='httponly' for k in reader_cookie._rest) and next((str(v).lower() for k,v in reader_cookie._rest.items() if k.lower()=='samesite'),'')=='lax'
   checks['http_protected_session_and_fresh_csrf']=request('/abonelik/oturum')[0]==200 and bool(csrf('/abonelik/oturum'))
   checks['http_reader_does_not_authorize_admin']=request('/AdminPost')[0]==404
   start();checks['http_actual_process_restart_preserves_reader_session']=request('/abonelik/oturum')[0]==200
   checks['http_used_proof_is_400']=login(token)[0]==400
   # Admin-only cookie cannot satisfy reader policy; both cookies may then coexist independently.
   admin_jar,admin=new_client();anti=csrf('/fixture-admin/login',admin)
   status,_,_=request('/fixture-admin/login',{'username':'fixture-admin','password':'synthetic-reader-password','__RequestVerificationToken':anti},opener=admin)
   checks['http_admin_only_cannot_authorize_reader']=status==302 and request('/abonelik/oturum',opener=admin)[0]==401 and request('/AdminPost',opener=admin)[0]==200
   for cookie in admin_jar:jar.set_cookie(cookie)
   checks['http_both_cookies_authorize_only_their_schemes']=request('/AdminPost')[0]==200 and request('/abonelik/oturum')[0]==200
   checks['http_logout_get_and_missing_csrf_cannot_revoke']=request('/abonelik/oturumu-kapat')[0] in (404,405) and request('/abonelik/oturumu-kapat',{})[0]==400 and request('/abonelik/oturum')[0]==200
   status,_,_=request('/abonelik/oturumu-kapat',{'__RequestVerificationToken':csrf('/abonelik/oturum')})
   _,replay=new_client()
   checks['http_logout_revokes_copied_cookie_server_side']=status==302 and request('/abonelik/oturum',headers={'Cookie':'AetherNotes.Reader='+saved},opener=replay)[0]==401
   checks['http_reader_logout_preserves_admin_cookie']=request('/AdminPost')[0]==200
   # Expiry and unsubscribe are checked against current DB facts, never the original cookie grant alone.
   fresh=seed();status,_,_=login(fresh['token']);checks['http_new_reader_login']=status==302 and request('/abonelik/oturum')[0]==200
   session_cookie=next(c.value for c in jar if c.name=='AetherNotes.Reader')
   sql('UPDATE "ReaderSessions" SET "CreatedUtc"="CreatedUtc"-interval \'8 days\',"ExpiresUtc"="ExpiresUtc"-interval \'8 days\' WHERE "SubscriberId"=\''+fresh['subscriberId']+'\'')
   checks['http_server_expiry_rejects_unexpired_browser_cookie']=request('/abonelik/oturum')[0]==401
   exit_seed=seed();login(exit_seed['token']);anti=csrf('/abonelik/cik')
   status,_,_=request('/abonelik/cik',{'Token':exit_seed['exitToken'],'__RequestVerificationToken':anti})
   checks['http_real_unsubscribe_closes_current_session']=status==200 and request('/abonelik/oturum')[0]==401
   # Deliberate database outage cannot turn a valid encrypted ticket into an authorized reader.
   outage=seed();login(outage['token']);checks['http_pre_outage_session_valid']=request('/abonelik/oturum')[0]==200
   stop();start({'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','READER_LOGIN_ENABLED':'false'})
   checks['http_existing_session_independent_of_ingress_and_worker']=request('/abonelik/oturum')[0]==200
   run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],cwd=root)
   try:
    status,h,body=request('/abonelik/oturum');checks['http_storage_outage_is_private_503']=status==503 and private(h) and outage['token'] not in body and 'SELECT' not in body
    for path in ('/bultenler','/bulten/unavailable'):
     status,h,body=request(path);checks['archive_identity_outage_private_503_'+path]=status==503 and private(h) and outage['token'] not in body
   finally:run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'],cwd=root)
   checks['http_storage_recovery_keeps_session_retryable']=request('/abonelik/oturum')[0]==200
   jar,client=new_client();start();anti=csrf();statuses=[request(data={'Email':'unknown@example.test','__RequestVerificationToken':anti})[0] for _ in range(6)]
   checks['http_request_rate_limit_real_429']=statuses[:5]==[200]*5 and statuses[5]==429
   checks['http_rate_limit_keeps_private_headers']=private(request(data={'Email':'unknown@example.test','__RequestVerificationToken':anti})[1])
   start()
   secure_seed=seed()
   jar,client=new_client()
   proxy_headers={'Host':'aether.example.test','X-Forwarded-For':'203.0.113.9','X-Forwarded-Proto':'https'}
   start({'ASPNETCORE_ENVIRONMENT':'Staging','DEPLOYMENT_PROFILE':'nginx-loopback','SITE_URL':'https://aether.example.test'},proxy_headers)
   status,h,body=request('/abonelik/giris',headers=proxy_headers)
   anti=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
   anti_cookie=h['set-cookie'].split(';',1)[0]
   status,h,body=request('/abonelik/giris',{'Token':secure_seed['token'],'__RequestVerificationToken':anti,'ReturnUrl':'/abonelik/oturum'},headers={**proxy_headers,'Cookie':anti_cookie})
   set_cookie=h.get('set-cookie','').lower()
   checks['http_staging_forwarded_https_reader_cookie_secure']=status==302 and 'secure' in set_cookie.split('; ') and 'httponly' in set_cookie.split('; ') and 'samesite=lax' in set_cookie
   checks['http_staging_cookie_authorizes_current_reader']=request('/abonelik/oturum',headers={**proxy_headers,'Cookie':h.get('set-cookie','').split(';',1)[0]})[0]==200
   jar,client=new_client();start()
   anti=csrf('/abonelik/giris')
   status,_,body=request('/abonelik/giris',{'Token':'x'*44,'__RequestVerificationToken':anti})
   checks['http_oversized_proof_rejected_without_echo']=status==400 and 'x'*44 not in body
   checks['http_body_and_media_limits_preserved']=request('/abonelik/giris',raw=b'Token='+b'x'*5000,headers={'Content-Type':'application/x-www-form-urlencoded'})[0] in (400,413) and request('/abonelik/giris',raw=b'{}',headers={'Content-Type':'application/json'})[0] in (400,415)
   start()
   run_archive_checks(root,fixture,report_dir,request,sql,seed,login,csrf,new_client,start,private,base_env,browser_module,browser_executable)
   checks['archive_real_HTTP_and_SQL_acceptance']=json.loads((report_dir/'archive-http.json').read_text())['passed']
   if browser_module:
    # Independent browser stories must not share the previous story's proof rate-limit window.
    start()
    seed(True)
    browser_env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable) if browser_executable else '',
     'READER_TEST_BASE':base,'READER_TEST_PROOF_FILE':str(proof_file),'READER_TEST_MAILBOX':str(mailbox),'READER_TEST_REPORT':str(report_dir/'browser.json'),
     'READER_TEST_SCREENSHOT':str(report_dir/'login-desktop.png'),'READER_TEST_MOBILE_SCREENSHOT':str(report_dir/'login-mobile.png')}
    proc=subprocess.Popen(['node',str(root/'scripts/verification/reader_identity_browser_probe.cjs')],cwd=root,env=browser_env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True)
    try:
     proc.communicate(timeout=90);checks['real_chrome_login_acceptance']=proc.returncode==0 and json.loads((report_dir/'browser.json').read_text())['passed']
    finally:
     try:os.killpg(proc.pid,signal.SIGTERM)
     except ProcessLookupError:pass
     proc.wait(timeout=10)
  finally:stop()
 log=log_file.read_text();checks['http_no_raw_proofs_or_addresses_in_logs']=all(p not in log for p in proofs if p) and all(a not in log for a in addresses)
 checks['http_modelbinding_trace_forced_off']='Attempting to bind' not in log
