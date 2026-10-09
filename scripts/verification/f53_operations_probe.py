#!/usr/bin/env python3
"""Exercise real MVC diagnostics only on an owned disposable source/database; never store secrets or cookies."""
import argparse,base64,getpass,http.client,json,os,socketserver,subprocess,threading,time
from pathlib import Path
from http_probe_support import admin_login_url, admin_login_path, cookie_opener,request_with_headers,submit_login,extract_antiforgery_token,multipart_payload
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--database-port',type=int,required=True);p.add_argument('--port',type=int,default=15195);p.add_argument('--report',type=Path,required=True);p.add_argument('--hold-for-browser',action='store_true');a=p.parse_args()
source=a.source.resolve();assert source.name=='source' and (str(source).startswith('/private/tmp/devcoreblog-f17.') or str(source).startswith('/tmp/devcoreblog-f17.')) and not (source/'.git').exists()
assert a.port!=a.database_port and 1024<a.port<65535
# Inject faults only into the owned test copy; no production route is added.
(source/'F53FaultController.cs').write_text('''using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
[Authorize]
[Route("f53-fixture")]
public sealed class F53FaultController : Controller
{
    [HttpGet("before")]
    public IActionResult Before() => throw new InvalidOperationException("F53_PRIVATE_CANARY_DO_NOT_LOG exception/SQL");
    [HttpGet("started")]
    public async Task Started()
    {
        Response.ContentType = "text/html";
        await Response.WriteAsync("<html><body>Owned partial response");
        await Response.Body.FlushAsync();
        throw new InvalidOperationException("F53_PRIVATE_CANARY_DO_NOT_LOG exception/SQL");
    }
}
''')
subprocess.run(['dotnet','build',str(source/'DevCoreBlog.csproj'),'--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
tmp=source.parent;keys=tmp/'f53-keys';keys.mkdir(exist_ok=True);keys.chmod(0o700)
password='f53-isolated-password';canary='F53_PRIVATE_CANARY_DO_NOT_LOG'
hashed=subprocess.run(['dotnet','run','--no-build','--project',str(source/'tools/DevCoreBlog.PasswordHashTool'),'--','--stdin'],input=password+'\n',capture_output=True,text=True,check=True).stdout.strip()
base=f'http://127.0.0.1:{a.port}';connection=f'Host=127.0.0.1;Port={a.database_port};Database=devcoreblog_f01_test;Username={getpass.getuser()}'
env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://blog.example.test','DATA_PROTECTION_KEYS_PATH':str(keys),'DB_CONNECTION_STRING':connection,'ADMIN_LOGIN_PATH':admin_login_path(),'ADMIN_USERNAME':'f53-admin','ADMIN_PASSWORD_HASH':hashed,'ADMIN_SESSION_VERSION':'f53-stable','ADMIN_SESSION_LIFETIME_SECONDS':'1800','CLOUDINARY_CLOUD_NAME':'f53-cloud','CLOUDINARY_API_KEY':canary+'-key','CLOUDINARY_API_SECRET':canary+'-media','WEBHOOK_API_SECRET':canary+'-webhook','Security__LoginRateLimit__PermitLimit':'20','Security__WebhookRateLimit__PermitLimit':'20','Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command':'Information','Logging__LogLevel__Npgsql.Command':'Trace'}
proc=None;lock_proc=None;streams=[];logs=[];checks={};latencies={};failure_ids={};fault_ids=set()
def stop():
 global proc
 if proc:
  proc.terminate()
  try:proc.wait(timeout=10)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
  proc=None
 for stream in streams:stream.flush()
def start(name,overrides=None):
 global proc
 file=tmp/('f53-'+name+'.log');logs.append(file);stream=file.open('w');streams.append(stream)
 proc=subprocess.Popen([str(source/'bin/Debug/net10.0/DevCoreBlog')],cwd=source,env={**env,**(overrides or {})},stdout=stream,stderr=subprocess.STDOUT)
 for _ in range(150):
  assert proc.poll() is None,'Owned application exited before ready'
  try:
   c=http.client.HTTPConnection('127.0.0.1',a.port,timeout=1);c.request('GET','/css/error-page.css');r=c.getresponse();r.read();c.close()
   if r.status==200:return
  except OSError:pass
  time.sleep(.1)
 raise AssertionError('Owned application did not start')
def anonymous_has_no_diagnostics():
 c=http.client.HTTPConnection('127.0.0.1',a.port,timeout=5);c.request('GET','/Admin/Dashboard');r=c.getresponse();body=r.read().decode();c.close()
 return r.status==404 and 'no-store' in r.getheader('Cache-Control','') and not r.getheader('Location') and 'database-status' not in body and 'media-status' not in body
def get_dashboard(admin):
 started=time.monotonic();s,_,b,h=request_with_headers(admin,base+'/Admin/Dashboard',headers={'X-Request-ID':canary})
 return s,b,h,time.monotonic()-started
class Stall(socketserver.BaseRequestHandler):
 def handle(self):release.wait(8)
release=threading.Event();server=None
try:
 start('configured');admin,_=cookie_opener();submit_login(admin,base,'f53-admin',password)
 s,b,h,d=get_dashboard(admin)
 checks['actual_postgresql_read_is_verified']=s==200 and 'Verified (PostgreSQL)' in b
 checks['credentials_do_not_claim_media_access']='Configured' in b and 'access not checked' in b and 'Cloudinary (Active)' not in b and 'Last successful upload:' not in b
 checks['anonymous_cannot_read_diagnostics']=anonymous_has_no_diagnostics()
 checks['server_request_id_ignores_client_value']=bool(h.get('X-Request-ID')) and canary not in h.get('X-Request-ID','')
 checks['diagnostics_are_private_no_store']='no-store' in h.get('Cache-Control','')
 _,_,form,_=request_with_headers(admin,base+'/AdminPost/Create');token=extract_antiforgery_token(form);assert token
 def upload(kind):
  content=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' if kind=='png' else 'R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==')
  data,mime=multipart_payload({'__RequestVerificationToken':token},file_field='file',file_name=canary+'.'+kind,content_type='image/'+kind,content=content)
  result=request_with_headers(admin,base+'/AdminPost/UploadEditorImage?probe='+canary,raw_data=data,headers={'Content-Type':mime})
  if result[0]>=400:failure_ids['Upload']=result[3].get('X-Request-ID')
  return result[0]
 checks['real_upload_fixture_success_is_observed']=upload('png')==200 and 'Last successful upload:' in get_dashboard(admin)[1]
 checks['provider_failure_is_observed_without_erasing_prior_success']=upload('gif')==503 and 'Last upload: Failed' in get_dashboard(admin)[1] and 'Last successful upload:' in get_dashboard(admin)[1]
 anon,_=cookie_opener();s,_,_,h=request_with_headers(anon,base+'/api/webhooks/posts',raw_data=canary.encode(),headers={'Content-Type':'application/json','X-DevCore-Secret':canary+'-wrong'})
 checks['webhook_rejection_preserves_401']=s==401
 failure_ids['Webhook']=h.get('X-Request-ID')
 s,_,_,h=request_with_headers(anon,base+'/api/webhooks/posts',raw_data=canary.encode(),headers={'Content-Type':'application/json','X-DevCore-Secret':canary+'-webhook'})
 checks['webhook_invalid_body_preserves_400']=s==400
 _,_,login,_=request_with_headers(anon,admin_login_url(base));login_token=extract_antiforgery_token(login)
 failed_login=request_with_headers(anon,admin_login_url(base),data={'username':canary+'-username','password':canary+'-password','__RequestVerificationToken':login_token})
 failure_ids['Authentication']=failed_login[3].get('X-Request-ID')
 if a.hold_for_browser:
  print('F53_BROWSER_READY '+base,flush=True)
  while not (tmp/'f53-continue').exists():
   assert proc.poll() is None,'Owned application stopped during browser hold';time.sleep(.2)
 # SELECT 1 succeeding must not allow later dashboard reads to hang on a real table lock.
 pg=['psql','-X','-h','127.0.0.1','-p',str(a.database_port),'-U',getpass.getuser(),'-d','devcoreblog_f01_test']
 lock_proc=subprocess.Popen(pg+['-c','BEGIN; LOCK TABLE "Posts" IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(15); ROLLBACK;'],env={**os.environ,'PGAPPNAME':'devcoreblog-f53-lock'},stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 for _ in range(40):
  locked=subprocess.run(pg+['-At','-c',"SELECT count(*) FROM pg_locks WHERE relation='\"Posts\"'::regclass AND mode='AccessExclusiveLock' AND granted"],capture_output=True,text=True,check=True).stdout.strip()
  if locked=='1':break
  time.sleep(.05)
 assert locked=='1','Owned test table lock was not acquired'
 s,b,h,d=get_dashboard(admin);latencies['locked_metric_query_seconds']=round(d,3)
 checks['real_locked_metric_query_is_bounded_and_not_fabricated']=s==503 and 'Statistics unavailable' in b and d<5
 subprocess.run(pg+['-At','-c',"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='devcoreblog-f53-lock' AND datname=current_database()"],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 lock_proc.wait(timeout=5);lock_proc=None
 stop();start('unconfigured',{'CLOUDINARY_API_SECRET':'','WEBHOOK_API_SECRET':''})
 s,b,h,d=get_dashboard(admin)
 checks['missing_media_config_keeps_admin_available_and_does_not_claim_access']=s==200 and 'Not configured' in b and 'uploads unavailable' in b
 checks['observations_reset_after_restart']='Last successful upload:' not in b
 _,_,form,_=request_with_headers(admin,base+'/AdminPost/Create');token=extract_antiforgery_token(form)
 checks['missing_media_config_fails_upload_closed']=upload('png')==503
 checks['missing_webhook_config_fails_closed']=request_with_headers(anon,base+'/api/webhooks/posts',raw_data=b'{}',headers={'Content-Type':'application/json','X-DevCore-Secret':canary+'-webhook'})[0]==401
 # Reuse only synthetic session with persistent owned keys to diagnose a stopped test database.
 subprocess.run(['pg_ctl','-D',str(tmp/'postgres'),'-m','fast','-w','stop'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 s,b,h,d=get_dashboard(admin);latencies['stopped_database_seconds']=round(d,3)
 checks['stopped_database_returns_real_503_with_diagnostics']=s==503 and 'Unavailable (PostgreSQL)' in b and 'Statistics unavailable' in b and 'Total Posts' not in b
 checks['outage_keeps_csp_request_id_and_no_store']=bool(h.get('Content-Security-Policy')) and bool(h.get('X-Request-ID')) and 'no-store' in h.get('Cache-Control','')
 checks['outage_does_not_expose_connection_or_exception']='Host=' not in b and 'Npgsql' not in b and canary not in b
 stop()
 server=socketserver.ThreadingTCPServer(('127.0.0.1',0),Stall);server.daemon_threads=True;threading.Thread(target=server.serve_forever,daemon=True).start()
 stalled=f'Host=127.0.0.1;Port={server.server_address[1]};Database=devcoreblog_f01_test;Username={getpass.getuser()};Timeout=30'
 start('timeout',{'DB_CONNECTION_STRING':stalled})
 s,b,h,d=get_dashboard(admin);latencies['stalled_handshake_seconds']=round(d,3)
 checks['real_driver_deadline_is_bounded']=s==503 and 'Timed out (PostgreSQL)' in b and d<5
 checks['static_asset_remains_available_during_outage']=request_with_headers(anon,base+'/css/error-page.css')[0]==200
 c=http.client.HTTPConnection('127.0.0.1',a.port,timeout=1)
 # Extract synthetic cookies only in memory from the opener's cookie processor.
 jar=next(handler.cookiejar for handler in admin.handlers if hasattr(handler,'cookiejar'))
 c.request('GET','/Admin/Dashboard',headers={'Cookie':'; '.join(cookie.name+'='+cookie.value for cookie in jar)});time.sleep(.1);c.close();time.sleep(.4)
 # A partial response must be aborted, and even that exception must stay out of host logs.
 s,_,b,h=request_with_headers(admin,base+'/f53-fixture/before');fault_ids.add(h.get('X-Request-ID'))
 checks['exception_before_headers_keeps_real_500_and_safe_body']=s==500 and canary not in b and bool(h.get('Content-Security-Policy'))
 c=http.client.HTTPConnection('127.0.0.1',a.port,timeout=5)
 c.request('GET','/f53-fixture/started',headers={'Cookie':'; '.join(cookie.name+'='+cookie.value for cookie in jar)})
 r=c.getresponse();fault_ids.add(r.getheader('X-Request-ID'));aborted=False
 try:r.read()
 except (http.client.IncompleteRead,ConnectionResetError):aborted=True
 c.close();checks['started_fault_aborts_incomplete_response']=aborted
 stop()
 raw='\n'.join(file.read_text() for file in logs)
 checks['private_values_are_absent_from_real_logs']=all(value not in raw for value in [canary,password,hashed])
 events=[]
 for line in raw.splitlines():
  try:events.append(json.loads(line))
  except json.JSONDecodeError:pass
 app=[event for event in events if event.get('Category','').startswith('DevCoreBlog')]
 def operation_logged(word):return any(word in event.get('Message','') and event.get('LogLevel')=='Warning' and any('TraceId' in scope for scope in event.get('Scopes',[]) if isinstance(scope,dict)) for event in app)
 checks['auth_failure_is_correlated']=operation_logged('sign-in attempt failed')
 checks['upload_failure_is_correlated']=operation_logged('Image storage')
 checks['webhook_failure_is_correlated']=operation_logged('Webhook')
 checks['error_response_ids_match_log_scopes']=all(identifier and any(any(scope.get('TraceId')==identifier for scope in event.get('Scopes',[]) if isinstance(scope,dict)) for event in app) for identifier in failure_ids.values()) and len(failure_ids)==3
 checks['provider_logs_cannot_be_enabled_by_specific_override']=not any(event.get('Category','').startswith(('Microsoft.EntityFrameworkCore','Npgsql')) for event in events)
 checks['client_abort_is_not_server_crash']=all(event.get('LogLevel')!='Error' or any(scope.get('TraceId') in fault_ids for scope in event.get('Scopes',[]) if isinstance(scope,dict)) for event in app)
 checks['deliberate_faults_have_safe_correlated_error_events']=all(identifier and any(event.get('LogLevel')=='Error' and any(scope.get('TraceId')==identifier for scope in event.get('Scopes',[]) if isinstance(scope,dict)) for event in app) for identifier in fault_ids) and len(fault_ids)==2
 a.report.write_text(json.dumps({'checks':checks,'count':len(checks),'latencies':latencies,'scope':'Disposable PostgreSQL + actual Npgsql stalled handshake + MVC, synthetic storage. No real Cloudinary access; logs scanned privately and not exported.'},indent=2)+'\n')
 print(json.dumps({'checks':checks,'count':len(checks),'latencies':latencies},indent=2));assert all(checks.values()),[key for key,value in checks.items() if not value]
finally:
 stop();release.set()
 if lock_proc is not None and lock_proc.poll() is None:lock_proc.terminate();lock_proc.wait(timeout=5)
 if server:server.shutdown();server.server_close()
 for stream in streams:stream.close()
