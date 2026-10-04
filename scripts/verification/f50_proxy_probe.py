#!/usr/bin/env python3
"""Real Production HTTP/TLS fixture, emulating the approved single-hop Nginx overwrite boundary."""
import argparse, getpass, http.client, json, os, ssl, subprocess, threading, time, urllib.parse
from http.cookies import SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from http_probe_support import extract_antiforgery_token
p=argparse.ArgumentParser()
p.add_argument('--security-headers', action='store_true', help='Run F52 header acceptance, including stopping the owned fixture database')
p.add_argument('--source',type=Path,required=True)
p.add_argument('--report',type=Path,required=True)
p.add_argument('--database-port',type=int,default=55452)
p.add_argument('--backend-port',type=int,default=15186)
p.add_argument('--proxy-port',type=int,default=15187)
a=p.parse_args()
assert all(1024 <= port <= 65534 for port in (a.database_port, a.backend_port, a.proxy_port))
assert a.backend_port not in (a.proxy_port, a.proxy_port + 1)
source=a.source.resolve();assert source.name=='source' and 'devcoreblog-f17.' in str(source) and not (source/'.git').exists()
tmp=source.parent; keys=tmp/'f50-keys';keys.mkdir(exist_ok=True);keys.chmod(0o700)
cert=tmp/'f50.crt';key=tmp/'f50.key'
subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-sha256','-nodes','-days','1','-subj','/CN=localhost',
    '-addext','subjectAltName=IP:127.0.0.1,DNS:localhost','-keyout',str(key),'-out',str(cert)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
key.chmod(0o600)
password='f50-isolated-password'
hash_result=subprocess.run(['dotnet','run','--no-build','--project',str(source/'tools/DevCoreBlog.PasswordHashTool'),'--','--stdin'],
    input=password+'\n',text=True,capture_output=True,check=True).stdout.strip()
env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Production','DOTNET_ENVIRONMENT':'Production',
    'DEPLOYMENT_PROFILE':'nginx-loopback','ASPNETCORE_URLS':f'http://127.0.0.1:{a.backend_port}',
    'SITE_URL':'https://blog.example.test','DATA_PROTECTION_KEYS_PATH':str(keys),
    'DB_CONNECTION_STRING':f'Host=127.0.0.1;Port={a.database_port};Database=devcoreblog_f01_test;Username={getpass.getuser()}',
    'ADMIN_USERNAME':'f50-admin','ADMIN_PASSWORD_HASH':hash_result,'ADMIN_SESSION_VERSION':'f50-1',
    'ADMIN_SESSION_LIFETIME_SECONDS':'1800','CLOUDINARY_CLOUD_NAME':'f50-test','CLOUDINARY_API_KEY':'f50-placeholder',
    'CLOUDINARY_API_SECRET':'f50-placeholder','WEBHOOK_API_SECRET':'f50-placeholder',
    'Security__LoginRateLimit__PermitLimit':'3','Security__LoginRateLimit__WindowSeconds':'300',
    'AllowedHosts':'*','ASPNETCORE_FORWARDEDHEADERS_ENABLED':'false'}
# Production must ignore an accidental dotenv file entirely, not overwrite service secrets/settings.
(source/'.env').write_text('ADMIN_USERNAME=wrong-dotenv-user\nADMIN_PASSWORD_HASH=invalid-placeholder\nSITE_URL=https://wrong-dotenv.example\nDEPLOYMENT_PROFILE=invalid\n')
proc=None;servers=[]
def start(overrides=None):
    global proc
    proc=subprocess.Popen([str(source/'bin/Debug/net10.0/DevCoreBlog')],cwd=source,env={**env,**(overrides or {})},stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    for _ in range(150):
        if proc.poll() is not None: raise RuntimeError('Production fixture exited before ready')
        try:
            c=http.client.HTTPConnection('127.0.0.1',a.backend_port,timeout=1);c.request('GET','/Account/Login',headers={'Host':'blog.example.test'});r=c.getresponse();r.read();c.close()
            if r.status==308:return
        except OSError:pass
        time.sleep(.1)
    raise RuntimeError('Production fixture not ready')
def stop():
    global proc
    if proc is not None:
        proc.terminate()
        try:proc.wait(timeout=10)
        except subprocess.TimeoutExpired:proc.kill();proc.wait()
        proc=None
class Proxy(BaseHTTPRequestHandler):
    protocol_version='HTTP/1.1'
    def log_message(self,*args):pass
    def relay(self):
        length=int(self.headers.get('Content-Length','0'))
        if length>10*1024*1024:self.send_error(413);return
        body=self.rfile.read(length) if length else None
        h={k:v for k,v in self.headers.items() if k.lower() not in ('x-forwarded-for','x-forwarded-proto','x-forwarded-host','x-forwarded-prefix','forwarded','connection','host','content-length')}
        h.update({'Host':self.headers.get('Host','blog.example.test'),
            'X-Forwarded-For':self.server.fixture_client_ip,'X-Forwarded-Proto':'https'})
        c=http.client.HTTPConnection('127.0.0.1',a.backend_port,timeout=10)
        c.request(self.command,self.path,body=body,headers=h);r=c.getresponse();data=r.read()
        self.send_response(r.status)
        for k,v in r.getheaders():
            if k.lower() not in ('transfer-encoding','connection','content-length','server','date'):self.send_header(k,v)
        self.send_header('Content-Length',str(len(data)));self.end_headers()
        if self.command!='HEAD':self.wfile.write(data)
        c.close()
    do_GET=do_POST=do_HEAD=relay
ctx=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);ctx.load_cert_chain(cert,key)
client_ctx=ssl.create_default_context(cafile=str(cert))
class Client:
    def __init__(self,port=None):self.port=port or a.proxy_port;self.cookies={}
    def call(self,path='/Account/Login',fields=None,headers=None,method=None):
        h={'Host':'blog.example.test',**(headers or {})}
        if self.cookies:h['Cookie']='; '.join(k+'='+v for k,v in self.cookies.items())
        data=urllib.parse.urlencode(fields).encode() if fields is not None else None
        if data is not None:h['Content-Type']='application/x-www-form-urlencoded'
        c=http.client.HTTPSConnection('127.0.0.1',self.port,context=client_ctx,timeout=10)
        c.request(method or ('POST' if fields is not None else 'GET'),path,body=data,headers=h);r=c.getresponse();pairs=r.getheaders();body=r.read().decode('utf-8','replace');status=r.status;c.close()
        for k,v in pairs:
            if k.lower()=='set-cookie':
                parsed=SimpleCookie();parsed.load(v)
                for name,value in parsed.items():self.cookies[name]=value.value
        return status,{k.lower():v for k,v in pairs},body,[v for k,v in pairs if k.lower()=='set-cookie']
checks={}
try:
    start()
    for port,client_ip in [(a.proxy_port,'198.51.100.11'),(a.proxy_port+1,'198.51.100.22')]:
        server=ThreadingHTTPServer(('127.0.0.1',port),Proxy);server.fixture_client_ip=client_ip
        server.socket=ctx.wrap_socket(server.socket,server_side=True);servers.append(server)
        threading.Thread(target=server.serve_forever,daemon=True).start()
    admin=Client()
    status,headers,body,cookies=admin.call(headers={'X-Forwarded-For':'203.0.113.99','X-Forwarded-Proto':'http','X-Forwarded-Host':'evil.example'})
    checks['trusted_https_proxy_has_no_redirect_loop']=status==200 and 'Admin Sign In' in body
    checks['production_hsts_on_forwarded_https']='max-age=2592000' in headers.get('strict-transport-security','')
    if a.security_headers:
        for route in ['/Account/Login', '/does-not-exist-f52', '/js/public-theme.js', '/rss.xml']:
            s,h,b,c=Client().call(route)
            checks[route + '_production_security_headers'] = h.get('x-content-type-options') == 'nosniff' and h.get('referrer-policy') == 'strict-origin-when-cross-origin' and h.get('x-frame-options') == 'DENY'
            html = route in ('/Account/Login', '/does-not-exist-f52')
            checks[route + '_production_csp_scope'] = bool(h.get('content-security-policy')) == html and 'content-security-policy-report-only' not in h
    checks['antiforgery_cookie_is_secure_httponly']=any('Antiforgery' in c and '; secure' in c.lower() and '; httponly' in c.lower() for c in cookies)
    token=extract_antiforgery_token(body);assert token
    status,h,b,cookies=admin.call(fields={'username':'f50-admin','password':password,'__RequestVerificationToken':token})
    checks['production_ignores_dotenv_and_login_succeeds']=status==302 and '/Admin/Dashboard' in h.get('location','')
    auth=[c for c in cookies if c.startswith('DevCoreBlog.Admin=')]
    # Cookie name comes from the actual application; avoid dumping its value.
    if not auth:auth=[c for c in cookies if 'Antiforgery' not in c]
    checks['admin_cookie_secure_httponly_lax_session']=bool(auth) and all('; secure' in c.lower() and '; httponly' in c.lower() and 'samesite=lax' in c.lower() and 'expires=' not in c.lower() for c in auth)
    checks['protected_admin_loads_through_tls_proxy']=admin.call('/AdminPost')[0]==200
    checks['logout_without_csrf_stays_400']=admin.call('/Account/Logout',fields={})[0]==400
    anon=Client();s,h,b,c=anon.call('/AdminPost');checks['authorization_redirect_uses_public_https_host']=s==302 and h.get('location','').startswith('https://blog.example.test/Account/Login')
    s,h,b,c=anon.call('/rss.xml',headers={'X-Forwarded-Host':'evil.example'});checks['public_document_keeps_trusted_site_origin']=s==200 and 'https://blog.example.test/' in b and 'evil.example' not in b
    checks['unknown_host_rejected_even_with_wildcard_config']=Client().call(headers={'Host':'evil.example'})[0]==400
    c=http.client.HTTPConnection('127.0.0.1',a.backend_port,timeout=5);c.request('GET','/Account/Login?returnUrl=%2FAdminPost',headers={'Host':'blog.example.test'});r=c.getresponse();location=r.getheader('Location','');r.read();c.close()
    checks['backend_http_redirect_is_single_public_https_308']=r.status==308 and location=='https://blog.example.test/Account/Login?returnUrl=%2FAdminPost'
    attacker=Client();s,h,b,c=attacker.call();t=extract_antiforgery_token(b);assert t
    statuses=[]
    for ip in ['203.0.113.1','203.0.113.2','203.0.113.3']:
        s,h,b,c=attacker.call(fields={'username':'wrong-user','password':'wrong-pass','__RequestVerificationToken':t},headers={'X-Forwarded-For':ip});statuses.append(s)
    checks['spoofed_client_ips_cannot_bypass_login_limit']=statuses==[200,200,429] and int(h.get('retry-after','0'))>0
    second=Client(a.proxy_port+1);s,h,b,c=second.call();t=extract_antiforgery_token(b);assert t
    checks['different_trusted_client_ip_has_own_bucket']=second.call(fields={'username':'wrong-user','password':'wrong-pass','__RequestVerificationToken':t})[0]==200
    stop();start()
    checks['same_persisted_keys_and_stamp_keep_session_after_restart']=admin.call('/AdminPost')[0]==200
    stop();start({'ADMIN_SESSION_VERSION':'f50-2'})
    checks['stamp_change_rejects_old_session']=admin.call('/AdminPost')[0]==302
    stop();start({'ADMIN_SESSION_VERSION':'f50-2','ADMIN_SESSION_LIFETIME_SECONDS':'1'})
    expiry=Client();s,h,b,c=expiry.call();t=extract_antiforgery_token(b);assert t
    s,h,b,c=expiry.call(fields={'username':'f50-admin','password':password,'__RequestVerificationToken':t});assert s==302
    time.sleep(2)
    checks['session_expires_without_sliding_renewal']=expiry.call('/AdminPost')[0]==302
    if a.security_headers:
        subprocess.run(['pg_ctl', '-D', str(tmp/'postgres'), '-m', 'fast', '-w', 'stop'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        s,h,b,c=Client().call('/post/f01-markdown-xss')
        checks['production_database_outage_preserves_500'] = s == 500 and 'no-store' in h.get('cache-control','')
        checks['production_500_keeps_csp_and_hsts'] = "style-src-attr 'none'" in h.get('content-security-policy','') and h.get('strict-transport-security') == 'max-age=2592000' and 'content-security-policy-report-only' not in h
        checks['production_500_keeps_other_security_headers'] = h.get('x-content-type-options') == 'nosniff' and h.get('referrer-policy') == 'strict-origin-when-cross-origin' and h.get('x-frame-options') == 'DENY'
    a.report.write_text(json.dumps({'checks':checks,'count':len(checks),'scope':'Production app + verified local TLS + Python Nginx-protocol fixture; not a real Nginx installation'},indent=2))
    print(json.dumps(checks,indent=2));assert all(checks.values())
finally:
    stop()
    for server in servers:server.shutdown();server.server_close()
