#!/usr/bin/env python3
"""Owned PostgreSQL test-email migration/admission/result acceptance; no app credentials in normal mode."""
from runtime_schema import ensure_runtime_schema
import argparse,getpass,json,os,subprocess,tempfile
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55533');p.add_argument('--browser-module',type=Path);p.add_argument('--browser-executable',type=Path)
p.add_argument('--database-only',action='store_true');p.add_argument('--env-file',type=Path);a=p.parse_args();root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};version='unverified';previous='20261014185330_CampaignDeliveryWork';current='20261015065456_TestEmailSubmissions'
def run(cmd,extra=None,required=True,timeout=150):
 if extra and "DB_CONNECTION_STRING" in extra and any(str(x).endswith("ContentRulesTool.dll") for x in cmd) and not any("seed" in str(x) for x in cmd):
     ensure_runtime_schema(root,env,extra["DB_CONNECTION_STRING"])
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned test-email command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(name,ok):checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
def safe_report(result,name):
 try:
  data=json.loads(result.stdout);valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
 except (ValueError,KeyError,TypeError):
  try:data=json.loads(result.stdout)
  except (ValueError,TypeError):data={'passed':False,'diagnostic':'No complete safe report','exit_code':result.returncode}
  valid=False
 a.report.parent.mkdir(parents=True,exist_ok=True);(a.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
 if not valid:raise RuntimeError('Test email probe failed: '+name+'; safe report retained')
if run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode==0:raise SystemExit('Owned test port occupied')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-test-email-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);env['TEST_EMAIL_ROOT']=owned;started=False
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  tool=['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll')]
  def snapshot(db,tables):return '\n'.join(sql(db,'SELECT to_jsonb(t)::text FROM "'+t+'" t ORDER BY 1') for t in tables)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8']);run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);started=True;version=sql('postgres','SHOW server_version')
   for db in ('test_email_empty','test_email_prior','test_email_http'):run(['createdb','-h','127.0.0.1','-p',a.port,db])
   check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
   migrate('test_email_prior',previous);safe_report(run(tool+['--test-email-seed'],{'DB_CONNECTION_STRING':connection('test_email_prior')},False),'test_email_previous_seed')
   prior=('Posts','Categories','Subscribers','NewsletterCampaigns','NewsletterRecipients','CampaignDispatches','CampaignDeliveryJobs','CampaignDeliveryAttempts','ProviderEmailEvents','ProviderAddressSuppressions','TransactionalEmailJobs','ReaderSessions')
   before=snapshot('test_email_prior',prior);history=sql('test_email_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
   migrate('test_email_empty',current);migrate('test_email_empty',previous)
   check('empty_down_removes_only_empty_test_schema',sql('test_email_empty',"SELECT to_regclass('\"TestEmailSubmissions\"') IS NULL AND to_regclass('\"CampaignDeliveryJobs\"') IS NOT NULL")=='t')
   for db in ('test_email_empty','test_email_prior','test_email_http'):migrate(db,current)
   check('all_previous_source_recipient_dispatch_provider_reader_and_outbox_rows_preserved',snapshot('test_email_prior',prior)==before)
   check('migration_history_additive',sql('test_email_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
   check('guard_installed',sql('test_email_prior',"SELECT count(*) FROM pg_trigger WHERE tgname='TR_TestEmailSubmissions_Guard' AND tgenabled='O'")=='1')
   for db in ('test_email_empty','test_email_prior'):safe_report(run(tool+['--test-email'],{'DB_CONNECTION_STRING':connection(db)},False,timeout=180),db)
   saved=snapshot('test_email_prior',('TestEmailSubmissions',));check('nonempty_down_preserves_intent_and_result_history',migrate('test_email_prior',previous,False).returncode!=0 and snapshot('test_email_prior',('TestEmailSubmissions',))==saved)
   if not a.database_only:
    from test_email_http_fixture import run_http_fixture
    run_http_fixture(root,f,env,connection('test_email_http'),a.port,a.report.parent,a.browser_module,a.browser_executable,a.env_file,checks)
   check('actual_postgresql_used',version.startswith('16.'))
  finally:
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode!=0)
 check('owned_fixture_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 a.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,
  'scope':'Owned real PostgreSQL; persistent fixed-recipient test admission, concurrency, rolling budgets, results and process restart. Synthetic transport faults; optional explicit real simulator mode separate.'};a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
