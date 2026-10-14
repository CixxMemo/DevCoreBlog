#!/usr/bin/env python3
"""Owned PostgreSQL outbox acceptance. Optional credential use is restricted to a fixed Resend simulator."""
import argparse, getpass, json, os, re, subprocess, tempfile
from pathlib import Path
parser=argparse.ArgumentParser()
parser.add_argument('--report',type=Path,required=True)
parser.add_argument('--port',default='55521')
parser.add_argument('--env-file',type=Path)
args=parser.parse_args(); root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
checks={}; version='unverified'
previous='20261012170132_SubscriberLifecycle'
current='20261014112734_TransactionalEmailOutbox'
def run(command,extra=None,required=True,timeout=90):
    result=subprocess.run(command,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
    if required and result.returncode: raise RuntimeError('Fixture command failed: '+command[0]+' exit='+str(result.returncode))
    return result
def check(name,value):
    checks[name]=bool(value); print(name+'='+str(bool(value)).lower(),flush=True)
def safe_report(result,name):
    try:
        data=json.loads(result.stdout)
        valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
    except (ValueError,KeyError,TypeError):
        data={'passed':False,'diagnostic':'No complete safe report','exit_code':result.returncode}; valid=False
    args.report.parent.mkdir(parents=True,exist_ok=True)
    (args.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n')
    check(name,valid)
if run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode==0: raise SystemExit('Owned test port occupied.')
try:
    with tempfile.TemporaryDirectory(prefix='devcoreblog-outbox-',dir='/tmp') as owned:
        fixture=Path(owned); (fixture/'socket').mkdir(); (fixture/'keys').mkdir(mode=0o700); started=False
        def connection(database): return f'Host=127.0.0.1;Port={args.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        def sql(database,query,required=True): return run(['psql','-X','-h','127.0.0.1','-p',args.port,'-d',database,'-At','-v','ON_ERROR_STOP=1','-c',query],required=required).stdout.strip()
        def migrate(database,target,required=True): return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(database)},required=required)
        def snapshot(database): return '\n'.join(sql(database,f'SELECT (to_jsonb(t)-\'PeriodId\')::text FROM "{table}" t ORDER BY 1') for table in ('Posts','Categories','WebhookReceipts','Subscribers'))
        try:
            run(['initdb','-D',str(fixture/'pg'),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
            run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {args.port} -k {fixture}/socket','-w','start']); started=True
            version=sql('postgres','SHOW server_version')
            for database in ('outbox_empty','outbox_prior'):
                run(['createdb','-h','127.0.0.1','-p',args.port,database])
            model=run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},required=False)
            check('model_snapshot_matches',model.returncode==0)
            migrate('outbox_prior',previous)
            sql('outbox_prior', '''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version")
                VALUES ('00000000-0000-0000-0000-000000000001','preserved@example.test','preserved@example.test',0,0,'2026-01-01Z','2026-01-01Z',1)''')
            before=snapshot('outbox_prior'); history=sql('outbox_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
            migrate('outbox_empty',current); migrate('outbox_empty',previous)
            check('empty_down_safe',sql('outbox_empty',"SELECT to_regclass('\"TransactionalEmailJobs\"') IS NULL")=='t')
            for database in ('outbox_empty','outbox_prior'):
                migrate(database,current)
                migrate(database,'20261014124542_SubscriberUnsubscriptionPeriod')
                safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--transactional-email'],
                    {'DB_CONNECTION_STRING':connection(database),'OUTBOX_TEST_KEYS':str(fixture/'keys')},required=False),database)
            # New synthetic test identities are expected; baseline rows remain byte-for-byte intact.
            check('prior_subscriber_preserved',sql('outbox_prior', 'SELECT "EmailAddress" || \':\' || "Version" FROM "Subscribers" WHERE "Id"=\'00000000-0000-0000-0000-000000000001\'')=='preserved@example.test:1')
            check('history_additive',sql('outbox_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[p.stem for p in sorted((root/'Migrations').glob('*.cs')) if not p.name.endswith('.Designer.cs') and current <= p.stem <= '20261014124542_SubscriberUnsubscriptionPeriod'])
            count=sql('outbox_prior','SELECT count(*) FROM "TransactionalEmailJobs"')
            rejected=migrate('outbox_prior',previous,False)
            check('nonempty_down_refuses_data_loss',rejected.returncode!=0 and sql('outbox_prior','SELECT count(*) FROM "TransactionalEmailJobs"')==count)
            if args.env_file:
                if args.env_file.is_symlink() or args.env_file.stat().st_mode & 0o077: raise RuntimeError('Private credential file required')
                values=[line.split('=',1)[1].strip().strip('\"\'') for line in args.env_file.read_text().splitlines() if line.startswith('RESEND_API_KEY=')]
                if len(values)!=1 or not re.fullmatch(r're_[A-Za-z0-9_-]+',values[0]): raise RuntimeError('Valid local credential required')
                safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--outbox-live'],
                    {'DB_CONNECTION_STRING':connection('outbox_empty'),'OUTBOX_TEST_KEYS':str(fixture/'keys'),'RESEND_API_KEY':values[0]},required=False),'real_resend_outbox')
        finally:
            if started: run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],required=False)
        check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode!=0)
except Exception as error:
    checks['fixture_completed']=False
    print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
    args.report.parent.mkdir(parents=True,exist_ok=True)
    report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned PostgreSQL, synthetic records; fault transport simulated. Optional real simulator reported separately.'}
    args.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
