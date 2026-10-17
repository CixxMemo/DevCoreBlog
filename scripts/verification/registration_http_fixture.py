"""Real MVC/worker fixture in an owned source copy. Mailbox transport is simulated unless fixed-simulator live mode is selected."""
import http.cookiejar,json,os,re,shutil,subprocess,time,urllib.error,urllib.parse,urllib.request
from pathlib import Path

def run_registration_http_fixture(root,fixture,base_env,connection,pg_port,report_dir,checks,browser_module,browser_executable,env_file):
    from runtime_schema import ensure_runtime_schema
    ensure_runtime_schema(root,base_env,connection)
    source=fixture/'source';source.mkdir();mailbox=fixture/'mailbox.jsonl';app_log=fixture/'app.log'
    port=15223;base=f'http://127.0.0.1:{port}'
    def run(command,extra=None,timeout=90):
        result=subprocess.run(command,cwd=source,env={**base_env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
        if result.returncode:raise RuntimeError('Owned HTTP fixture command failed: '+command[0]+' exit='+str(result.returncode))
        return result
    excludes=['.local','.git','.codex','.agents','.auth','docs','AGENTS.md','.env*','node_modules','bin','obj','cookies*','__pycache__']
    run(['rsync','-a',*[arg for name in excludes for arg in ('--exclude',name)],str(root)+'/',str(source)+'/'])
    for project in ('.','DevCoreBlog.Core','DevCoreBlog.Data','DevCoreBlog.Services'):
        if (root/project/'obj').exists():shutil.copytree(root/project/'obj',source/project/'obj',dirs_exist_ok=True)
    program=source/'Program.cs';text=program.read_text();anchor='var app = builder.Build();'
    if text.count(anchor)!=1:raise RuntimeError('Owned mailbox registration anchor changed')
    program.write_text(text.replace(anchor,'builder.Services.AddSingleton<DevCoreBlog.Core.Email.IEmailSender, RegistrationFixtureMailboxSender>();\n'+anchor))
    (source/'RegistrationFixtureMailboxSender.cs').write_text('''using System.Text.Json;
using System.Text.RegularExpressions;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Services.Email;

// Owned disposable fixture only. Live mode permits exactly the provider's fixed simulator recipient.
internal sealed class RegistrationFixtureMailboxSender(ResendOptions options, TimeProvider clock) : IEmailSender, IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(5) }) { Timeout=Timeout.InfiniteTimeSpan };
    public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken=default)
    {
        var live=Environment.GetEnvironmentVariable("REGISTRATION_TEST_LIVE")=="true";
        if(live && request.Recipient!="delivered@resend.dev")return new(EmailSendOutcome.Rejected,EmailSendReason.Validation);
        var result=live ? await new ResendEmailSender(client,options,clock).SendAsync(request,cancellationToken)
            : new EmailSendResult(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200);
        if(result.Outcome==EmailSendOutcome.Accepted)
        {
            var path=Environment.GetEnvironmentVariable("REGISTRATION_TEST_MAILBOX")??throw new InvalidOperationException();
            var token=Regex.Match(request.Text,@"#t=([A-Za-z0-9_-]{43})").Groups[1].Value;
            await File.AppendAllTextAsync(path,JsonSerializer.Serialize(new { recipient=request.Recipient, token, live })+"\\n",cancellationToken);
        }
        return result;
    }
    public void Dispose()=>client.Dispose();
}
''')
    run(['dotnet','build','DevCoreBlog.csproj','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false'],timeout=120)
    password=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],
        input='synthetic-registration-password\n',cwd=root,env=base_env,capture_output=True,text=True,timeout=20)
    if password.returncode:raise RuntimeError('Synthetic hash tool failed')
    env={**base_env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,
         'SITE_URL':base,'SITE_TIME_ZONE':'Europe/Istanbul','ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':password.stdout.strip(),
         'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(fixture/'keys'),
         'SUBSCRIPTIONS_ENABLED':'true','TRANSACTIONAL_EMAIL_WORKER_ENABLED':'true','RESEND_API_KEY':'re_synthetic',
         'RESEND_FROM_EMAIL':'sender@example.test','RESEND_FROM_NAME':'Synthetic','REGISTRATION_TEST_MAILBOX':str(mailbox),
         'Logging__LogLevel__Default':'Information','Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding':'Trace'}
    jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar));process=None
    addresses=[];proofs=[]
    def request(path='/abone-ol',data=None,method=None,extra_headers=None):
        req=urllib.request.Request(base+path,data=urllib.parse.urlencode(data).encode() if data is not None else None,method=method,headers=extra_headers or {})
        try:
            with client.open(req,timeout=20) as response:return response.status,{k.lower():v for k,v in response.headers.items()},response.read().decode()
        except urllib.error.HTTPError as error:return error.code,{k.lower():v for k,v in error.headers.items()},error.read().decode()
    def sql(query):
        result=subprocess.run(['psql','-X','-h','127.0.0.1','-p',str(pg_port),'-d','registration_http','-At','-v','ON_ERROR_STOP=1','-c',query],env=base_env,capture_output=True,text=True)
        if result.returncode:raise RuntimeError('Owned SQL assertion failed')
        return result.stdout.strip()
    def literal(value):return "'"+value.replace("'","''")+"'"
    def state(email):return sql('SELECT "Status" || \':\' || "Version" FROM "Subscribers" WHERE "AddressKey"='+literal(email.lower()))
    def stop():
        nonlocal process
        if process and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=10)
            except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
        process=None
    def start(extra=None):
        nonlocal process
        stop()
        process=subprocess.Popen(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll'),'--urls',base],cwd=source,env={**env,**(extra or {})},stdout=output,stderr=subprocess.STDOUT)
        for _ in range(100):
            if process.poll() is not None:raise RuntimeError('Registration application did not start')
            try:
                if request()[0]==200:return
            except OSError:time.sleep(.1)
        raise RuntimeError('Registration readiness timed out')
    def csrf(path='/abone-ol'):
        status,headers,body=request(path)
        match=re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body)
        if status!=200 or not match:raise RuntimeError('Antiforgery form unavailable')
        return match.group(1),headers,body
    def submit(email,anti=None,extra=None):
        addresses.append(email)
        if anti is None:anti=csrf()[0]
        return request(data={'Email':email,'__RequestVerificationToken':anti,**(extra or {})})
    def mail(email):
        for _ in range(120):
            rows=[json.loads(line) for line in mailbox.read_text().splitlines()] if mailbox.exists() else []
            found=[row for row in rows if row['recipient'].lower()==email.lower()]
            if found:
                proofs.append(found[-1]['token']);return found[-1]
            time.sleep(.1)
        raise RuntimeError('Owned worker mailbox timed out')
    def private(headers):return 'no-store' in headers.get('cache-control','') and headers.get('referrer-policy')=='no-referrer' and 'noindex' in headers.get('x-robots-tag','')
    def confirm(email,row):
        before=state(email);anti,_,_=csrf('/abonelik/dogrula#t='+row['token'])
        safe=state(email)==before
        status,headers,body=request('/abonelik/dogrula',{'Token':row['token'],'__RequestVerificationToken':anti})
        return safe and status==200 and private(headers) and state(email).startswith('1:')
    with app_log.open('w') as output:
        try:
            for name,extra in (('switch_off',{'SUBSCRIPTIONS_ENABLED':'false'}),('sender_missing',{'RESEND_FROM_EMAIL':''}),('worker_off',{'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false'})):
                start(extra);anti,h,body=csrf();status,_,_=submit(name+'@example.test',anti)
                checks['http_'+name+'_closed_without_identity']=status==503 and 'şu anda kapalı' in body and 'disabled' in body and state(name+'@example.test')==''
            start();anti,h,body=csrf()
            checks['http_enabled_accessible_private_form']=private(h) and 'E-posta adresiniz' in body and 'name="Email"' in body and 'required' in body
            checks['http_registration_csp_enforced']="script-src 'self'" in h.get('content-security-policy','') and 'unsafe-eval' not in h.get('content-security-policy','')
            email='first@example.test';status,_,generic=submit(email,anti);row=mail(email)
            checks['http_post_queues_without_activating']=status==200 and state(email)=='0:1' and email not in generic
            checks['http_real_worker_journals_acceptance']=sql('SELECT count(*) FROM "TransactionalEmailAttempts" WHERE "Outcome"=0')=='1'
            checks['http_request_confirmation_chain']=confirm(email,row)
            checks['http_active_response_indistinguishable']=submit(email,anti)[2]==generic
            other='pending@example.test';checks['http_pending_response_indistinguishable']=submit(other,anti)[2]==generic;mail(other)
            checks['http_duplicate_pending_response_indistinguishable']=submit(other,anti)[2]==generic
            sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version","UnsubscribedUtc") VALUES
                ('00000000-0000-0000-0000-000000000004','suppressed@example.test','suppressed@example.test',0,4,'2026-01-01Z','2026-01-01Z',1,NULL),
                ('00000000-0000-0000-0000-000000000005','exited@example.test','exited@example.test',2,0,'2026-01-01Z','2026-01-01Z',2,'2026-01-01Z')''')
            checks['http_suppressed_response_indistinguishable']=submit('suppressed@example.test',anti)[2]==generic
            denied,h,_=submit('blocked-ip@example.test',anti)
            checks['http_ip_limit_real_429_and_private_headers']=denied==429 and private(h)
            spoofed=request(data={'Email':'spoof@example.test','__RequestVerificationToken':anti},extra_headers={'X-Forwarded-For':'203.0.113.10'})
            checks['http_forwarded_ip_spoof_does_not_bypass_limit']=spoofed[0]==429
            start();anti,_,_=csrf()
            checks['http_restart_duplicate_does_not_create_mail']=submit(other,anti)[2]==generic and sql('SELECT count(*) FROM "TransactionalEmailJobs"')=='2'
            checks['http_reapplication_pending_only']=submit('exited@example.test',anti)[0]==200 and state('exited@example.test')=='0:3';mail('exited@example.test')
            checks['http_invalid_single_mailbox_400']=submit('Name <invalid@example.test>',anti)[0]==400
            checks['http_overposting_cannot_activate']=submit('overpost@example.test',anti,{'Status':'1','Id':'00000000-0000-0000-0000-000000000004'})[0]==200 and state('overpost@example.test')=='0:1';mail('overpost@example.test')
            checks['http_missing_csrf_400']=request(data={'Email':'no-csrf@example.test'})[0]==400 and state('no-csrf@example.test')==''
            start();anti,_,_=csrf();outage_email='outage@example.test'
            before=sql('SELECT "Count" FROM "SubscriptionRegistrationBudgets" WHERE "Id"=1')
            run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'])
            try:
                outage_status,outage_headers,outage_body=submit(outage_email,anti)
                checks['http_real_db_outage_safe_503']=outage_status==503 and private(outage_headers) and outage_email not in outage_body and 'SELECT' not in outage_body
            finally:run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {pg_port} -k {fixture}/socket','-w','start'])
            checks['http_failed_request_has_no_identity_or_budget_charge']=state(outage_email)=='' and sql('SELECT "Count" FROM "SubscriptionRegistrationBudgets" WHERE "Id"=1')==before
            checks['http_outage_recovery_queues_normally']=submit(outage_email,anti)[0]==200 and state(outage_email)=='0:1';mail(outage_email)
            if browser_module:
                start()
                result=subprocess.run(['node',str(root/'scripts/verification/subscription_registration_browser_probe.cjs')],cwd=root,
                    env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable) if browser_executable else '',
                         'REGISTRATION_TEST_BASE':base,'REGISTRATION_TEST_MAILBOX':str(mailbox),'REGISTRATION_TEST_PG_PORT':str(pg_port),
                         'REGISTRATION_TEST_REPORT':str(report_dir/'browser.json'),'REGISTRATION_TEST_SCREENSHOT_DIR':str(report_dir)},capture_output=True,text=True,timeout=90)
                checks['real_chrome_registration_chain']=result.returncode==0 and json.loads((report_dir/'browser.json').read_text())['passed']
                if mailbox.exists():proofs.extend(json.loads(line)['token'] for line in mailbox.read_text().splitlines())
                addresses.extend(['browser@example.test','nojs@example.test'])
            if env_file:
                if env_file.is_symlink() or env_file.stat().st_mode&0o077:raise RuntimeError('Private credential file required')
                values=[line.split('=',1)[1].strip().strip('\"\'') for line in env_file.read_text().splitlines() if line.startswith('RESEND_API_KEY=')]
                if len(values)!=1 or not re.fullmatch(r're_[A-Za-z0-9_-]+',values[0]):raise RuntimeError('Valid local credential required')
                start({'RESEND_API_KEY':values[0],'RESEND_FROM_EMAIL':'onboarding@resend.dev','RESEND_FROM_NAME':'Aether Notes Test','REGISTRATION_TEST_LIVE':'true'})
                email='delivered@resend.dev';status,_,_=submit(email);row=mail(email)
                checks['real_resend_http_worker_accepts_fixed_simulator']=status==200 and row['live'] is True and state(email)=='0:1'
                checks['real_resend_submitted_proof_confirms_once']=confirm(email,row)
            # Global limiter runs before IP buckets; exhausted global Retry-After differs from the 10-minute IP window.
            start();responses=[request(data={'Email':'rate@example.test'}) for _ in range(70)]
            checks['http_global_sixty_per_minute_before_ip_partitions']=sum(status==400 for status,_,_ in responses)==5 and all(status==429 for status,_,_ in responses[5:])
            checks['http_global_quota_has_own_retry_after']=all(int(h.get('retry-after','9999'))<=60 for _,h,_ in responses[60:])
            checks['http_rate_tests_do_not_write']=state('rate@example.test')==''
            checks['http_no_auth_cookie']=all('Admin' not in cookie.name and 'Reader' not in cookie.name and cookie.name!='.AspNetCore.Cookies' for cookie in jar)
        finally:stop()
    text=app_log.read_text()
    checks['http_addresses_and_proofs_absent_from_logs']=all(address not in text for address in addresses) and all(proof not in text for proof in proofs)
    checks['http_model_binding_trace_stays_off']='Attempting to bind' not in text
