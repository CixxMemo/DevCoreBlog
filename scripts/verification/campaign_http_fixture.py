"""Actual MVC boundary for protected source deletion; only synthetic IDs/cookies in owned memory."""
import http.cookiejar,json,re,shutil,subprocess,time,urllib.error,urllib.parse,urllib.request

def run_http_fixture(root,fixture,base_env,connection,pg_port,checks):
    base='http://127.0.0.1:15225';web=fixture/'web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot')
    seed_file=fixture/'seed.json'
    seeded=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--campaign-seed'],cwd=root,env={**base_env,'DB_CONNECTION_STRING':connection,'CAMPAIGN_TEST_SEED_FILE':str(seed_file)},capture_output=True,text=True,timeout=30)
    if seeded.returncode:raise RuntimeError('Campaign HTTP seed failed')
    seed=json.loads(seed_file.read_text())
    password=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input='synthetic-campaign-password\n',cwd=root,env=base_env,capture_output=True,text=True,timeout=20)
    if password.returncode:raise RuntimeError('Synthetic hash failed')
    app_env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':base,'SITE_TIME_ZONE':'Europe/Istanbul',
        'ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password.stdout.strip(),'ADMIN_LOGIN_PATH':'/fixture-admin/login',
        'DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),'SUBSCRIPTIONS_ENABLED':'false','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false'}
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self,request,fp,code,message,headers,new_url):return None
    jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(NoRedirect(),urllib.request.HTTPCookieProcessor(jar))
    def request(path,data=None,method=None):
        req=urllib.request.Request(base+path,data=urllib.parse.urlencode(data).encode() if data is not None else None,method=method)
        try:
            with client.open(req,timeout=20) as response:return response.status,dict(response.headers),response.read().decode()
        except urllib.error.HTTPError as error:return error.code,dict(error.headers),error.read().decode()
    def sql(query):return subprocess.run(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','campaign_http','-At','-v','ON_ERROR_STOP=1','-c',query],env=base_env,capture_output=True,text=True,check=True).stdout.strip()
    def csrf(body):return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body).group(1)
    def present(identity):return sql('SELECT count(*) FROM "Posts" WHERE "Id"='+str(int(identity)))=='1'
    def campaign():return sql('SELECT "SnapshotHash" || \':\' || "State" || \':\' || "Version" FROM "NewsletterCampaigns"')
    path='/AdminPost/Delete/'+str(seed['postId']);before=campaign()
    with (fixture/'http.log').open('w') as output:
        process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env=app_env,stdout=output,stderr=subprocess.STDOUT)
        try:
            for _ in range(100):
                if process.poll() is not None:raise RuntimeError('Campaign HTTP application did not start')
                try:
                    if request('/fixture-admin/login')[0]==200:break
                except OSError:time.sleep(.1)
            else:raise RuntimeError('Campaign HTTP readiness timed out')
            _,_,anonymous_login=request('/fixture-admin/login')
            anonymous_status,anonymous_headers,_=request(path,{'__RequestVerificationToken':csrf(anonymous_login)})
            anonymous_headers={key.lower():value for key,value in anonymous_headers.items()}
            # The actual admin challenge deliberately hides its route with 404, including valid anonymous CSRF.
            checks['http_anonymous_cannot_delete_campaign_source']=anonymous_status==404 and 'location' not in anonymous_headers \
                and 'no-store' in anonymous_headers.get('cache-control','') and present(seed['postId'])
            _,_,login=request('/fixture-admin/login');status,_,_=request('/fixture-admin/login',{'username':'fixture-admin','password':'synthetic-campaign-password','__RequestVerificationToken':csrf(login)})
            if status!=302:raise RuntimeError('Synthetic sign-in failed')
            _,_,page=request('/AdminPost/Create');anti=csrf(page)
            checks['http_get_cannot_delete']=request(path)[0] in (404,405) and present(seed['postId'])
            checks['http_delete_requires_csrf']=request(path,{})[0]==400 and present(seed['postId'])
            status,headers,body=request(path,{'__RequestVerificationToken':anti})
            checks['http_campaign_source_returns_honest_409']=status==409 and present(seed['postId']) and campaign()==before
            checks['http_conflict_response_not_cached']='no-store' in {k.lower():v for k,v in headers.items()}.get('cache-control','')
            checks['http_conflict_has_no_snapshot_sql_or_credentials']=all(value not in body for value in ('Sentetik konu','Sentetik bülten','sender@example.test','re_synthetic','SELECT','23503'))
            checks['http_ordinary_post_can_be_deleted']=request('/AdminPost/Delete/'+str(seed['ordinaryId']),{'__RequestVerificationToken':anti})[0]==302 and not present(seed['ordinaryId'])
            checks['http_missing_delete_returns_404']=request('/AdminPost/Delete/'+str(seed['ordinaryId']),{'__RequestVerificationToken':anti})[0]==404
            checks['http_campaign_preparation_creates_no_email_work']=sql('SELECT count(*) FROM "TransactionalEmailJobs"')=='0' and sql('SELECT count(*) FROM "TransactionalEmailAttempts"')=='0'
        finally:
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
    log=(fixture/'http.log').read_text()
    checks['http_no_snapshot_or_private_sender_in_logs']=all(value not in log for value in ('Sentetik bülten','sender@example.test','re_synthetic_campaign','Retained synthetic legacy'))
