#!/usr/bin/env python3
"""Owned PostgreSQL migration and real HTTP exit acceptance; real simulator use is explicit and bounded."""
from runtime_schema import ensure_runtime_schema
import argparse,getpass,json,os,re,subprocess,tempfile
from pathlib import Path
from unsubscription_http_fixture import run_http_fixture
parser=argparse.ArgumentParser()
parser.add_argument('--report',type=Path,required=True)
parser.add_argument('--port',default='55524')
parser.add_argument('--env-file',type=Path)
parser.add_argument('--browser-module',type=Path)
parser.add_argument('--browser-executable',type=Path)
args=parser.parse_args();root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
checks={};version='unverified'
previous='20261014121355_SubscriptionRegistrationAdmission';current='20261014124542_SubscriberUnsubscriptionPeriod'
def run(command,extra=None,required=True,timeout=90):
    if extra and "DB_CONNECTION_STRING" in extra and any(str(x).endswith("ContentRulesTool.dll") for x in command) and not any("seed" in str(x) for x in command):
        ensure_runtime_schema(root,env,extra["DB_CONNECTION_STRING"])
    result=subprocess.run(command,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
    if required and result.returncode:raise RuntimeError('Fixture command failed: '+command[0]+' exit='+str(result.returncode))
    return result
def check(name,value):checks[name]=bool(value);print(name+'='+str(bool(value)).lower(),flush=True)
def safe_report(result,name):
    try:
        data=json.loads(result.stdout);valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
    except (ValueError,KeyError,TypeError):data={'passed':False,'diagnostic':'No complete safe report','exit_code':result.returncode};valid=False
    args.report.parent.mkdir(parents=True,exist_ok=True)
    (args.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
if run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode==0:raise SystemExit('Owned test port occupied.')
try:
    with tempfile.TemporaryDirectory(prefix='devcoreblog-unsubscription-',dir='/tmp') as owned:
        fixture=Path(owned);(fixture/'socket').mkdir();(fixture/'keys').mkdir(mode=0o700);started=False
        def connection(database):return f'Host=127.0.0.1;Port={args.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        def sql(database,query,required=True):return run(['psql','-X','-h','127.0.0.1','-p',args.port,'-d',database,'-At','-v','ON_ERROR_STOP=1','-c',query],required=required).stdout.strip()
        def migrate(database,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(database)},required=required)
        try:
            run(['initdb','-D',str(fixture/'pg'),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
            run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {args.port} -k {fixture}/socket','-w','start']);started=True
            version=sql('postgres','SHOW server_version')
            for database in ('unsubscription_empty','unsubscription_prior','unsubscription_http'):run(['createdb','-h','127.0.0.1','-p',args.port,database])
            check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},required=False).returncode==0)
            migrate('unsubscription_prior',previous)
            sql('unsubscription_prior', '''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version")
                VALUES ('00000000-0000-0000-0000-000000000001','preserved@example.test','preserved@example.test',0,0,'2026-01-01Z','2026-01-01Z',1),
                ('00000000-0000-0000-0000-000000000002','preserved2@example.test','preserved2@example.test',0,0,'2026-01-01Z','2026-01-01Z',1)''')
            before=sql('unsubscription_prior','SELECT to_jsonb(t)::text FROM "Subscribers" t ORDER BY "Id"')
            history=sql('unsubscription_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
            check('prior_schema_has_no_period',sql('unsubscription_prior',"SELECT count(*) FROM information_schema.columns WHERE table_name='Subscribers' AND column_name='PeriodId'")=='0')
            migrate('unsubscription_empty',current);migrate('unsubscription_empty',previous)
            check('empty_down_safe',sql('unsubscription_empty',"SELECT count(*) FROM information_schema.columns WHERE table_name='Subscribers' AND column_name='PeriodId'")=='0')
            for database in ('unsubscription_empty','unsubscription_prior'):
                migrate(database,current)
            check('all_old_subscriber_columns_preserved',before==sql('unsubscription_prior',"SELECT (to_jsonb(t)-'PeriodId')::text FROM \"Subscribers\" t ORDER BY \"Id\""))
            check('existing_rows_receive_distinct_nonempty_periods',sql('unsubscription_prior',"SELECT count(DISTINCT \"PeriodId\") FROM \"Subscribers\" WHERE \"PeriodId\"<>'00000000-0000-0000-0000-000000000000'")=='2')
            check('history_additive',sql('unsubscription_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
            rejected=sql('unsubscription_prior',"UPDATE \"Subscribers\" SET \"PeriodId\"='00000000-0000-0000-0000-000000000000'",False)
            check('empty_period_database_constraint',sql('unsubscription_prior',"SELECT count(*) FROM \"Subscribers\" WHERE \"PeriodId\"='00000000-0000-0000-0000-000000000000'")=='0')
            for database in ('unsubscription_empty','unsubscription_prior'):
                migrate(database,'20261014140624_ReaderIdentity')
                safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--unsubscription'],{'DB_CONNECTION_STRING':connection(database),'OUTBOX_TEST_KEYS':str(fixture/'keys')},required=False),database)
            periods=sql('unsubscription_prior','SELECT "Id" || \':\' || "PeriodId" FROM "Subscribers" ORDER BY 1')
            check('nonempty_down_refuses_period_loss',migrate('unsubscription_prior',previous,False).returncode!=0 and periods==sql('unsubscription_prior','SELECT "Id" || \':\' || "PeriodId" FROM "Subscribers" ORDER BY 1'))
            migrate('unsubscription_http','20261014140624_ReaderIdentity')
            run_http_fixture(root,fixture,env,connection('unsubscription_http'),args.port,args.report.parent,checks,args.browser_module,args.browser_executable)
            if args.env_file:
                if args.env_file.is_symlink() or args.env_file.stat().st_mode & 0o077:raise RuntimeError('Private credential file required')
                values=[line.split('=',1)[1].strip().strip('\"\'') for line in args.env_file.read_text().splitlines() if line.startswith('RESEND_API_KEY=')]
                if len(values)!=1 or not re.fullmatch(r're_[A-Za-z0-9_-]+',values[0]):raise RuntimeError('Valid local credential required')
                run(['createdb','-h','127.0.0.1','-p',args.port,'unsubscription_live']);migrate('unsubscription_live','20261014140624_ReaderIdentity')
                safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--unsubscription-live'],{'DB_CONNECTION_STRING':connection('unsubscription_live'),'OUTBOX_TEST_KEYS':str(fixture/'keys'),'RESEND_API_KEY':values[0]},required=False),'real_resend_exit')
        finally:
            if started:run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],required=False)
        check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode!=0)
except Exception as error:
    checks['fixture_completed']=False;print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
    args.report.parent.mkdir(parents=True,exist_ok=True)
    report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned real PostgreSQL/MVC. Optional Chrome and one fixed Resend simulator API call are separate evidence.'}
    args.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
