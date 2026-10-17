#!/usr/bin/env python3
"""Owned PostgreSQL recipient selection acceptance; no application credentials or provider calls."""
from runtime_schema import ensure_runtime_schema
import argparse,getpass,json,os,subprocess,tempfile
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55527');a=p.parse_args()
root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};version='unverified'
previous='20261014140624_ReaderIdentity';current='20261014171616_CampaignRecipientSelection'
def run(cmd,extra=None,required=True,timeout=150):
 if extra and "DB_CONNECTION_STRING" in extra and any(str(x).endswith("ContentRulesTool.dll") for x in cmd) and not any("seed" in str(x) for x in cmd):
     ensure_runtime_schema(root,env,extra["DB_CONNECTION_STRING"])
 result=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and result.returncode:raise RuntimeError('Owned recipients command failed: '+cmd[0]+' exit='+str(result.returncode))
 return result
def check(name,ok):checks[name]=bool(ok);print(name+'='+str(bool(ok)).lower(),flush=True)
def safe_report(result,name):
 try:
  data=json.loads(result.stdout);valid=result.returncode==0 and data['passed'] and data['count']==len(data['checks']) and all(data['checks'].values())
 except (ValueError,KeyError,TypeError):
  try:diagnostic={k:v for k,v in json.loads(result.stdout).items() if k in ('exceptionType','sqlState','lastCheck')}
  except (ValueError,TypeError,AttributeError):diagnostic={}
  data={'passed':False,'exit_code':result.returncode,'diagnostic':'No complete safe report',**diagnostic};valid=False
 a.report.parent.mkdir(parents=True,exist_ok=True);(a.report.parent/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');check(name,valid)
if run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode==0:raise SystemExit('Owned test port occupied.')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-recipients-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();started=False;env['RECIPIENT_TEST_ROOT']=owned
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  tool=['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll')]
  def snapshot(db,tables):return '\n'.join(sql(db,'SELECT to_jsonb(t)::text FROM "'+table+'" t ORDER BY 1') for table in tables)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
   run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);started=True
   version=sql('postgres','SHOW server_version')
   for db in ('recipients_empty','recipients_prior'):run(['createdb','-h','127.0.0.1','-p',a.port,db])
   check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
   migrate('recipients_prior',previous)
   safe_report(run(tool+['--recipients-seed'],{'DB_CONNECTION_STRING':connection('recipients_prior')},False),'previous_seed')
   prior_tables=('Posts','Categories','Subscribers','NewsletterCampaigns','ReaderLoginTokens','ReaderSessions','TransactionalEmailJobs','TransactionalEmailAttempts')
   before=snapshot('recipients_prior',prior_tables);history=sql('recipients_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
   migrate('recipients_empty',current);migrate('recipients_empty',previous)
   check('empty_down_removes_only_empty_recipient_schema',sql('recipients_empty',"SELECT to_regclass('\"NewsletterRecipients\"') IS NULL AND to_regclass('\"NewsletterRecipientSelections\"') IS NULL")=='t')
   for db in ('recipients_empty','recipients_prior'):migrate(db,current)
   check('all_prior_campaign_subscriber_reader_and_outbox_rows_preserved',snapshot('recipients_prior',prior_tables)==before)
   check('migration_history_is_additive',sql('recipients_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
   check('all_three_guard_triggers_installed',sql('recipients_prior',"SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_RecipientSelection_Progress','TR_NewsletterRecipients_Binding','TR_NewsletterRecipients_BatchCommit') AND tgenabled='O'")=='3')
   for db in ('recipients_empty','recipients_prior'):
    safe_report(run(tool+['--recipients'],{'DB_CONNECTION_STRING':connection(db)},False,timeout=180),db)
   preserved=snapshot('recipients_prior',('NewsletterRecipientSelections','NewsletterRecipients'))
   check('nonempty_down_preserves_all_progress_and_recipient_history',migrate('recipients_prior',previous,False).returncode!=0 and snapshot('recipients_prior',('NewsletterRecipientSelections','NewsletterRecipients'))==preserved)
   check('fixture_uses_actual_postgresql',version.startswith('16.'))
  finally:
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode!=0)
 check('owned_data_removed',not f.exists())
except Exception as error:
 checks['fixture_completed']=False;print(str(error) if isinstance(error,RuntimeError) else type(error).__name__,flush=True)
finally:
 a.report.parent.mkdir(parents=True,exist_ok=True)
 report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned real PostgreSQL; additive upgrade, 10003 synthetic subscribers per schema, actual process restart, batch concurrency, lifecycle cutoff, atomic failure, immutable history; no network email or real credentials.'}
 a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
