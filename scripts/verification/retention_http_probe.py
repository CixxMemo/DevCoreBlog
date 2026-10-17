"""Real worker host acceptance on one owned PostgreSQL database; no sender or real dotenv."""
import json,shutil,subprocess,time,urllib.request,urllib.error

def verify_worker(root,fixture,env,connection,sql,check):
    web=fixture/'worker-web';web.mkdir();shutil.copytree(root/'wwwroot',web/'wwwroot');keys=fixture/'worker-keys';keys.mkdir(mode=0o700)
    hashed=subprocess.run(['dotnet',str(root/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],cwd=web,env=env,input='synthetic-retention-password\n',text=True,capture_output=True,timeout=20)
    if hashed.returncode:raise RuntimeError('Owned retention credential could not be generated.')
    base='http://127.0.0.1:15240'
    app={**env,'DB_CONNECTION_STRING':connection,'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'SITE_URL':base,
        'ADMIN_USERNAME':'fixture-admin','ADMIN_PASSWORD_HASH':hashed.stdout.strip(),'ADMIN_LOGIN_PATH':'/fixture-admin/login','DATA_PROTECTION_KEYS_PATH':str(keys),
        'TRANSACTIONAL_EMAIL_WORKER_ENABLED':'false','NEWSLETTER_WORKER_ENABLED':'false','NEWSLETTER_DELIVERY_READY':'false','TEST_EMAIL_ENABLED':'false','RESEND_WEBHOOK_ENABLED':'false',
        'SUBSCRIPTIONS_ENABLED':'false','READER_LOGIN_ENABLED':'false','Logging__LogLevel__Default':'Information'}
    address='worker-private-retention@example.test'
    def seed():sql('retention_http',f'''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version") VALUES (gen_random_uuid(),'{address}','{address}',0,0,'2020-01-01Z','2020-01-01Z',1)''')
    def count():return sql('retention_http','SELECT count(*) FROM "Subscribers"')
    logs=[]
    def host(name,settings,expected=None,reject=False):
        log=fixture/(name+'.log');output=log.open('w');logs.append(log)
        process=subprocess.Popen(['dotnet',str(root/'bin/Debug/net10.0/DevCoreBlog.dll'),'--contentRoot',str(web),'--urls',base],cwd=web,env={**app,**settings},stdout=output,stderr=subprocess.STDOUT)
        try:
            if reject:
                process.wait(timeout=20);return process.returncode!=0
            ready=False
            for _ in range(150):
                if process.poll() is not None:raise RuntimeError('Owned retention host stopped unexpectedly.')
                try:
                    with urllib.request.urlopen(base+'/fixture-admin/login',timeout=2) as response:ready=response.status==200
                except (OSError,urllib.error.HTTPError):pass
                if ready and (expected is None or expected in log.read_text()):break
                time.sleep(.1)
            if not ready or (expected and expected not in log.read_text()):raise RuntimeError('Retention worker observation timed out.')
            if expected is None:time.sleep(.5)
            return True
        finally:
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
            output.close()
    seed()
    check('http_worker_default_closed',host('worker-default',{}) and count()=='1' and 'Retention dryRun=' not in logs[-1].read_text())
    check('http_worker_inspect_only_does_not_delete',host('worker-preview',{'RETENTION_ENABLED':'true','RETENTION_APPLY_ENABLED':'false'},'Retention dryRun=True') and count()=='1')
    check('http_worker_apply_requires_enabled',host('worker-invalid',{'RETENTION_ENABLED':'false','RETENTION_APPLY_ENABLED':'true'},reject=True) and count()=='1')
    check('http_worker_real_apply_removes_only_eligible_orphan',host('worker-apply',{'RETENTION_ENABLED':'true','RETENTION_APPLY_ENABLED':'true'},'Retention dryRun=False') and count()=='0')
    seed();sql('retention_http', '''UPDATE "RetentionCheckpoints" SET "LastAppliedUtc"=now()+interval '1 year' WHERE "Id"=1''')
    check('http_worker_restart_backward_clock_closes_without_fake_counts',host('worker-clock',{'RETENTION_ENABLED':'true','RETENTION_APPLY_ENABLED':'true'},'Retention clock moved backwards;')
        and count()=='1' and 'Retention dryRun=' not in logs[-1].read_text())
    combined=''.join(p.read_text() for p in logs)
    check('http_worker_logs_no_address_or_sql',address not in combined and 'SELECT ' not in combined and 'UPDATE ' not in combined)
