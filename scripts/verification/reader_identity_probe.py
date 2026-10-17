#!/usr/bin/env python3
"""Owned PostgreSQL upgrade, login concurrency and real MVC acceptance; optional fixed-simulator test is explicit."""
from runtime_schema import ensure_runtime_schema
import argparse,getpass,json,os,re,subprocess,tempfile
from pathlib import Path
from reader_identity_http_fixture import run_http_fixture
parser=argparse.ArgumentParser();parser.add_argument('--report',type=Path,required=True);parser.add_argument('--port',default='55526')
parser.add_argument('--env-file',type=Path);parser.add_argument('--browser-module',type=Path);parser.add_argument('--browser-executable',type=Path)
args=parser.parse_args();root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};version='unverified'
previous='20261014131546_NewsletterCampaignSnapshots';current='20261014140624_ReaderIdentity'
def run(cmd,extra=None,required=True,timeout=90):
 if extra and "DB_CONNECTION_STRING" in extra and any(str(x).endswith("ContentRulesTool.dll") for x in cmd) and not any("seed" in str(x) for x in cmd):
     ensure_runtime_schema(root,env,extra["DB_CONNECTION_STRING"])
 result=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and result.returncode:raise RuntimeError('Owned fixture command failed: '+cmd[0]+' exit='+str(result.returncode))
 return result
def check(name,value):checks[name]=bool(value);print(name+'='+str(bool(value)).lower(),flush=True)
def safe_report(result,name):
 try:
  data=json.loads(result.stdout);valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
 except (ValueError,KeyError,TypeError):data={'passed':False,'diagnostic':'No complete safe report','exit_code':result.returncode};valid=False
 args.report.parent.mkdir(parents=True,exist_ok=True);(args.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
if run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode==0:raise SystemExit('Owned test port occupied.')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-reader-',dir='/tmp') as owned:
  fixture=Path(owned);(fixture/'socket').mkdir();(fixture/'keys').mkdir(mode=0o700);started=False;env['READER_TEST_ROOT']=owned
  def connection(db):return f'Host=127.0.0.1;Port={args.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',args.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  def snapshot():return sql('reader_prior', '''SELECT to_jsonb(t)::text FROM "Subscribers" t ORDER BY 1;
    SELECT (to_jsonb(t)-'Purpose')::text FROM "TransactionalEmailJobs" t ORDER BY 1;
    SELECT to_jsonb(t)::text FROM "TransactionalEmailAttempts" t ORDER BY 1''')
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
   run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {args.port} -k {owned}/socket','-w','start']);started=True
   version=sql('postgres','SHOW server_version')
   for db in ('reader_empty','reader_prior','reader_http'):run(['createdb','-h','127.0.0.1','-p',args.port,db])
   check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
   migrate('reader_prior',previous)
   sql('reader_prior', '''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","Version")
    VALUES ('00000000-0000-0000-0000-000000000001','preserved@example.test','preserved@example.test',0,0,'2026-01-01Z','2026-01-01Z',1);
    INSERT INTO "TransactionalEmailJobs" ("Id","SubscriberId","SubscriberVersion","ProtectedPayload","PayloadHash","CreatedUtc","UpdatedUtc","ExpiresUtc","DueUtc","State","Problem","AttemptCount","FirstAttemptUtc")
    VALUES ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001',1,'AAAA',repeat('A',64),'2026-01-01Z','2026-01-01Z','2026-01-02Z','2026-01-01Z',5,7,1,'2026-01-01Z');
    INSERT INTO "TransactionalEmailAttempts" ("JobId","Number","LeaseId","StartedUtc","CompletedUtc","Outcome","Reason")
    VALUES ('00000000-0000-0000-0000-000000000002',1,'00000000-0000-0000-0000-000000000003','2026-01-01Z','2026-01-01Z',3,13)''')
   before=snapshot();history=sql('reader_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
   check('prior_schema_has_no_reader',sql('reader_prior',"SELECT to_regclass('\"ReaderSessions\"') IS NULL")=='t')
   migrate('reader_empty',current);migrate('reader_empty',previous)
   check('empty_down_safe',sql('reader_empty',"SELECT to_regclass('\"ReaderSessions\"') IS NULL AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='TransactionalEmailJobs' AND column_name='Purpose')")=='t')
   for db in ('reader_empty','reader_prior'):migrate(db,current)
   check('prior_lifecycle_and_outbox_journal_preserved',before==snapshot())
   check('old_mail_default_verification_purpose',sql('reader_prior','SELECT "Purpose" FROM "TransactionalEmailJobs"')=='0')
   check('history_additive',sql('reader_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
   for db in ('reader_empty','reader_prior'):
    safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--reader-checks'],{'DB_CONNECTION_STRING':connection(db)},False),db)
   identity_before=sql('reader_prior','SELECT to_jsonb(t)::text FROM "ReaderLoginTokens" t ORDER BY 1; SELECT to_jsonb(t)::text FROM "ReaderSessions" t ORDER BY 1; SELECT to_jsonb(t)::text FROM "ReaderLoginBudgets" t ORDER BY 1')
   check('nonempty_down_preserves_proofs_sessions_and_budget',migrate('reader_prior',previous,False).returncode!=0 and identity_before==sql('reader_prior','SELECT to_jsonb(t)::text FROM "ReaderLoginTokens" t ORDER BY 1; SELECT to_jsonb(t)::text FROM "ReaderSessions" t ORDER BY 1; SELECT to_jsonb(t)::text FROM "ReaderLoginBudgets" t ORDER BY 1'))
   migrate('reader_http',current)
   run_http_fixture(root,fixture,env,connection('reader_http'),args.port,args.report.parent,checks,args.browser_module,args.browser_executable)
   if args.env_file:
    if args.env_file.is_symlink() or args.env_file.stat().st_mode & 0o077:raise RuntimeError('Private credential file required')
    keys=[line.split('=',1)[1].strip().strip('\"\'') for line in args.env_file.read_text().splitlines() if line.startswith('RESEND_API_KEY=')]
    if len(keys)!=1 or not re.fullmatch(r're_[A-Za-z0-9_-]+',keys[0]):raise RuntimeError('Valid local credential required')
    run(['createdb','-h','127.0.0.1','-p',args.port,'reader_live']);migrate('reader_live',current)
    safe_report(run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--reader-live'],{'DB_CONNECTION_STRING':connection('reader_live'),'RESEND_API_KEY':keys[0]},False),'real_resend_reader')
  finally:
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',args.port],required=False).returncode!=0)
except Exception as error:checks['fixture_completed']=False;print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
 args.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned real PostgreSQL/MVC login, expiry, revocation, cookie separation. Observer mailbox is simulated; optional fixed Resend simulator is separate.'};args.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
