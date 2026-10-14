#!/usr/bin/env python3
"""Owned F34 migration/concurrency acceptance. No real environment, sender or provider credentials are loaded."""
import argparse,getpass,json,os,subprocess,tempfile
from pathlib import Path
from campaign_http_fixture import run_http_fixture
parser=argparse.ArgumentParser();parser.add_argument('--report',type=Path,required=True);parser.add_argument('--port',default='55525')
args=parser.parse_args();root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
checks={};version='unverified';previous='20261014124542_SubscriberUnsubscriptionPeriod';current='20261014131546_NewsletterCampaignSnapshots'
def run(command,extra=None,required=True,timeout=90):
    result=subprocess.run(command,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
    if required and result.returncode:raise RuntimeError('Fixture command failed: '+command[0]+' exit='+str(result.returncode))
    return result
def check(name,value):checks[name]=bool(value);print(name+'='+str(bool(value)).lower(),flush=True)
def safe_report(result,name):
    try:
        data=json.loads(result.stdout);valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
    except (ValueError,KeyError,TypeError):data={'passed':False,'diagnostic':'No complete safe report','exit_code':result.returncode};valid=False
    args.report.parent.mkdir(parents=True,exist_ok=True);(args.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
if run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode==0:raise SystemExit('Owned test port occupied.')
try:
    with tempfile.TemporaryDirectory(prefix='devcoreblog-campaign-',dir='/tmp') as owned:
        fixture=Path(owned);(fixture/'socket').mkdir();(fixture/'keys').mkdir(mode=0o700);started=False
        env['CAMPAIGN_TEST_ROOT']=str(fixture)
        def connection(database):return f'Host=127.0.0.1;Port={args.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        def sql(database,query):return run(['psql','-X','-h','127.0.0.1','-p',args.port,'-d',database,'-At','-v','ON_ERROR_STOP=1','-c',query]).stdout.strip()
        def migrate(database,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(database)},required=required)
        def snapshot():return '\n'.join(sql('campaign_prior',f'SELECT to_jsonb(t)::text FROM "{table}" t ORDER BY 1') for table in ('Posts','Categories','Subscribers','SubscriberVerificationTokens','TransactionalEmailJobs','TransactionalEmailAttempts','SubscriptionRegistrationBudgets','WebhookReceipts'))
        try:
            run(['initdb','-D',str(fixture/'pg'),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
            run(['pg_ctl','-D',str(fixture/'pg'),'-l',str(fixture/'pg.log'),'-o',f'-h 127.0.0.1 -p {args.port} -k {fixture}/socket','-w','start']);started=True
            version=sql('postgres','SHOW server_version')
            for database in ('campaign_empty','campaign_prior','campaign_http'):run(['createdb','-h','127.0.0.1','-p',args.port,database])
            check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},required=False).returncode==0)
            migrate('campaign_prior',previous)
            sql('campaign_prior', '''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (9001,'Preserved','preserved','2026-01-01Z',true,1);
                INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion")
                VALUES (9001,'Preserved','preserved','Summary','Excerpt','Preserved private fixture',9001,'2026-01-01Z',true,false,'2030-01-01Z','',7,1);
                INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version")
                VALUES ('00000000-0000-0000-0000-000000000001','preserved@example.test','preserved@example.test',0,0,'2026-01-01Z','2026-01-01Z',1)''')
            before=snapshot();history=sql('campaign_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
            check('prior_schema_has_no_campaign',sql('campaign_prior',"SELECT to_regclass('\"NewsletterCampaigns\"') IS NULL")=='t')
            migrate('campaign_empty',current);migrate('campaign_empty',previous)
            check('empty_down_removes_only_empty_campaign_schema',sql('campaign_empty',"SELECT to_regclass('\"NewsletterCampaigns\"') IS NULL AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE proname='GuardNewsletterCampaignSnapshot')")=='t')
            for database in ('campaign_empty','campaign_prior'):migrate(database,current)
            check('all_previous_rows_preserved',before==snapshot())
            check('history_additive',sql('campaign_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
            check('immutability_guard_installed',sql('campaign_prior',"SELECT count(*) FROM pg_trigger WHERE tgname='TR_NewsletterCampaigns_Immutable' AND tgenabled='O'")=='1')
            for database in ('campaign_empty','campaign_prior'):
                safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--campaigns'],{'DB_CONNECTION_STRING':connection(database)},required=False),database)
            count=sql('campaign_prior','SELECT count(*) FROM "NewsletterCampaigns"')
            hashes=sql('campaign_prior','SELECT "Id" || \':\' || "SnapshotHash" || \':\' || "Version" FROM "NewsletterCampaigns" ORDER BY 1')
            check('nonempty_down_retains_all_campaigns',migrate('campaign_prior',previous,False).returncode!=0 and count==sql('campaign_prior','SELECT count(*) FROM "NewsletterCampaigns"') and hashes==sql('campaign_prior','SELECT "Id" || \':\' || "SnapshotHash" || \':\' || "Version" FROM "NewsletterCampaigns" ORDER BY 1'))
            check('preexisting_source_and_subscriber_preserved_after_tests',sql('campaign_prior','SELECT "Title" || \':\' || "ViewCount" || \':\' || "EditVersion" FROM "Posts" WHERE "Id"=9001')=='Preserved:7:1' and sql('campaign_prior',"SELECT \"EmailAddress\" || ':' || \"Version\" FROM \"Subscribers\" WHERE \"Id\"='00000000-0000-0000-0000-000000000001'")=='preserved@example.test:1')
            migrate('campaign_http',current)
            run_http_fixture(root,fixture,env,connection('campaign_http'),args.port,checks)
        finally:
            if started:run(['pg_ctl','-D',str(fixture/'pg'),'-m','fast','-w','stop'],required=False)
        check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode!=0)
except Exception as error:
    checks['fixture_completed']=False;print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
    args.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned PostgreSQL immutable intent/concurrency and authenticated MVC deletion conflict. No provider/recipient calls.'};args.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
