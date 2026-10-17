"""Real isolated MVC exit journey; all synthetic proofs and raw logs stay in a disposable owned directory."""
import http.cookiejar,json,os,re,shutil,signal,subprocess,time,urllib.error,urllib.parse,urllib.request
from pathlib import Path

def run_http_fixture(root,fixture,base_env,connection,pg_port,report_dir,checks,browser_module,browser_executable):
    from runtime_schema import ensure_runtime_schema
    ensure_runtime_schema(root,base_env,connection)
    base='http://127.0.0.1:15224';web=fixture/'web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot')
    proof_file=fixture/'exit-proof.json';log_file=fixture/'web.log';raw_proofs=[]
    password=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='synthetic-exit-password\n',cwd=root,env=base_env,capture_output=True,text=True,timeout=20)
    if password.returncode:raise RuntimeError('Synthetic hash failed')
    app_env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,
        'SITE_URL':base,'SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password.stdout.strip(),
        'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),
        'SUBSCRIPTIONS_ENABLED':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false',
        'Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
    seed_env={**base_env,'DB_CONNECTION_STRING':connection,'OUTBOX_TEST_KEYS':str(fixture/'keys'),'UNSUBSCRIPTION_TEST_PROOF_FILE':str(proof_file)}
    def seed():
        result=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--unsubscription-seed'],cwd=root,env=seed_env,capture_output=True,text=True,timeout=30)
        if result.returncode:raise RuntimeError('Synthetic exit seed failed')
        data=json.loads(proof_file.read_text());raw_proofs.append(data['token']);return data
    jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    def request(path='/abonelik/cik',data=None,method=None,raw=None,content_type=None):
        payload=raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
        req=urllib.request.Request(base+path,data=payload,method=method,headers={'Content-Type':content_type} if content_type else {})
        try:
            with client.open(req,timeout=20) as response:return response.status,{k.lower():v for k,v in response.headers.items()},response.read().decode()
        except urllib.error.HTTPError as error:return error.code,{k.lower():v for k,v in error.headers.items()},error.read().decode()
    def sql(query):
        return subprocess.run(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','unsubscription_http','-At','-v','ON_ERROR_STOP=1','-c',query],env=base_env,capture_output=True,text=True,check=True).stdout.strip()
    def state(identity):return sql('SELECT "Status" || \':\' || "Version" FROM "Subscribers" WHERE "Id"=\''+identity+'\'')
    def csrf(body):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
    with log_file.open('w') as output:
        process=None
        def stop():
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
        def start():
            nonlocal process
            stop()
            process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
            for _ in range(100):
                if process.poll() is not None:raise RuntimeError('Exit application did not start')
                try:
                    if request()[0]==200:return
                except OSError:time.sleep(.1)
            raise RuntimeError('Exit HTTP readiness timed out')
        try:
            start();first=seed();identity=first['subscriberId'];token=first['token']
            status,h,body=request('/abonelik/cik#t='+token);antiforgery=csrf(body)
            checks['http_get_and_head_do_not_exit']=status==200 and request(method='HEAD')[0]==200 and state(identity)=='1:2'
            checks['http_private_headers']=h.get('referrer-policy')=='no-referrer' and 'no-store' in h.get('cache-control','') and 'noindex' in h.get('x-robots-tag','')
            checks['http_csp_enforced']=bool(h.get('content-security-policy')) and "script-src 'self'" in h['content-security-policy'] and 'unsafe-eval' not in h['content-security-policy']
            checks['http_no_proof_or_address_in_html']=token not in body and 'value=""' in body and identity not in body and '@example.test' not in body
            checks['http_missing_csrf_rejected']=request(data={'Token':token})[0]==400 and state(identity)=='1:2'
            status,h,result=request(data={'Token':token,'__RequestVerificationToken':antiforgery,'Status':'1','SubscriberId':'00000000-0000-0000-0000-000000000000'})
            checks['http_post_exits_with_provider_worker_and_registration_disabled']=status==200 and state(identity)=='2:3'
            checks['http_no_token_echo_or_auth_cookie']=token not in result and all(not any(name in c.name for name in ('Admin','Reader','.AspNetCore.Cookies')) for c in jar)
            checks['http_replay_idempotent']=request(data={'Token':token,'__RequestVerificationToken':antiforgery})[0]==200 and state(identity)=='2:3'
            checks['http_forged_proof_and_oversized_proof_rejected']=request(data={'Token':'x'*100,'__RequestVerificationToken':antiforgery})[0]==400 and request(data={'Token':'x'*513,'__RequestVerificationToken':antiforgery})[0]==400
            checks['http_query_not_used_as_post_proof']=request('/abonelik/cik?Token='+('x'*100),{'__RequestVerificationToken':antiforgery})[0]==400
            checks['http_json_not_accepted']=request(raw=b'{}',content_type='application/json')[0] in (400,415)
            checks['http_body_limit_enforced']=request(raw=b'Token='+b'x'*5000,content_type='application/x-www-form-urlencoded')[0] in (400,413)
            start()
            checks['http_restart_keeps_exit_and_link_replay']=request(data={'Token':token,'__RequestVerificationToken':csrf(request()[2])})[0]==200 and state(identity)=='2:3'
            # Reapply and confirm at the same DB timestamps; only the period must invalidate old links.
            period_before=sql('SELECT "PeriodId" FROM "Subscribers" WHERE "Id"=\''+identity+'\'')
            sql('UPDATE "Subscribers" SET "Status"=1,"VerifiedUtc"="UnsubscribedUtc","Version"="Version"+2,"PeriodId"=gen_random_uuid() WHERE "Id"=\''+identity+'\'')
            checks['http_old_link_cannot_exit_new_period']=request(data={'Token':token,'__RequestVerificationToken':csrf(request()[2])})[0]==400 and state(identity)=='1:5' and period_before!=sql('SELECT "PeriodId" FROM "Subscribers" WHERE "Id"=\''+identity+'\'')
            statuses=[request()[0] for _ in range(22)]
            checks['http_bounded_rate_limit_real_429']=429 in statuses
            _,h,_=request();checks['http_rate_limit_private_headers']='no-store' in h.get('cache-control','') and h.get('referrer-policy')=='no-referrer' and bool(h.get('retry-after'))
            start()
            if browser_module:
                seed()
                browser_env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable) if browser_executable else '',
                    'UNSUBSCRIPTION_TEST_BASE':base,'UNSUBSCRIPTION_TEST_PROOF_FILE':str(proof_file),'UNSUBSCRIPTION_TEST_PG_PORT':str(pg_port),
                    'UNSUBSCRIPTION_TEST_REPORT':str(report_dir/'browser.json'),'UNSUBSCRIPTION_TEST_SCREENSHOT':str(report_dir/'exit-desktop.png'),'UNSUBSCRIPTION_TEST_MOBILE_SCREENSHOT':str(report_dir/'exit-mobile.png')}
                browser_process=subprocess.Popen(['node',str(root/'scripts/verification/subscriber_unsubscription_browser_probe.cjs')],cwd=root,env=browser_env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True)
                try:
                    browser_process.communicate(timeout=90)
                    checks['real_chrome_acceptance']=browser_process.returncode==0 and json.loads((report_dir/'browser.json').read_text())['passed']
                finally:
                    try:os.killpg(browser_process.pid,signal.SIGTERM)
                    except ProcessLookupError:pass
                    browser_process.wait(timeout=10)
                start()
            outage=seed();antiforgery=csrf(request()[2])
            stopped=subprocess.run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],env=base_env,capture_output=True,text=True,timeout=20)
            if stopped.returncode:raise RuntimeError('Owned outage setup failed')
            try:
                status,h,result=request(data={'Token':outage['token'],'__RequestVerificationToken':antiforgery})
                checks['http_database_outage_safe_503']=status==503 and outage['token'] not in result and 'SELECT' not in result and 'no-store' in h.get('cache-control','')
            finally:
                restarted=subprocess.run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'],env=base_env,capture_output=True,text=True,timeout=20)
                if restarted.returncode:raise RuntimeError('Owned outage recovery failed')
            checks['http_outage_preserves_retryable_exit']=state(outage['subscriberId'])=='1:2' and request(data={'Token':outage['token'],'__RequestVerificationToken':antiforgery})[0]==200 and state(outage['subscriberId'])=='2:3'
        finally:stop()
    log=log_file.read_text()
    checks['http_no_raw_proofs_or_addresses_in_logs']=all(token not in log for token in raw_proofs) and '@example.test' not in log
    checks['http_modelbinding_trace_forced_off']='Attempting to bind' not in log
