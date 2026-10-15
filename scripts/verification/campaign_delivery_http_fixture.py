"""Actual TLS Nginx -> trusted forwarded MVC -> PostgreSQL one-click protocol and log privacy acceptance."""
import json,re,shutil,socket,ssl,subprocess,time,urllib.error,urllib.parse,urllib.request
from pathlib import Path

def run_http_fixture(root,fixture,base_env,connection,pg_port,nginx,checks):
 nginx=str(nginx or shutil.which('nginx') or '')
 if not nginx or not Path(nginx).is_file():raise RuntimeError('Nginx test prerequisite absent; pass --nginx or install the supported proxy test tool.')
 for port in (15230,15430,15530):
  with socket.socket() as s:
   if s.connect_ex(('127.0.0.1',port))==0:raise RuntimeError('Owned TLS test port occupied')
 base='https://localhost:15530';plain='http://127.0.0.1:15430';backend='http://127.0.0.1:15230';web=fixture/'web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot')
 proxy=fixture/'proxy';proxy.mkdir();(proxy/'logs').mkdir();proof_file=fixture/'one-click.json';log_file=fixture/'web.log';access=proxy/'access.log';native=proxy/'native.log';proofs=[];ids=[]
 def command(cmd,extra=None,required=True):
  r=subprocess.run(cmd,cwd=root,env={**base_env,**(extra or {})},capture_output=True,text=True,timeout=45)
  if required and r.returncode:raise RuntimeError('Owned TLS command failed: '+cmd[0])
  return r
 def stop(process):
  if process is not None and process.poll() is None:
   process.terminate()
   try:process.wait(timeout=10)
   except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
 def check(name,value):checks[name]=bool(value);print(name+'='+str(bool(value)).lower(),flush=True)
 def sql(q):return command(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','delivery_http','-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
 def state(identity):return sql('SELECT "Status" || \':\' || "Version" FROM "Subscribers" WHERE "Id"=\''+identity+'\'')
 command(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-keyout',str(proxy/'key.pem'),'-out',str(proxy/'cert.pem'),'-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1'])
 context=ssl.create_default_context(cafile=str(proxy/'cert.pem'))
 class NoRedirect(urllib.request.HTTPRedirectHandler):
  def redirect_request(self,*args):return None
 client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPSHandler(context=context))
 def request(path='/abonelik/tek-tik',body=None,method=None,mime=None,headers=None,origin=base):
  req=urllib.request.Request(origin+path,data=body,method=method,headers={**({'Content-Type':mime} if mime else {}),**(headers or {})})
  try:
   with client.open(req,timeout=15) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
  except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
 def seed():
  r=command(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--campaign-delivery-http-seed'],
   {'DB_CONNECTION_STRING':connection,'CAMPAIGN_DELIVERY_PROOF_FILE':str(proof_file),'CAMPAIGN_DELIVERY_TEST_ORIGIN':base})
  if not json.loads(r.stdout).get('passed'):raise RuntimeError('Owned one-click seed failed')
  data=json.loads(proof_file.read_text());proofs.extend([data['token'],data['humanToken']]);ids.append(data['subscriberId']);return data
 template=(root/'deployment/nginx/devcoreblog.conf.example').read_text()
 template=template.replace('blog.example','localhost').replace('/var/log/nginx/aethernotes-access.log',str(access))
 template=re.sub(r'^\s*listen \[::\].*\n','',template,flags=re.M).replace('listen 80','listen 127.0.0.1:15430').replace('listen 443','listen 127.0.0.1:15530')
 template=template.replace('/etc/letsencrypt/live/localhost/fullchain.pem',str(proxy/'cert.pem')).replace('/etc/letsencrypt/live/localhost/privkey.pem',str(proxy/'key.pem'))
 template=template.replace('http://127.0.0.1:5000',backend)
 config='daemon off; master_process off; pid '+str(proxy/'nginx.pid')+'; error_log '+str(native)+'; events { worker_connections 64; } http { '+template+' }'
 conf=proxy/'nginx.conf';conf.write_text(config)
 check('actual_nginx_full_deployment_template_parses',command([nginx,'-p',str(proxy)+'/', '-c',str(conf),'-t'],required=False).returncode==0)
 if not checks['actual_nginx_full_deployment_template_parses']:raise RuntimeError('Owned full Nginx template invalid')
 # Password input is a synthetic value; never inherit the real app dotenv.
 password=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='synthetic-protocol-password\n',cwd=root,env=base_env,capture_output=True,text=True,timeout=20)
 if password.returncode:raise RuntimeError('Synthetic TLS fixture hash failed')
 app_env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':backend,'SITE_URL':base,
  'DEPLOYMENT_PROFILE':'nginx-loopback','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password.stdout.strip(),
  'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),'SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false',
  'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','RESEND_WEBHOOK_ENABLED':'false',
  'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics':'Trace','Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel.Connections':'Trace',
  'Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware':'Trace','Logging__LogLevel__Microsoft.AspNetCore.HttpLogging':'Trace',
  'Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
 output=log_file.open('w');proxy_output=(proxy/'process.log').open('w');app=None;server=None
 def start_proxy(unsafe=False):
  nonlocal server
  stop(server);conf.write_text(config.replace('"path":"$uri"','"path":"$request_uri"') if unsafe else config)
  server=subprocess.Popen([nginx,'-p',str(proxy)+'/', '-c',str(conf)],env=base_env,stdout=proxy_output,stderr=subprocess.STDOUT)
  for _ in range(100):
   if server.poll() is not None:raise RuntimeError('Owned Nginx failed to start')
   try:
    if request()[0]==204:return
   except OSError:pass
   time.sleep(.1)
  raise RuntimeError('TLS proxy readiness timed out')
 def start_app():
  nonlocal app
  stop(app);app=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',backend],cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
  for _ in range(100):
   if app.poll() is not None:raise RuntimeError('Owned campaign HTTP application failed')
   try:
    if request(origin=backend)[0]==400:return
   except OSError:pass
   time.sleep(.1)
  raise RuntimeError('Owned application readiness timed out')
 try:
  start_app();start_proxy();d=seed();identity=d['subscriberId'];path='/abonelik/tek-tik?t='+d['token'];standard=b'List-Unsubscribe=One-Click';mime='application/x-www-form-urlencoded'
  status,h,_=request(path);head=request(path,method='HEAD')
  check('https_get_head_and_scanner_do_not_change_subscription',status==204 and head[0]==204 and state(identity)=='1:2')
  check('https_token_responses_are_private',h.get('referrer-policy')=='no-referrer' and 'no-store' in h.get('cache-control','') and 'noindex' in h.get('x-robots-tag','') and h.get('x-content-type-options')=='nosniff' and h.get('x-frame-options')=='DENY')
  check('plaintext_proxy_and_direct_backend_never_redirect_proof',all(r[0]==400 and 'location' not in r[1] for r in [request(path,body=standard,mime=mime,origin=plain,headers={'Host':'localhost'}),request(path,body=standard,mime=mime,origin=backend)]))
  variants=['/abonelik/tek-tik/?t='+d['token'],'/ABONELIK/TEK-TIK?t='+d['token']]
  check('path_case_and_trailing_slash_preserve_https_private_boundary',all(request(v)[0]==204 and request(v)[1].get('referrer-policy')=='no-referrer'
   and request(v,body=standard,mime=mime,origin=backend)[0]==400
   and request(v,body=standard,mime=mime,origin=plain,headers={'Host':'localhost'})[0]==400 for v in variants))
  early=request(path,body=standard,mime=mime,origin=plain,headers={'Host':'localhost'})
  check('early_proxy_rejection_preserves_exit_privacy_headers','no-store' in early[1].get('cache-control','') and early[1].get('referrer-policy')=='no-referrer' and 'noindex' in early[1].get('x-robots-tag',''))
  check('cookie_authorization_and_wrong_body_cannot_exit',request(path,body=standard,mime=mime,headers={'Cookie':'Synthetic=1'})[0]==400
   and request(path,body=standard,mime=mime,headers={'Authorization':'Bearer synthetic'})[0]==400
   and request(path,body=b'List-Unsubscribe=wrong',mime=mime)[0]==400
   and request(path,body=standard+b'&List-Unsubscribe=One-Click',mime=mime)[0]==400 and state(identity)=='1:2')
  check('query_bound_and_purpose_isolated_proof_required',request('/abonelik/tek-tik?t='+d['humanToken'],body=standard,mime=mime)[0]==400
   and request('/abonelik/tek-tik?t='+'x'*100,body=standard,mime=mime)[0]==400
   and request(path+'&extra=1',body=standard,mime=mime)[0]==400 and request(path+'&t='+d['token'],body=standard,mime=mime)[0]==400)
  check('protocol_body_limit_and_content_type_enforced',request(path,body=b'x'*3000,mime=mime)[0]==413
   and request(path,body=b'{}',mime='application/json')[0]==415)
  check('cookie_free_standard_post_exits_with_all_workers_disabled',request(path,body=standard,mime=mime)[0]==204 and state(identity)=='2:3')
  start_app();check('restart_and_oneclick_replay_are_idempotent',request(path,body=standard,mime=mime)[0]==204 and state(identity)=='2:3')
  sql('UPDATE "Subscribers" SET "Status"=1,"VerifiedUtc"="UnsubscribedUtc","Version"="Version"+2,"PeriodId"=gen_random_uuid() WHERE "Id"=\''+identity+'\'')
  check('old_oneclick_proof_cannot_exit_new_subscription_period',request(path,body=standard,mime=mime)[0]==400 and state(identity)=='1:5')
  d=seed();path='/abonelik/tek-tik?t='+d['token'];boundary='f37fixture'
  multipart=('--'+boundary+'\r\nContent-Disposition: form-data; name="List-Unsubscribe"\r\n\r\nOne-Click\r\n--'+boundary+'--\r\n').encode()
  check('standard_multipart_form_also_exits',request(path,body=multipart,mime='multipart/form-data; boundary='+boundary)[0]==204 and state(d['subscriberId'])=='2:3')
  check('human_exit_retains_antiforgery_requirement',request('/abonelik/cik',body=b'Token='+d['humanToken'].encode(),mime=mime)[0]==400)
  # Negative control proves a conventional raw-query access log would leak the proof.
  start_proxy(unsafe=True);request(path);stop(server);leaked=d['token'] in access.read_text();access.write_text('');start_proxy()
  check('proxy_log_negative_control_detects_query_proof_leak',leaked)
  d=seed();path='/abonelik/tek-tik?t='+d['token'];request(path);request(path,body=standard,mime=mime);request(path,headers={'Host':'invalid.example.test'})
  for _ in range(125):
   if request(path)[0]==429:break
  status,h,_=request(path);check('real_429_retains_private_headers',status==429 and 'no-store' in h.get('cache-control','') and h.get('referrer-policy')=='no-referrer')
  start_app();d=seed();path='/abonelik/tek-tik?t='+d['token']
  command(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'])
  try:
   status,h,body=request(path,body=standard,mime=mime);check('database_outage_returns_safe_503_without_token_echo',status==503 and d['token'] not in body and 'SELECT' not in body and 'no-store' in h.get('cache-control',''))
  finally:command(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'])
  check('outage_preserves_subscription_and_later_exit',state(d['subscriberId'])=='1:2' and request(path,body=standard,mime=mime)[0]==204 and state(d['subscriberId'])=='2:3')
 finally:
  stop(server);stop(app);output.close();proxy_output.close()
 logs=log_file.read_text()+access.read_text()+(native.read_text() if native.exists() else '')+(proxy/'process.log').read_text()
 check('actual_proxy_and_app_logs_exclude_proofs_addresses_and_private_ids',all(t not in logs for t in proofs+ids) and '@example.test' not in logs)
 check('specific_raw_request_diagnostic_overrides_cannot_reenable_url_logging','Request starting' not in logs and 'Attempting to bind' not in logs)
 check('safe_proxy_log_still_records_path_status_and_request_id','"path":"/abonelik/tek-tik"' in access.read_text() and '"status":204' in access.read_text() and '"requestId":' in access.read_text())
