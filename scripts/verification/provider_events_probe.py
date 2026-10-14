#!/usr/bin/env python3
"""Owned real PostgreSQL and MVC provider ingress acceptance. Never loads real dotenv or makes email calls."""
import argparse,base64,getpass,hashlib,hmac,http.client,json,os,shutil,socket,subprocess,tempfile,time,urllib.error,urllib.request,uuid
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55528');a=p.parse_args()
root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};version='unverified'
previous='20261014171616_CampaignRecipientSelection';current='20261014175940_ProviderEmailEvents'
key=bytes(range(1,33));secret='whsec_'+base64.b64encode(key).decode()
def run(cmd,extra=None,required=True,timeout=150):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Provider fixture command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(name,ok):checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
def safe_report(r,name):
 try:
  data=json.loads(r.stdout);valid=r.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
 except (ValueError,KeyError,TypeError):
  try:diagnostic={k:v for k,v in json.loads(r.stdout).items() if k in ('exceptionType','sqlState','lastCheck')}
  except (ValueError,TypeError,AttributeError):diagnostic={}
  data={'passed':False,'exit_code':r.returncode,**diagnostic};valid=False
 a.report.parent.mkdir(parents=True,exist_ok=True);(a.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
def http_fixture(f,connection,sql):
 base='http://127.0.0.1:15228';web=f/'web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot');(f/'keys').mkdir(mode=0o700)
 # A valid generated hash is required; stdin is handled explicitly and never a real credential.
 hashed=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='synthetic-provider-password\n',env=env,cwd=root,capture_output=True,text=True,timeout=20)
 if hashed.returncode:raise RuntimeError('Synthetic provider hash failed')
 app_env={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':base,'SITE_TIME_ZONE':'Europe/Istanbul',
  'ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hashed.stdout.strip(),'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),
  'SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false',
  'RESEND_WEBHOOK_SECRET':secret,'RESEND_WEBHOOK_ALLOWED_SENDERS':'onboarding@resend.dev',
  'Logging__LogLevel__Microsoft.EntityFrameworkCore':'Trace','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
 log_file=f/'http.log';output=log_file.open('w');process=None
 def request(body=b'',identity=None,stamp=None,signature=None,method='POST',content_type='application/json',path='/api/webhooks/resend'):
  identity=identity or 'msg_'+uuid.uuid4().hex
  stamp=str(int(time.time()) if stamp is None else stamp)
  sign=signature if signature is not None else 'v1,'+base64.b64encode(hmac.new(key,(identity+'.'+stamp+'.').encode()+body,hashlib.sha256).digest()).decode()
  req=urllib.request.Request(base+path,data=body if method=='POST' else None,method=method,headers={'Content-Type':content_type,'svix-id':identity,'svix-timestamp':stamp,'svix-signature':sign})
  try:
   with urllib.request.urlopen(req,timeout=15) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read()
  except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read()
 def start(enabled):
  nonlocal process
  process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env={**app_env,'RESEND_WEBHOOK_ENABLED':str(enabled).lower()},stdout=output,stderr=subprocess.STDOUT)
  for _ in range(150):
   if process.poll() is not None:raise RuntimeError('Provider HTTP application failed to start')
   try:
    if request(method='GET',path='/fixture-admin/login')[0]==200:return
   except OSError:time.sleep(.1)
  raise RuntimeError('Provider HTTP readiness timed out')
 def stop():
  if process is not None and process.poll() is None:
   process.terminate()
   try:process.wait(timeout=10)
   except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
 def body(kind='email.delivered',to='http-private@example.test',message=None):
  return json.dumps({'type':kind,'created_at':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'data':{'email_id':message or str(uuid.uuid4()),'from':'Fixture <onboarding@resend.dev>','to':[to],'bounce':{'type':'Permanent'},'subject':'PRIVATE_PROVIDER_SUBJECT'}},ensure_ascii=False,separators=(',',':')).encode()
 try:
  start(False);status,headers,_=request(body())
  check('http_disabled_callback503_no_store',status==503 and 'no-store' in headers.get('cache-control',''));stop();start(True)
  sql('provider_http',"INSERT INTO \"Subscribers\" (\"Id\",\"EmailAddress\",\"AddressKey\",\"Status\",\"Suppression\",\"CreatedUtc\",\"UpdatedUtc\",\"Version\") VALUES (gen_random_uuid(),'http-private@example.test','http-private@example.test',0,0,now(),now(),1)")
  get_status,get_headers,_=request(method='GET');head_status,head_headers,_=request(method='HEAD')
  # The existing MVC fallback hides unmatched methods as404; neither method has a callback action.
  check('http_get_head_never_mutate',get_status in (404,405) and head_status in (404,405)
   and 'no-store' in get_headers.get('cache-control','') and 'no-store' in head_headers.get('cache-control','')
   and sql('provider_http','SELECT count(*) FROM "ProviderEmailEvents"')=='0')
  raw=body();identity='msg_http_delivery'
  check('http_missing_wrong_and_old_signature401',request(raw,signature='v1,wrong')[0]==401 and request(raw,stamp=int(time.time())-360)[0]==401)
  status,headers,response=request(raw,identity=identity)
  check('http_independent_python_signature204_without_cookie_or_csrf',status==204 and response==b'' and sql('provider_http','SELECT count(*) FROM "ProviderEmailEvents"')=='1')
  check('http_success_private_headers','no-store' in headers.get('cache-control','') and headers.get('referrer-policy')=='no-referrer' and 'noindex' in headers.get('x-robots-tag','') and headers.get('x-content-type-options')=='nosniff')
  check('http_exact_replay_no_extra_event',request(raw,identity=identity)[0]==204 and sql('provider_http','SELECT count(*) FROM "ProviderEmailEvents"')=='1')
  check('http_same_id_changed_body409',request(raw+b' ',identity=identity)[0]==409)
  check('http_malformed_json400',request(b'{broken')[0]==400)
  check('http_invalid_utf8_rejected400',request(b'\xff')[0]==400)
  check('http_wrong_content_type415',request(raw,content_type='text/plain')[0]==415)
  oversized=b' '*65537;status,headers,_=request(oversized)
  check('http_body_limit413_private_headers',status==413 and 'no-store' in headers.get('cache-control',''))
  connection_http=http.client.HTTPConnection('127.0.0.1',15228,timeout=15)
  connection_http.request('POST','/api/webhooks/resend',body=[b' '*4096 for _ in range(17)],headers={'Content-Type':'application/json','svix-id':'msg_chunked','svix-timestamp':str(int(time.time())),'svix-signature':'v1,bad'},encode_chunked=True)
  response_http=connection_http.getresponse();check('http_chunked_body_is_bounded413',response_http.status==413);response_http.read();connection_http.close()
  # Replay survives an actual process replacement; event DB and durable key ring stay intact.
  stop();start(True)
  check('http_real_restart_preserves_replay',request(raw,identity=identity)[0]==204 and sql('provider_http','SELECT count(*) FROM "ProviderEmailEvents"')=='1')
  bounced=body('email.bounced');check('http_hard_bounce_sets_source_and_ledger',request(bounced)[0]==204 and sql('provider_http',"SELECT \"Suppression\" FROM \"Subscribers\" WHERE \"AddressKey\"='http-private@example.test'")=='1' and sql('provider_http','SELECT count(*) FROM "ProviderAddressSuppressions"')=='1')
  run(['pg_ctl','-D',str(f/'pg'),'-m','fast','-w','stop'])
  failed=body('email.complained');status,headers,_=request(failed,identity='msg_outage')
  check('http_storage_outage_honest503_private_headers',status==503 and 'no-store' in headers.get('cache-control',''))
  run(['pg_ctl','-D',str(f/'pg'),'-l',str(f/'pg.log'),'-o',f'-h 127.0.0.1 -p {a.port} -k {f}/socket','-w','start'])
  check('http_same_event_retries_after_storage_recovery',request(failed,identity='msg_outage')[0]==204 and sql('provider_http',"SELECT \"Suppression\" FROM \"Subscribers\" WHERE \"AddressKey\"='http-private@example.test'")=='3')
  # Four partial bodies occupy the global concurrency budget before any payload can be processed.
  slow=[]
  try:
   for i in range(4):
    s=socket.create_connection(('127.0.0.1',15228),timeout=5);slow.append(s)
    s.sendall(('POST /api/webhooks/resend HTTP/1.1\r\nHost: 127.0.0.1:15228\r\nContent-Type: application/json\r\nContent-Length: 200\r\nsvix-id: msg_slow'+str(i)+'\r\nsvix-timestamp: '+str(int(time.time()))+'\r\nsvix-signature: v1,bad\r\n\r\n{').encode())
   time.sleep(.3);status,headers,_=request(raw)
   check('http_global_four_concurrent_bodies_limit429',status==429 and 'no-store' in headers.get('cache-control',''))
  finally:
   for s in slow:s.close()
  stop();start(True)
  ignored=body('email.opened');statuses=[request(ignored,identity='msg_rate_'+str(i))[0] for i in range(61)]
  check('http_global_sixty_per_minute_limit',statuses[:60]==[204]*60 and statuses[60]==429)
  check('http_tracking_produces_no_new_receipts',sql('provider_http','SELECT count(*) FROM "ProviderEmailEvents"')=='3')
  check('http_never_creates_mail_jobs',sql('provider_http','SELECT count(*) FROM "TransactionalEmailJobs"')=='0')
 finally:stop();output.close()
 log=log_file.read_text()
 check('http_private_payload_addresses_and_secret_absent_from_logs',all(value not in log for value in [secret,'PRIVATE_PROVIDER_SUBJECT','http-private@example.test','msg_http_delivery']))

if run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode==0:raise SystemExit('Owned provider port occupied.')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-provider-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();started=False;env['PROVIDER_TEST_ROOT']=owned
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  tool=['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll')]
  def snapshot(db,tables):return '\n'.join(sql(db,'SELECT to_jsonb(t)::text FROM "'+t+'" t ORDER BY 1') for t in tables)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);started=True
   version=sql('postgres','SHOW server_version')
   for db in ('provider_empty','provider_prior','provider_http'):run(['createdb','-h','127.0.0.1','-p',a.port,db])
   check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
   migrate('provider_prior',previous);safe_report(run(tool+['--provider-events-seed'],{'DB_CONNECTION_STRING':connection('provider_prior')},False),'provider_previous_seed')
   prior_tables=('Subscribers','NewsletterCampaigns','NewsletterRecipients','ReaderSessions','TransactionalEmailJobs')
   before=snapshot('provider_prior',prior_tables);history=sql('provider_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
   migrate('provider_empty',current);migrate('provider_empty',previous)
   check('empty_down_safe',sql('provider_empty',"SELECT to_regclass('\"ProviderEmailEvents\"') IS NULL")=='t')
   for db in ('provider_empty','provider_prior','provider_http'):migrate(db,current)
   check('previous_rows_preserved',snapshot('provider_prior',prior_tables)==before)
   check('existing_suppression_backfilled',sql('provider_prior',"SELECT \"Flags\" FROM \"ProviderAddressSuppressions\" WHERE \"AddressKey\"='preserved@example.test'")=='1')
   check('migration_history_additive',sql('provider_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
   for db in ('provider_empty','provider_prior'):safe_report(run(tool+['--provider-events'],{'DB_CONNECTION_STRING':connection(db)},False),db)
   preserved=snapshot('provider_prior',('ProviderEmailEvents','ProviderEmailObservations','ProviderAddressSuppressions','Subscribers'))
   check('nonempty_down_preserves_all_facts_and_suppression',migrate('provider_prior',previous,False).returncode!=0 and snapshot('provider_prior',('ProviderEmailEvents','ProviderEmailObservations','ProviderAddressSuppressions','Subscribers'))==preserved)
   http_fixture(f,connection('provider_http'),sql)
   check('fixture_uses_actual_postgresql',version.startswith('16.'))
  finally:
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode!=0)
 check('owned_data_removed',not f.exists())
except Exception as error:checks['fixture_completed']=False;print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
 a.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned real PostgreSQL + MVC; independent Python HMAC signature, replay, atomic suppression, real restart/outage/bounded body/concurrency. No external provider calls or real credentials.'};a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
