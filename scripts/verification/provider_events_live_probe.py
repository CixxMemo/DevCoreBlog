#!/usr/bin/env python3
"""Explicit Resend simulator acceptance through an ephemeral, callback-only relay. Never uses real recipient addresses."""
import argparse,getpass,http.server,json,os,re,shutil,stat,subprocess,tempfile,threading,time,urllib.error,urllib.request,uuid
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--env-file',type=Path,required=True);p.add_argument('--cloudflared',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[2];checks={};created_id=None;endpoint=None;api_key=None;callbacks=[];lock=threading.Lock();signing_secret=None
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
def check(name,ok):checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
def run(cmd,extra=None,required=True,timeout=120):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Live fixture command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def api(method,path,data=None):
 req=urllib.request.Request('https://api.resend.com'+path,data=json.dumps(data).encode() if data is not None else None,method=method,
  headers={'Authorization':'Bearer '+api_key,'Content-Type':'application/json','User-Agent':'AetherNotes-F36-simulator'})
 with urllib.request.urlopen(req,timeout=20) as r:
  raw=r.read(65537)
  if len(raw)>65536:raise RuntimeError('Provider response exceeded bound')
  return r.status,json.loads(raw) if raw else {}
def delete_owned_endpoint():
 global created_id
 if created_id is None:return endpoint is None
 for _ in range(3):
  try:
   api('DELETE','/webhooks/'+str(uuid.UUID(created_id)));created_id=None;return True
  except urllib.error.HTTPError as e:
   if e.code==404:created_id=None;return True
  except Exception:pass
  time.sleep(1)
 return False
def terminate(process):
 if process is not None and process.poll() is None:
  process.terminate()
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
try:
 if stat.S_IMODE(a.env_file.stat().st_mode)!=0o600:raise RuntimeError('Owner-only dotenv required')
 if not a.cloudflared.resolve().parent.name.startswith('devcoreblog-f36-cloudflared-') or a.cloudflared.resolve().parent.parent!=Path('/tmp').resolve() or a.cloudflared.name!='cloudflared':raise RuntimeError('Verified temporary tunnel required')
 for line in a.env_file.read_text().splitlines():
  k,s,v=line.partition('=')
  if s and k.strip()=='RESEND_API_KEY':api_key=v.strip().strip('\"\'')
 if not api_key or not api_key.startswith('re_'):raise RuntimeError('Sending key missing')
 for port in (55529,15229,15329):
  import socket
  with socket.socket() as s:
   if s.connect_ex(('127.0.0.1',port))==0:raise RuntimeError('Live owned port occupied')
 with tempfile.TemporaryDirectory(prefix='devcoreblog-provider-live-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);web=f/'web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot')
  processes=[];server=None;started=False;tunnel_output=None;web_output=None
  connection=f'Host=127.0.0.1;Port=55529;Database=provider_live;Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(q):return run(['psql','-X','-h','127.0.0.1','-p','55529','-d','provider_live','-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  route='/f36-'+uuid.uuid4().hex
  def forward(body,headers):
   request=urllib.request.Request('http://127.0.0.1:15229/api/webhooks/resend',data=body,method='POST',headers={**headers,'Content-Type':'application/json'})
   try:
    with urllib.request.urlopen(request,timeout=12) as response:response.read(1024);return response.status
   except urllib.error.HTTPError as e:e.read(1024);return e.code
   except Exception:return 503
  class Relay(http.server.BaseHTTPRequestHandler):
   def log_message(self,*args):pass
   def do_GET(self):self.send_error(404)
   def do_POST(self):
    if self.path!=route:self.send_error(404);return
    try:
     n=int(self.headers.get('Content-Length','0'))
     if n<1 or n>65536 or self.headers.get('Transfer-Encoding') is not None:self.send_error(413);return
     if any(len(self.headers.get_all(name,[]))!=1 for name in ('svix-id','svix-timestamp','svix-signature')):self.send_error(401);return
     self.connection.settimeout(10);body=self.rfile.read(n)
     if len(body)!=n:self.send_error(400);return
     headers={name:self.headers[name] for name in ('svix-id','svix-timestamp','svix-signature')}
     code=forward(body,headers)
     if code==204:
      with lock:
       if len(callbacks)<12:callbacks.append((body,headers))
     self.send_response(code);self.send_header('Cache-Control','no-store');self.send_header('Content-Length','0');self.end_headers()
    except Exception:
     try:self.send_error(503)
     except Exception:pass
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
   run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p 55529 -k {owned}/socket','-w','start']);started=True
   run(['createdb','-h','127.0.0.1','-p','55529','provider_live'])
   run(['dotnet','ef','database','update','20261014175940_ProviderEmailEvents','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection})
   for address in ('delivered@resend.dev','bounced@resend.dev','complained@resend.dev'):
    sql(f'''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version")
     VALUES (gen_random_uuid(),'{address}','{address}',1,0,now(),now(),now(),1)''')
   server=http.server.ThreadingHTTPServer(('127.0.0.1',15329),Relay);threading.Thread(target=server.serve_forever,daemon=True).start()
   tunnel_output=(f/'tunnel.log').open('w');tunnel=subprocess.Popen([str(a.cloudflared),'tunnel','--no-autoupdate','--protocol','http2','--url','http://127.0.0.1:15329'],env=env,stdout=tunnel_output,stderr=subprocess.STDOUT);processes.append(tunnel)
   public=None
   for _ in range(120):
    if tunnel.poll() is not None:raise RuntimeError('Temporary tunnel failed')
    found=re.search(r'https://[a-z0-9-]+\.trycloudflare\.com',(f/'tunnel.log').read_text())
    if found:public=found.group(0);break
    time.sleep(.25)
   if public is None:raise RuntimeError('Temporary tunnel URL unavailable')
   endpoint=public+route
   status,webhook=api('POST','/webhooks',{'endpoint':endpoint,'events':['email.delivered','email.bounced','email.complained']})
   created_id=str(uuid.UUID(webhook['id']));signing_secret=webhook['signing_secret'];check('temporary_resend_webhook_created',status in (200,201) and signing_secret.startswith('whsec_'))
   hash_result=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='synthetic-live-provider-password\n',env=env,cwd=root,capture_output=True,text=True,timeout=20)
   if hash_result.returncode:raise RuntimeError('Synthetic hash failed')
   base='http://127.0.0.1:15229';app_env={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':base,
    'ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hash_result.stdout.strip(),'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),
    'SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false',
    'RESEND_WEBHOOK_ENABLED':'true','RESEND_WEBHOOK_SECRET':signing_secret,'RESEND_WEBHOOK_ALLOWED_SENDERS':'onboarding@resend.dev'}
   web_output=(f/'http.log').open('w');web_process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env=app_env,stdout=web_output,stderr=subprocess.STDOUT);processes.append(web_process)
   for _ in range(100):
    if web_process.poll() is not None:raise RuntimeError('Live owned application failed')
    try:
     if urllib.request.urlopen(base+'/fixture-admin/login',timeout=2).status==200:break
    except Exception:time.sleep(.1)
   else:raise RuntimeError('Live owned application readiness failed')
   # Wait for external DNS/TLS; the relay exposes neither the app UI nor the administrator.
   for _ in range(40):
    try:urllib.request.urlopen(endpoint,timeout=3)
    except urllib.error.HTTPError as e:
     if e.code==404:break
    except Exception:pass
    time.sleep(.5)
   else:raise RuntimeError('Public callback-only relay unreachable')
   def public_status(url,data=None):
    try:
     with urllib.request.urlopen(urllib.request.Request(url,data=data),timeout=5) as response:return response.status
    except urllib.error.HTTPError as error:return error.code
   check('relay_rejects_public_get_and_other_routes',public_status(public+'/fixture-admin/login')==404
    and public_status(public+'/other',b'{}')==404 and public_status(endpoint,b'{}')==401)
   expected={}
   for address,kind in [('delivered@resend.dev',0),('bounced@resend.dev',1),('complained@resend.dev',2)]:
    status,sent=api('POST','/emails',{'from':'Aether Letter Test <onboarding@resend.dev>','to':[address],'subject':'F36 synthetic callback acceptance','text':'Synthetic provider event test. No login link, personal data or campaign content.'})
    expected[str(uuid.UUID(sent['id']))]=(address,kind);check('test_send_'+str(kind)+'_accepted',status in (200,201));time.sleep(.6)
   observed=False
   for i in range(120):
    observed=all(sql(f'''SELECT count(*) FROM "ProviderEmailEvents" WHERE "MessageId"='{message}' AND "AddressKey"='{address}' AND "Kind"={kind}''')!='0' for message,(address,kind) in expected.items())
    if observed:break
    if i%20==0:print('Waiting for authenticated Resend simulator callbacks',flush=True)
    time.sleep(1)
   check('real_resend_signed_delivered_bounced_complained_callbacks',observed)
   check('real_provider_source_flags_correct',sql('''SELECT string_agg("AddressKey" || ':' || "Suppression",',' ORDER BY "AddressKey") FROM "Subscribers"''')=='bounced@resend.dev:1,complained@resend.dev:2,delivered@resend.dev:0')
   before=sql('SELECT json_agg(t ORDER BY "EventId") FROM "ProviderEmailEvents" t')+sql('SELECT json_agg(t ORDER BY "Id") FROM "Subscribers" t')
   with lock:replay=list(callbacks)
   replay_ok=bool(replay) and all(forward(body,headers)==204 for body,headers in replay)
   after=sql('SELECT json_agg(t ORDER BY "EventId") FROM "ProviderEmailEvents" t')+sql('SELECT json_agg(t ORDER BY "Id") FROM "Subscribers" t')
   check('actual_provider_payload_replay_is_no_write',replay_ok and before==after)
   check('no_real_worker_jobs_or_campaigns',sql('SELECT count(*) FROM "TransactionalEmailJobs"')=='0' and sql('SELECT count(*) FROM "NewsletterCampaigns"')=='0')
   web_output.flush();log=(f/'http.log').read_text();check('provider_secret_and_payload_not_logged',signing_secret not in log and api_key not in log and 'F36 synthetic callback acceptance' not in log and all(x not in log for x in ('delivered@resend.dev','bounced@resend.dev','complained@resend.dev')))
  finally:
   check('temporary_provider_webhook_deleted',delete_owned_endpoint())
   for process in reversed(processes):terminate(process)
   if server is not None:server.shutdown();server.server_close()
   if tunnel_output is not None:tunnel_output.close()
   if web_output is not None:web_output.close()
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_and_tunnel_stopped',all(x.poll() is not None for x in processes) and run(['pg_isready','-h','127.0.0.1','-p','55529'],required=False).returncode!=0)
 check('owned_live_data_removed',not f.exists())
except Exception as e:checks['live_fixture_completed']=False;print('Live fixture failed: '+(str(e) if isinstance(e,RuntimeError) else type(e).__name__),flush=True)
finally:
 a.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Three fixed Resend simulator addresses; actual signed callbacks through temporary callback-only tunnel and owned PostgreSQL; no personal mailbox delivery or production configuration.'};a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
