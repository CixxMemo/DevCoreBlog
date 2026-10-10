#!/usr/bin/env python3
"""Run a clean, disposable quality gate; never copy local credentials or export fixture logs."""
import argparse,json,os,signal,shutil,subprocess,tempfile,time
from pathlib import Path
from npm_audit_policy import evaluate_npm_audit

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--report-dir',type=Path,required=True)
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
            valid='metadata' in data and 'vulnerabilities' in data and not data.get('error')
            policy=evaluate_npm_audit(data,code,json.loads((source/'package-lock.json').read_text()),json.loads((source/'package.json').read_text()))
            clean=valid and policy['passed']
            stages[name]['exception_policy']=policy
        else:
            valid=bool(data.get('projects')) and not data.get('problems')
            clean=valid and not any(package.get('vulnerabilities') for project in data['projects'] for framework in project.get('frameworks',[]) for group in ('topLevelPackages','transitivePackages') for package in framework.get(group,[]))
        (report/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n')
        stages[name]['passed']=clean if kind=='npm' else code==0 and clean
        stages[name]['advisory_response_valid']=valid
    except (ValueError,KeyError,TypeError):
        stages[name]['passed']=False
        stages[name]['advisory_response_valid']=False
    label='PASS_WITH_ACCEPTED_BUILD_RISK' if stages[name].get('exception_policy',{}).get('accepted') and stages[name]['passed'] else ('PASS' if stages[name]['passed'] else 'FAIL/UNVERIFIED')
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
        run('sdk',['dotnet','--version'],source)
        if run('npm-install',['npm','ci','--ignore-scripts','--no-fund','--no-audit'],source)[0]: raise RuntimeError('Frontend install failed')
        json_audit('npm-audit',['npm','audit','--json'],source,'npm')
        # Explicit public source: no user NuGet credential feeds are inherited.
        (source/'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
        build=['dotnet','build','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false']
        if run('restore',['dotnet','restore','DevCoreBlog.csproj','--configfile','NuGet.Config','-p:NuGetAudit=false'],source)[0]: raise RuntimeError('Application restore failed')
        if run('build',build+['DevCoreBlog.csproj'],source)[0]: raise RuntimeError('Application build failed')
        if run('editor-distribution',['node','scripts/verification/editor_build_probe.mjs'],source)[0]: raise RuntimeError('Editor distribution integrity failed')
        if run('tiptap-distribution',['node','scripts/verification/tiptap_build_probe.mjs'],source)[0]: raise RuntimeError('Tiptap distribution integrity failed')
        if run('publish-boundary',['python3','scripts/verification/publish_boundary_probe.py','--source',str(source)],source)[0]: raise RuntimeError('Private files entered SDK items or publish output')
        for project in ('DevCoreBlog.csproj','DevCoreBlog.Core/DevCoreBlog.Core.csproj','DevCoreBlog.Data/DevCoreBlog.Data.csproj','DevCoreBlog.Services/DevCoreBlog.Services.csproj'):
            json_audit('nuget-'+Path(project).stem,['dotnet','package','list','--project',project,'--include-transitive','--vulnerable','--format','json','--no-restore'],source,'nuget')
        if run('tool-restore',['dotnet','tool','restore','--configfile','NuGet.Config'],source)[0]: raise RuntimeError('EF tool restore failed')
        if run('ef-version',['dotnet','ef','--version'],source)[0]: raise RuntimeError('EF tool unavailable')
        for tool in ('PasswordHash','ContentRules','ImageUploadPolicy','Operations','DocumentValidation'):
            project=f'tools/DevCoreBlog.{tool}Tool/DevCoreBlog.{tool}Tool.csproj'
            if run('restore-'+tool,['dotnet','restore',project,'--configfile','NuGet.Config','-p:NuGetAudit=false'],source)[0]: raise RuntimeError('Test tool restore failed')
            if run('build-'+tool,build+[project],source)[0]: raise RuntimeError('Test tool build failed')
            if tool in ('ImageUploadPolicy','Operations','DocumentValidation'):
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
            'DEVCORE_VOL1_F08_REPORT_DIR':str(report),'DEVCORE_F17_PG_PORT':'55478','DEVCORE_F17_APP_PORT':'15408'})
        document_checks={line.split('=')[0]:line.endswith('=true') for line in output.splitlines() if line.startswith('f08_') and line.endswith(('=true','=false'))}
        (report/'document-migration-fixture.json').write_text(json.dumps({'checks':document_checks,'exit_code':code},indent=2)+'\n')
        try:
            document_db=json.loads((report/'document-persistence.json').read_text())
            document_http=json.loads((report/'document-persistence-http.json').read_text())
            writing_http=json.loads((report/'document-writing-http.json').read_text())
            tables_http=json.loads((report/'document-tables-http.json').read_text())
            images_http=json.loads((report/'document-images-http.json').read_text())
            stages['document-persistence']['passed']=code==0 and len(document_checks)>=5 and all(document_checks.values()) and all(
                bool(data['checks']) and data['count']==len(data['checks']) and all(data['checks'].values()) for data in (document_db,document_http,writing_http,tables_http,images_http))
        except (OSError,ValueError,KeyError,TypeError):
            stages['document-persistence']['passed']=False
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
