"""Shared isolated real MVC fixture. Synthetic proofs and application logs never leave the owned temp directory."""
import http.cookiejar,json,os,re,shutil,subprocess,time,urllib.error,urllib.parse,urllib.request
from pathlib import Path

def run_http_fixture(root,fixture,base_env,connection,pg_port,report_dir,checks,browser_module,browser_executable):
    port=15222; base=f'http://127.0.0.1:{port}'; web=fixture/'web'; web.mkdir()
    shutil.copytree(root/'wwwroot',web/'wwwroot')
    proof_file=fixture/'synthetic-proof.json'; app_log=fixture/'app.log'
    env={**base_env,'DB_CONNECTION_STRING':connection,'OUTBOX_TEST_KEYS':str(fixture/'keys'),
         'VERIFICATION_TEST_PROOF_FILE':str(proof_file)}
    def seed():
        result=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--verification-seed'],
                              cwd=root,env=env,capture_output=True,text=True,timeout=30)
        if result.returncode: raise RuntimeError('Synthetic proof seed failed')
        return json.loads(proof_file.read_text())
    password=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],
                            input='synthetic-verification-password\n',cwd=root,env=base_env,capture_output=True,text=True,timeout=20)
    if password.returncode: raise RuntimeError('Synthetic admin hash failed')
    app_env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,
             'SITE_URL':base,'SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password.stdout.strip(),
             'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),
             'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'true','Logging__LogLevel__Default':'Information',
             'Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
    jar=http.cookiejar.CookieJar(); client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    def request(path='/abonelik/dogrula',data=None,method=None):
        req=urllib.request.Request(base+path,data=urllib.parse.urlencode(data).encode() if data is not None else None,method=method)
        try:
            with client.open(req,timeout=20) as response:return response.status,dict(response.headers),response.read().decode()
        except urllib.error.HTTPError as error:return error.code,dict(error.headers),error.read().decode()
    def sql(query):
        return subprocess.run(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','verification_http','-At','-v','ON_ERROR_STOP=1','-c',query],
                              env=base_env,capture_output=True,text=True,check=True).stdout.strip()
    def state(identity):return sql('SELECT "Status" || \':\' || "Version" FROM "Subscribers" WHERE "Id"=\''+identity+'\'')
    def headers(values):return {k.lower():v for k,v in values.items()}
    raw_proofs=[]
    with app_log.open('w') as output:
        process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],
                                 cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
        try:
            for _ in range(100):
                if process.poll() is not None:raise RuntimeError('Verification application did not start')
                try:
                    status,h,body=request()
                    if status==200:break
                except OSError:time.sleep(.1)
            else:raise RuntimeError('Verification HTTP readiness timed out')
            seed1=seed(); token=seed1['token'];identity=seed1['subscriberId'];raw_proofs.append(token)
            status,h,body=request('/abonelik/dogrula#t='+token)
            h=headers(h)
            checks['http_get_does_not_consume']=status==200 and state(identity)=='0:1'
            checks['http_private_security_headers']=h.get('referrer-policy')=='no-referrer' and 'no-store' in h.get('cache-control','') and 'noindex' in h.get('x-robots-tag','')
            checks['http_csp_enforced']=bool(h.get('content-security-policy')) and "script-src 'self'" in h['content-security-policy'] and 'unsafe-eval' not in h['content-security-policy']
            checks['http_no_proof_in_rendered_html']=token not in body and 'value=""' in body
            checks['http_missing_csrf_rejected']=request(data={'Token':token})[0]==400 and state(identity)=='0:1'
            antiforgery=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
            status,h,result=request(data={'Token':token,'__RequestVerificationToken':antiforgery})
            checks['http_post_confirms_atomically']=status==200 and state(identity)=='1:2' and 'E-posta adresiniz doğrulandı' in result
            checks['http_no_login_cookie_or_token_echo']=token not in result and all(not cookie.name.startswith(('.AspNetCore.Cookies','AetherNotes.Admin','AetherNotes.Reader')) for cookie in jar)
            checks['http_replay_rejected']=request(data={'Token':token,'__RequestVerificationToken':antiforgery})[0]==400 and state(identity)=='1:2'
            checks['http_oversized_proof_rejected']=request(data={'Token':'x'*44,'__RequestVerificationToken':antiforgery})[0]==400
            checks['http_query_does_not_supply_post_proof']=request('/abonelik/dogrula?Token='+('x'*43),{'__RequestVerificationToken':antiforgery})[0]==400
            checks['http_head_safe']=request(method='HEAD')[0]==200 and state(identity)=='1:2'
            # Fresh server process resets only in-memory IP rate buckets; proof and subscriber persistence remain.
            process.terminate();process.wait(timeout=10)
            process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
            for _ in range(100):
                try:
                    if request()[0]==200:break
                except OSError:time.sleep(.1)
            checks['http_restart_preserves_consumed_proof']=request(data={'Token':token,'__RequestVerificationToken':antiforgery})[0]==400 and state(identity)=='1:2'
            # Rate limit rejects bounded excess reads with a real 429, preserving private headers.
            statuses=[request()[0] for _ in range(22)]
            checks['http_rate_limit_429']=429 in statuses
            process.terminate();process.wait(timeout=10)
            process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
            for _ in range(100):
                try:
                    if request()[0]==200:break
                except OSError:time.sleep(.1)
            if browser_module:
                browser_seed=seed();raw_proofs.append(browser_seed['token'])
                result=subprocess.run(['node',str(root/'scripts/verification/subscriber_verification_browser_probe.cjs')],cwd=root,
                    env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable) if browser_executable else '',
                         'VERIFICATION_TEST_PROOF_FILE':str(proof_file),'VERIFICATION_TEST_BASE':base,'VERIFICATION_TEST_PG_PORT':str(pg_port),'VERIFICATION_TEST_REPORT':str(report_dir/'browser.json'),
                         'VERIFICATION_TEST_SCREENSHOT':str(report_dir/'confirmation.png')},capture_output=True,text=True,timeout=90)
                checks['real_chrome_acceptance']=result.returncode==0 and json.loads((report_dir/'browser.json').read_text())['passed']
            outage=seed();raw_proofs.append(outage['token'])
            _,_,outage_page=request()
            csrf=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',outage_page).group(1)
            stopped=subprocess.run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],env=base_env,capture_output=True,text=True,timeout=20)
            if stopped.returncode: raise RuntimeError('Owned outage setup failed')
            try:
                outage_status,_,outage_body=request(data={'Token':outage['token'],'__RequestVerificationToken':csrf})
                checks['http_real_db_outage_is_safe_503']=outage_status==503 and outage['token'] not in outage_body and 'SELECT' not in outage_body
            finally:
                restarted=subprocess.run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'],env=base_env,capture_output=True,text=True,timeout=20)
                if restarted.returncode: raise RuntimeError('Owned outage recovery failed')
            checks['http_outage_does_not_consume_proof']=state(outage['subscriberId'])=='0:1' and request(data={'Token':outage['token'],'__RequestVerificationToken':csrf})[0]==200 and state(outage['subscriberId'])=='1:2'
            pending=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--verification-pending'],cwd=root,env=env,capture_output=True,text=True,timeout=30)
            if pending.returncode: raise RuntimeError('Pending worker fixture seed failed')
            time.sleep(2)
            # Worker is explicitly enabled, but missing provider settings prevent all real sends.
            checks['worker_enabled_missing_config_no_attempts']=sql('SELECT count(*) FROM "TransactionalEmailJobs" WHERE "State"=0')=='1' and sql('SELECT count(*) FROM "TransactionalEmailAttempts" WHERE "JobId" IN (SELECT "Id" FROM "TransactionalEmailJobs" WHERE "State"=0)')=='0'
        finally:
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
    log=app_log.read_text()
    checks['http_no_raw_proof_in_logs']=all(token not in log for token in raw_proofs)
    checks['http_model_binding_trace_forced_off']='Attempting to bind' not in log
