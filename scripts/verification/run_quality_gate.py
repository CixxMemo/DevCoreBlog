#!/usr/bin/env python3
"""Run a clean, disposable quality gate; never copy local credentials or export fixture logs."""
import argparse,json,os,signal,shutil,subprocess,tempfile,time
from pathlib import Path
from npm_audit_policy import evaluate_npm_audit

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--report-dir',type=Path,required=True)
parser.add_argument('--nginx',type=Path,help='Supported Nginx executable for real HTTPS campaign acceptance; otherwise discover nginx on PATH.')
parser.add_argument('--final-acceptance',action='store_true',help='Also run F57 security/data and F22 conflict regression on separate disposable clusters.')
args=parser.parse_args(); report=args.report_dir.resolve()
if report.exists() and any(report.iterdir()):
    raise SystemExit('Report directory must be empty; preserve prior evidence in another directory.')
report.mkdir(parents=True,exist_ok=True)
# Deliberately do not inherit application credentials, config overrides or fixture flags.
allowed=('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL','SystemRoot')
env={k:v for k,v in os.environ.items() if k in allowed}
env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',PYTHONDONTWRITEBYTECODE='1')
stages={}

def run(name,command,cwd,timeout=240,export=True,extra=None):
    started=time.monotonic()
    proc=subprocess.Popen(command,cwd=cwd,env={**env,**(extra or {})},stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,start_new_session=True)
    try:
        output,_=proc.communicate(timeout=timeout); code=proc.returncode
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid,signal.SIGTERM)
        try: output,_=proc.communicate(timeout=15)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid,signal.SIGKILL); output,_=proc.communicate()
        code=124
    stages[name]={'exit_code':code,'passed':code==0,'seconds':round(time.monotonic()-started,3)}
    if export: (report/(name+'.log')).write_text(output)
    print(name+': '+('PASS' if code==0 else 'FAIL'),flush=True)
    return code,output

def json_audit(name,command,source,kind):
    code,output=run(name,command,source,export=False)
    try:
        data=json.loads(output)
        if kind=='npm':
            policy=evaluate_npm_audit(data,code)
            valid=policy['response_valid']
            clean=policy['passed']
            stages[name]['audit_policy']=policy
        else:
            valid=bool(data.get('projects')) and not data.get('problems')
            clean=valid and not any(package.get('vulnerabilities') for project in data['projects'] for framework in project.get('frameworks',[]) for group in ('topLevelPackages','transitivePackages') for package in framework.get(group,[]))
        (report/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n')
        stages[name]['passed']=clean if kind=='npm' else code==0 and clean
        stages[name]['advisory_response_valid']=valid
    except (ValueError,KeyError,TypeError):
        stages[name]['passed']=False
        stages[name]['advisory_response_valid']=False
    label='PASS' if stages[name]['passed'] else 'FAIL/UNVERIFIED'
    print(name+': advisory '+label,flush=True)

try:
    for command in ('git','dotnet','node','npm','python3','rsync','initdb','pg_ctl','createdb','psql','pg_isready','rg','openssl'):
        if not shutil.which(command,path=env.get('PATH')): raise RuntimeError('Missing prerequisite: '+command)
    with tempfile.TemporaryDirectory(prefix='devcoreblog-f55-',dir='/tmp') as owned:
        source=Path(owned)/'source';source.mkdir()
        files=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT).decode().split('\0')
        for name in set(files):
            if not name: continue
            parts=Path(name).parts
            if any(p in ('bin','obj','node_modules','.git','__pycache__','.auth') or p.startswith('.env') or p.startswith('cookies') for p in parts) or name.startswith('wwwroot/generated/'): continue
            path=ROOT/name
            if path.is_symlink(): raise RuntimeError('Source symlink not supported: '+name)
            if path.is_file():
                dest=source/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest)
        # Use only the public registry; never read the host user's npm credentials.
        npm_global=Path(owned)/'npm-global.conf';npm_global.write_text('')
        env.update(NPM_CONFIG_USERCONFIG='/dev/null',NPM_CONFIG_GLOBALCONFIG=str(npm_global),NPM_CONFIG_REGISTRY='https://registry.npmjs.org/')
        run('sdk',['dotnet','--version'],source)
        if run('npm-install',['npm','ci','--ignore-scripts','--no-fund','--no-audit'],source)[0]: raise RuntimeError('Frontend install failed')
        json_audit('npm-audit',['npm','audit','--json'],source,'npm')
        if run('npm-audit-policy',['python3','scripts/verification/f55_audit_policy_probe.py','--audit',str(report/'npm-audit.json'),'--report',str(report/'npm-audit-policy.json')],source)[0]:
            raise RuntimeError('Strict npm audit policy regression failed')
        # Explicit public source: no user NuGet credential feeds are inherited.
        (source/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
        build=['dotnet','build','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false']
        if run('restore',['dotnet','restore','DevCoreBlog.csproj','--configfile','NuGet.Config','-p:NuGetAudit=false'],source)[0]: raise RuntimeError('Application restore failed')
        if run('build',build+['DevCoreBlog.csproj'],source)[0]: raise RuntimeError('Application build failed')
        if run('editor-distribution',['node','scripts/verification/editor_build_probe.mjs'],source)[0]: raise RuntimeError('Editor distribution integrity failed')
        if run('tiptap-distribution',['node','scripts/verification/tiptap_build_probe.mjs'],source)[0]: raise RuntimeError('Tiptap distribution integrity failed')
        if run('document-recovery-store',['node','scripts/verification/document_recovery_store_probe.mjs'],source)[0]: raise RuntimeError('Recovery race/failure checks failed')
        if run('site-dates',['sh','scripts/verification/run_site_dates_probe.sh',str(report/'site-dates')],source)[0]: raise RuntimeError('Turkish site date/UTC regression failed')
        if run('publish-boundary',['python3','scripts/verification/publish_boundary_probe.py','--source',str(source)],source)[0]: raise RuntimeError('Private files entered SDK items or publish output')
        for project in ('DevCoreBlog.csproj','DevCoreBlog.Core/DevCoreBlog.Core.csproj','DevCoreBlog.Data/DevCoreBlog.Data.csproj','DevCoreBlog.Services/DevCoreBlog.Services.csproj'):
            json_audit('nuget-'+Path(project).stem,['dotnet','package','list','--project',project,'--include-transitive','--vulnerable','--format','json','--no-restore'],source,'nuget')
        if run('tool-restore',['dotnet','tool','restore','--configfile','NuGet.Config'],source)[0]: raise RuntimeError('EF tool restore failed')
        if run('ef-version',['dotnet','ef','--version'],source)[0]: raise RuntimeError('EF tool unavailable')
        for tool in ('PasswordHash','ContentRules','ImageUploadPolicy','Operations','DocumentValidation','DocumentTransition','MediaInventory','EmailProvider'):
            project=f'tools/DevCoreBlog.{tool}Tool/DevCoreBlog.{tool}Tool.csproj'
            if run('restore-'+tool,['dotnet','restore',project,'--configfile','NuGet.Config','-p:NuGetAudit=false'],source)[0]: raise RuntimeError('Test tool restore failed')
            if run('build-'+tool,build+[project],source)[0]: raise RuntimeError('Test tool build failed')
            if tool in ('ImageUploadPolicy','Operations','DocumentValidation','EmailProvider'):
                if run('checks-'+tool,['dotnet',str(source/Path(project).parent/'bin/Debug/net10.0'/('DevCoreBlog.'+tool+'Tool.dll'))],source)[0]: raise RuntimeError('Tool checks failed')
        # Existing runner owns cluster, sockets, synthetic credentials and application cleanup.
        code,output=run('postgres-http',['sh','scripts/verification/run_f17_visibility.sh'],source,timeout=480,export=False,extra={'DEVCORE_F21_PROBE':'1','DEVCORE_F49_PROBE':'1','DEVCORE_F30_PROBE':'1','DEVCORE_F55_PROBE':'1','DEVCORE_F55_REPORT_DIR':str(report),'DEVCORE_VOL1_F01_REPORT_DIR':str(report),'DEVCORE_F17_PG_PORT':'55459','DEVCORE_F17_APP_PORT':'15196'})
        checks={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.endswith(('=true','=false'))}
        (report/'fixture-checks.json').write_text(json.dumps({'checks':checks,'exit_code':code},indent=2)+'\n')
        stages['postgres-http']['passed']=code==0 and bool(checks) and all(checks.values())
        if code: print('Fixture failed; private raw output withheld. See structured reports and rerun the isolated fixture for diagnosis.',flush=True)
        code,output=run('content-access',['sh','scripts/verification/run_f17_visibility.sh'],source,timeout=480,export=False,extra={
            'DEVCORE_VOL1_F04_REPORT_DIR':str(report),'DEVCORE_F17_PG_PORT':'55474','DEVCORE_F17_APP_PORT':'15204'})
        access_checks={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.startswith('f04_') and line.endswith(('=true','=false'))}
        (report/'content-access-fixture.json').write_text(json.dumps({'checks':access_checks,'exit_code':code},indent=2)+'\n')
        try:
            http_access=json.loads((report/'public-access.json').read_text())
            stages['content-access']['passed']=code==0 and bool(access_checks) and all(access_checks.values()) and bool(http_access['checks']) and all(http_access['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['content-access']['passed']=False
        code,output=run('document-persistence',['sh','scripts/verification/run_f17_visibility.sh'],source,timeout=480,export=False,extra={
            'DEVCORE_VOL1_F08_REPORT_DIR':str(report),'DEVCORE_VOL1_F33_REPORT_DIR':str(report),'DEVCORE_VOL1_F32_REPORT_DIR':str(report),'DEVCORE_F17_PG_PORT':'55478','DEVCORE_F17_APP_PORT':'15408'})
        document_checks={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.startswith('f08_') and line.endswith(('=true','=false'))}
        (report/'document-migration-fixture.json').write_text(json.dumps({'checks':document_checks,'exit_code':code},indent=2)+'\n')
        try:
            document_db=json.loads((report/'document-persistence.json').read_text())
            document_http=json.loads((report/'document-persistence-http.json').read_text())
            writing_http=json.loads((report/'document-writing-http.json').read_text())
            tables_http=json.loads((report/'document-tables-http.json').read_text())
            images_http=json.loads((report/'document-images-http.json').read_text())
            youtube_http=json.loads((report/'document-youtube-http.json').read_text())
            recovery_http=json.loads((report/'document-recovery-http.json').read_text())
            preview_http=json.loads((report/'document-preview-http.json').read_text())
            email_preview_http=json.loads((report/'document-email-preview-http.json').read_text())
            public_document_http=json.loads((report/'public-document-http.json').read_text())
            newsletter_http=json.loads((report/'newsletter-writing-http.json').read_text())
            teasers_http=json.loads((report/'newsletter-teasers-http.json').read_text())
            stages['document-persistence']['passed']=code==0 and len(document_checks)>=5 and all(document_checks.values()) and all(
                bool(data['checks']) and data['count']==len(data['checks']) and all(data['checks'].values()) for data in (document_db,document_http,writing_http,tables_http,images_http,youtube_http,recovery_http,preview_http,email_preview_http,public_document_http,newsletter_http,teasers_http))
        except (OSError,ValueError,KeyError,TypeError):
            stages['document-persistence']['passed']=False
        code,output=run('webhook-documents',['sh','scripts/verification/run_f17_visibility.sh'],source,timeout=600,export=False,extra={
            'DEVCORE_VOL1_F17_REPORT_DIR':str(report),'DEVCORE_F17_PG_PORT':'55492','DEVCORE_F17_APP_PORT':'15422'})
        webhook_checks={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.startswith('vol1_f17_') and line.endswith(('=true','=false'))}
        (report/'webhook-documents-fixture.json').write_text(json.dumps({'checks':webhook_checks,'exit_code':code},indent=2)+'\n')
        try:
            webhook_reports=[json.loads((report/name).read_text()) for name in ['webhook-document-http.json','webhook-document-restart.json','webhook-document-missing.json']]
            stages['webhook-documents']['passed']=code==0 and len(webhook_checks)>=6 and all(webhook_checks.values()) and all(
                data['passed'] and data['count']==len(data['checks']) and bool(data['checks']) and all(data['checks'].values()) for data in webhook_reports)
        except (OSError,ValueError,KeyError,TypeError):
            stages['webhook-documents']['passed']=False
        code,output=run('document-transition',['python3','scripts/verification/document_transition_probe.py','--port','55502','--report',str(report/'document-transition.json')],source,timeout=240,export=False)
        try:
            transition=json.loads((report/'document-transition.json').read_text())
            stages['document-transition']['passed']=code==0 and transition['passed'] and transition['count']==len(transition['checks']) and all(transition['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['document-transition']['passed']=False
        code,output=run('media-inventory',['python3','scripts/verification/media_inventory_probe.py','--port','55504','--report',str(report/'media-inventory.json')],source,timeout=180,export=False)
        try:
            media=json.loads((report/'media-inventory.json').read_text())
            stages['media-inventory']['passed']=code==0 and media['passed'] and media['count']==len(media['checks']) and all(media['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['media-inventory']['passed']=False
        code,output=run('subscriber-lifecycle',['python3','scripts/verification/subscriber_lifecycle_probe.py','--port','55520','--report',str(report/'subscriber-migration.json')],source,timeout=180,export=False)
        try:
            subscriber=json.loads((report/'subscriber-migration.json').read_text())
            stages['subscriber-lifecycle']['passed']=code==0 and subscriber['passed'] and subscriber['count']==len(subscriber['checks']) and all(subscriber['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['subscriber-lifecycle']['passed']=False
        code,output=run('transactional-email',['python3','scripts/verification/transactional_email_probe.py','--port','55521','--report',str(report/'outbox-migration.json')],source,timeout=180,export=False)
        try:
            outbox=json.loads((report/'outbox-migration.json').read_text())
            stages['transactional-email']['passed']=code==0 and outbox['passed'] and outbox['count']==len(outbox['checks']) and all(outbox['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['transactional-email']['passed']=False
        code,output=run('subscriber-verification',['python3','scripts/verification/subscriber_verification_probe.py','--port','55522','--report',str(report/'verification-migration.json')],source,timeout=180,export=False)
        try:
            verification=json.loads((report/'verification-migration.json').read_text())
            stages['subscriber-verification']['passed']=code==0 and verification['passed'] and verification['count']==len(verification['checks']) and all(verification['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['subscriber-verification']['passed']=False
        code,output=run('subscription-registration',['python3','scripts/verification/subscription_registration_probe.py','--port','55523','--report',str(report/'registration-migration.json')],source,timeout=240,export=False)
        try:
            registration=json.loads((report/'registration-migration.json').read_text())
            stages['subscription-registration']['passed']=code==0 and registration['passed'] and registration['count']==len(registration['checks']) and all(registration['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['subscription-registration']['passed']=False
        code,output=run('subscriber-unsubscription',['python3','scripts/verification/subscriber_unsubscription_probe.py','--port','55524','--report',str(report/'unsubscription-migration.json')],source,timeout=180,export=False)
        try:
            unsubscription=json.loads((report/'unsubscription-migration.json').read_text())
            stages['subscriber-unsubscription']['passed']=code==0 and unsubscription['passed'] and unsubscription['count']==len(unsubscription['checks']) and all(unsubscription['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['subscriber-unsubscription']['passed']=False
        code,output=run('newsletter-campaign',['python3','scripts/verification/newsletter_campaign_probe.py','--port','55525','--report',str(report/'campaign-migration.json')],source,timeout=180,export=False)
        try:
            campaign=json.loads((report/'campaign-migration.json').read_text())
            stages['newsletter-campaign']['passed']=code==0 and campaign['passed'] and campaign['count']==len(campaign['checks']) and all(campaign['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['newsletter-campaign']['passed']=False
        code,output=run('campaign-recipients',['python3','scripts/verification/campaign_recipients_probe.py','--port','55527','--report',str(report/'recipients-migration.json')],source,timeout=480,export=False)
        try:
            recipients=json.loads((report/'recipients-migration.json').read_text())
            stages['campaign-recipients']['passed']=code==0 and recipients['passed'] and recipients['count']==len(recipients['checks']) and all(recipients['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['campaign-recipients']['passed']=False
        code,output=run('provider-events',['python3','scripts/verification/provider_events_probe.py','--port','55528','--report',str(report/'provider-events.json')],source,timeout=480,export=False)
        try:
            events=json.loads((report/'provider-events.json').read_text())
            stages['provider-events']['passed']=code==0 and events['passed'] and events['count']==len(events['checks']) and all(events['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['provider-events']['passed']=False
        delivery_command=['python3','scripts/verification/campaign_delivery_probe.py','--port','55530','--report',str(report/'campaign-delivery.json')]
        if args.nginx is not None:delivery_command.extend(['--nginx',str(args.nginx.resolve())])
        code,output=run('campaign-delivery',delivery_command,source,timeout=480,export=False)
        try:
            delivery=json.loads((report/'campaign-delivery.json').read_text())
            stages['campaign-delivery']['passed']=code==0 and delivery['passed'] and delivery['count']==len(delivery['checks']) and all(delivery['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['campaign-delivery']['passed']=False
        code,output=run('test-email',['python3','scripts/verification/test_email_probe.py','--port','55533','--report',str(report/'test-email'/'report.json')],source,timeout=480,export=False)
        try:
            test_email=json.loads((report/'test-email'/'report.json').read_text())
            stages['test-email']['passed']=code==0 and test_email['passed'] and test_email['count']==len(test_email['checks']) and all(test_email['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['test-email']['passed']=False
        code,output=run('campaign-admin',['python3','scripts/verification/campaign_admin_probe.py','--port','55534','--report',str(report/'campaign-admin'/'report.json')],source,timeout=480,export=False)
        try:
            campaign_admin=json.loads((report/'campaign-admin'/'report.json').read_text())
            stages['campaign-admin']['passed']=code==0 and campaign_admin['passed'] and campaign_admin['count']==len(campaign_admin['checks']) and all(campaign_admin['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['campaign-admin']['passed']=False
        code,output=run('subscriber-admin',['python3','scripts/verification/subscriber_admin_probe.py','--port','55535','--report',str(report/'subscriber-admin'/'report.json')],source,timeout=480,export=False)
        try:
            subscriber_admin=json.loads((report/'subscriber-admin'/'report.json').read_text())
            stages['subscriber-admin']['passed']=code==0 and subscriber_admin['passed'] and subscriber_admin['count']==len(subscriber_admin['checks']) and all(subscriber_admin['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['subscriber-admin']['passed']=False
        code,output=run('reader-identity',['python3','scripts/verification/reader_identity_probe.py','--port','55526','--report',str(report/'reader-migration.json')],source,timeout=300,export=False)
        try:
            reader=json.loads((report/'reader-migration.json').read_text())
            stages['reader-identity']['passed']=code==0 and reader['passed'] and reader['count']==len(reader['checks']) and all(reader['checks'].values())
        except (OSError,ValueError,KeyError,TypeError):
            stages['reader-identity']['passed']=False
        if args.final_acceptance:
            code,output=run('final-security-data',['sh','scripts/verification/run_f17_visibility.sh'],source,timeout=480,export=False,extra={'DEVCORE_F49_PROBE':'1','DEVCORE_F57_PROBE':'1','DEVCORE_F57_REPORT':str(report/'final-regression.json'),'DEVCORE_F17_PG_PORT':'55461','DEVCORE_F17_APP_PORT':'15199'})
            try:
                acceptance=json.loads((report/'final-regression.json').read_text())
                stages['final-security-data']['passed']=code==0 and len(acceptance['stages'])==7 and bool(acceptance['checks']) and all(acceptance['checks'].values())
            except (OSError,ValueError,KeyError,TypeError):
                stages['final-security-data']['passed']=False
            code,output=run('final-edit-conflict',['sh','scripts/verification/run_f22_edit_conflict.sh'],source,timeout=480,export=False,extra={'DEVCORE_F22_PG_PORT':'55462','DEVCORE_F22_APP_PORT':'15200'})
            conflicts={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.startswith('f22_') and line.endswith(('=true','=false'))}
            (report/'final-edit-conflict.json').write_text(json.dumps({'checks':conflicts,'exit_code':code},indent=2)+'\n')
            stages['final-edit-conflict']['passed']=code==0 and len(conflicts)==8 and all(conflicts.values())
except Exception as error:
    stages['gate-error']={'passed':False,'error_type':type(error).__name__,'reason':str(error) if isinstance(error,RuntimeError) else 'See failed stage'}
finally:
    passed=bool(stages) and all(s['passed'] for s in stages.values())
    # Report origin separately from the sanitized environment passed to the fixture.
    remote = os.environ.get('GITHUB_ACTIONS') == 'true'
    origin = {'remote_workflow_executed':remote}
    if remote:
        origin.update(workflow_run_id=os.environ.get('GITHUB_RUN_ID'),
                      workflow_commit=os.environ.get('GITHUB_SHA'))
    (report/'summary.json').write_text(json.dumps({'passed':passed,'stages':stages,**origin},indent=2)+'\n')
    print('Quality gate: '+('PASS' if passed else 'FAIL'),flush=True)
raise SystemExit(0 if passed else 1)
