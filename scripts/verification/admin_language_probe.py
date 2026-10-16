#!/usr/bin/env python3
"""Real disposable PG/MVC form errors and optional Chrome. No real credentials/settings or provider calls."""
import argparse,getpass,html,http.cookiejar,json,os,socket,subprocess,tempfile,time,urllib.request,urllib.error,urllib.parse
from pathlib import Path
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
def run(cmd,extra=None,input=None,timeout=180):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},input=input,capture_output=True,text=True,timeout=timeout)
 if r.returncode:raise RuntimeError('Owned language command failed: '+cmd[0]+' exit='+str(r.returncode)+((': '+r.stdout[-800:]) if cmd[0]=='node' else ''))
 return r

def check(name,ok):
 checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
 # Independent checks continue so a single run reports the complete acceptance matrix.
for port in (55538,15238):
 with socket.socket() as sock:
  if sock.connect_ex(('127.0.0.1',port))==0:raise SystemExit('Owned language port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-language-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);proc=None;pg=False;log=f/'web.log';stream=log.open('w');base='http://127.0.0.1:15238'
  connection=f'Host=127.0.0.1;Port=55538;Database=language;Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(q):return run(['psql','-X','-h','127.0.0.1','-p','55538','-d','language','-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def snapshot():return '\n'.join(sql('SELECT to_jsonb(t)::text FROM "'+t+'" t ORDER BY 1') for t in ('Posts','Categories','PublicationSettings','NewsletterCampaigns','Subscribers','CampaignDispatches','TransactionalEmailJobs'))
  def stop():
   if proc is not None and proc.poll() is None:
    proc.terminate()
    try:proc.wait(timeout=10)
    except subprocess.TimeoutExpired:proc.kill();proc.wait(timeout=10)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p 55538 -k {owned}/socket','-w','start']);pg=True
   run(['createdb','-h','127.0.0.1','-p','55538','language']);run(['dotnet','ef','database','update','--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection})
   source,packet,control,clock,gate=build_fixture_host(root,f,env)
   hashed=run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='f44-synthetic-password\n').stdout.strip()
   settings={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':'https://aether.example.test','SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hashed,'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(f/'keys'),'NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','READER_LOGIN_ENABLED':'false','SUBSCRIPTIONS_ENABLED':'false','TEST_EMAIL_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false','RESEND_API_KEY':'','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
   proc=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(source),'--urls',base],cwd=source,env=settings,stdout=stream,stderr=subprocess.STDOUT)
   class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
   client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
   def request(path,data=None,method=None):
    req=urllib.request.Request(base+path,data=urllib.parse.urlencode(data).encode() if data is not None else None,method=method)
    try:
     with client.open(req,timeout=15) as r:return r.status,{k.lower():v for k,v in r.headers.items()},r.read().decode()
    except urllib.error.HTTPError as e:return e.code,{k.lower():v for k,v in e.headers.items()},e.read().decode()
   for _ in range(150):
    if proc.poll() is not None:raise RuntimeError('Owned language app exited')
    try:
     if request('/fixture-admin/login')[0]==200:break
    except OSError:pass
    time.sleep(.1)
   else:raise RuntimeError('Owned language app readiness failed')
   values=Fields(request('/fixture-admin/login')[2]).values;bad=request('/fixture-admin/login',{**values,'username':'absent','password':'invalid'})
   check('login_error_turkish_and_associated',bad[0]==200 and 'Kullanıcı adı veya parola hatalı.' in html.unescape(bad[2]) and 'login-error' in bad[2] and 'aria-invalid="true"' in bad[2])
   values=Fields(bad[2]).values;check('real_admin_login',request('/fixture-admin/login',{**values,'username':'fixture-admin','password':'f44-synthetic-password'})[0]==302)
   before=snapshot()
   paths=['/Admin/Dashboard','/Admin/Automations','/AdminPost','/AdminCategory','/AdminPublication','/AdminSubscriber','/AdminCampaign','/AdminNewsletter','/AdminWriting/Create','/AdminPost/Create']
   pages=[request(path) for path in paths]
   check('all_admin_screens_turkish_and_available',all(r[0]==200 and '<html lang="tr">' in r[2] and 'Yönetim menüsü' in r[2] for r in pages))
   check('admin_reads_no_mutation',before==snapshot())
   def post(path,changes):
    token=Fields(request(path)[2]).values['__RequestVerificationToken'];return request(path,{**changes,'__RequestVerificationToken':token})
   bad=post('/AdminCategory/Create',{'Name':''})
   check('category_required_error_on_field',bad[0]==200 and 'Kategori adı gereklidir.' in html.unescape(bad[2]) and 'id="Name__error"' in bad[2] and 'aria-describedby="Name__error"' in bad[2] and 'aria-invalid="true"' in bad[2])
   bad=post('/AdminPost/Create',{'Title':'','Content':'','CategoryId':'not-a-number','PublishDate':'not-a-date','SaveAction':'99','IsActive':'invalid'})
   check('post_invalid_fields_turkish_without_input_echo',bad[0]==200 and all(s in html.unescape(bad[2]) for s in ('Başlık gereklidir.','İçerik gereklidir.','Bu alanın değeri geçersiz.')) and all(s not in html.unescape(bad[2]) for s in ('is required.','is invalid.','is not valid for')))
   bad=post('/AdminWriting/Create',{'Title':'','DocumentJson':'{}','CategoryId':'invalid','PublishDate':'invalid','SaveAction':'SaveDraft','IsActive':'invalid'})
   check('document_metadata_binding_uses_turkish',bad[0]==400 and 'Bu alanın değeri geçersiz.' in html.unescape(bad[2]) and 'id="CategoryId__error"' in bad[2] and 'id="IsActive__error"' in bad[2])
   # Publication has a wrapper display model but an unprefixed HTTP allowlist.
   bad=request('/AdminPublication/Save',{'Name':'','Cadence':'','Version':'0','__RequestVerificationToken':Fields(request('/AdminPublication')[2]).values['__RequestVerificationToken']})
   check('publication_field_errors_and_real_400',bad[0]==400 and all(s in html.unescape(bad[2]) for s in ('Yayın adı gereklidir.','Hedef ritim bilgisi gereklidir.','id="Name__error"','id="Cadence__error"')))
   lexical=request('/AdminPublication/Save',{'Name':'<b>Yayın</b>','Cadence':'Haftalık','Version':'0','__RequestVerificationToken':Fields(request('/AdminPublication')[2]).values['__RequestVerificationToken']})
   check('domain_text_error_attaches_to_publication_field',lexical[0]==400 and 'başında/sonunda boşluk' in html.unescape(lexical[2]) and 'aria-invalid="true"' in lexical[2])
   check('invalid_forms_do_not_change_stored_data',before==snapshot())
   csrf=request('/AdminCategory/Create',{'Name':'Forged'});check('csrf_status_remains_400',csrf[0]==400)
   invalid=request('/AdminPost?page=bad');check('invalid_filter_remains_400_turkish',invalid[0]==400 and 'Filtreler geçersiz.' in html.unescape(invalid[2]))
   missing=request('/AdminCampaignMetrics?id=00000000-0000-0000-0000-000000000001');check('missing_campaign_remains_404',missing[0]==404)
   check('error_responses_keep_privacy_and_CSP',all('no-store' in r[1].get('cache-control','') and 'noindex' in r[1].get('x-robots-tag','') and r[1].get('referrer-policy')=='no-referrer' and ('content-security-policy' in r[1] if r[1].get('content-type','').startswith('text/html') else not r[2] or r[1].get('content-type','').startswith('text/plain')) for r in (bad,csrf,invalid,missing)))
   created=post('/AdminCategory/Create',{'Name':'F44 HTTP category'})
   check('valid_category_form_saves_via_PRG',created[0]==302 and sql('SELECT count(*) FROM "Categories" WHERE "Name"=\'F44 HTTP category\'')=='1')
   if a.browser_module and a.browser_executable:
    run(['node',str(root/'scripts/verification/admin_language_browser_probe.cjs')],{'DEVCORE_PLAYWRIGHT_MODULE':str(a.browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(a.browser_executable),'F44_REPORT_DIR':str(a.report.parent)})
    b=json.loads((a.report.parent/'admin-language-browser.json').read_text());check('real_chrome_desktop_mobile_keyboard',b['passed'])
   check('no_provider_call_or_send_work',not packet.exists() and sql('SELECT count(*) FROM "TransactionalEmailJobs"')=='0' and sql('SELECT count(*) FROM "CampaignDispatches"')=='0')
   stop();stream.flush();check('trace_logs_do_not_contain_submitted_values',all(v not in log.read_text() for v in ('f44-synthetic-password','not-a-number','not-a-date','Host=','SELECT ')))
  finally:
   stop();stream.close()
   if pg:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'])
 check('owned_host_cluster_and_keys_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 result={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real isolated PG/MVC validation and optional Chrome. Source-copy sender only detects unintended sends; no provider calls.'};a.report.write_text(json.dumps(result,indent=2)+'\n')
raise SystemExit(0 if result['passed'] else 1)
