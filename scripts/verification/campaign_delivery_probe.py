#!/usr/bin/env python3
"""Real owned PostgreSQL campaign work acceptance; no real recipients, DB or credentials in normal mode."""
from runtime_schema import ensure_runtime_schema
import argparse,getpass,json,os,stat,subprocess,tempfile
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55530');p.add_argument('--nginx',type=Path)
p.add_argument('--live-only',action='store_true');p.add_argument('--env-file',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')}
env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5');checks={};version='unverified'
previous='20261014175940_ProviderEmailEvents';current='20261014185330_CampaignDeliveryWork'
def run(cmd,extra=None,required=True,timeout=150):
 if extra and "DB_CONNECTION_STRING" in extra and any(str(x).endswith("ContentRulesTool.dll") for x in cmd) and not any("seed" in str(x) for x in cmd):
     ensure_runtime_schema(root,env,extra["DB_CONNECTION_STRING"])
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned delivery command failed: '+cmd[0]+' exit='+str(r.returncode))
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
 if not valid:raise RuntimeError('Delivery probe failed: '+name+'; safe details retained in report')
if run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode==0:raise SystemExit('Owned test port occupied.')
try:
 with tempfile.TemporaryDirectory(prefix='devcoreblog-delivery-',dir='/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();(f/'keys').mkdir(mode=0o700);started=False;env['CAMPAIGN_DELIVERY_TEST_ROOT']=owned
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  tool=['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll')]
  def snapshot(db,tables):return '\n'.join(sql(db,'SELECT to_jsonb(t)::text FROM "'+t+'" t ORDER BY 1') for t in tables)
  try:
   run(['initdb','-D',owned+'/pg','--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
   run(['pg_ctl','-D',owned+'/pg','-l',owned+'/pg.log','-o',f'-h 127.0.0.1 -p {a.port} -k {owned}/socket','-w','start']);started=True;version=sql('postgres','SHOW server_version')
   if a.live_only:
    if a.env_file is None or stat.S_IMODE(a.env_file.stat().st_mode)!=0o600:raise RuntimeError('Owner-only key source required')
    key=None
    for line in a.env_file.read_text().splitlines():
     k,sep,v=line.partition('=')
     if sep and k.strip()=='RESEND_API_KEY':key=v.strip().strip('\"\'')
    if not key or not key.startswith('re_'):raise RuntimeError('Sending key absent')
    run(['createdb','-h','127.0.0.1','-p',a.port,'delivery_live']);migrate('delivery_live',current)
    safe_report(run(tool+['--campaign-delivery-live'],{'DB_CONNECTION_STRING':connection('delivery_live'),'RESEND_API_KEY':key},False),'simulator')
   else:
    for db in ('delivery_empty','delivery_prior','delivery_http'):run(['createdb','-h','127.0.0.1','-p',a.port,db])
    check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
    migrate('delivery_prior',previous);safe_report(run(tool+['--campaign-delivery-seed'],{'DB_CONNECTION_STRING':connection('delivery_prior')},False),'previous_seed')
    prior=('Posts','Categories','Subscribers','NewsletterCampaigns','NewsletterRecipients','NewsletterRecipientSelections','ProviderEmailEvents','ProviderEmailObservations','ProviderAddressSuppressions','ReaderSessions','TransactionalEmailJobs','TransactionalEmailAttempts')
    before=snapshot('delivery_prior',prior);history=sql('delivery_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
    migrate('delivery_empty',current);migrate('delivery_empty',previous)
    check('empty_down_removes_only_empty_delivery_schema',sql('delivery_empty',"SELECT to_regclass('\"CampaignDispatches\"') IS NULL AND to_regclass('\"CampaignDeliveryJobs\"') IS NULL AND to_regclass('\"CampaignDeliveryAttempts\"') IS NULL")=='t')
    for db in ('delivery_empty','delivery_prior','delivery_http'):migrate(db,current)
    check('all_source_subscriber_recipient_provider_reader_and_outbox_rows_preserved',snapshot('delivery_prior',prior)==before)
    check('migration_history_additive',sql('delivery_prior','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()==history+[current])
    check('all_five_guards_installed',sql('delivery_prior',"SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_CampaignDispatches_Guard','TR_CampaignDeliveryJobs_Guard','TR_CampaignDeliveryAttempts_Guard','TR_CampaignDeliveryJobs_Commit','TR_CampaignDeliveryAttempts_Commit') AND tgenabled='O'")=='5')
    for db in ('delivery_empty','delivery_prior'):safe_report(run(tool+['--campaign-delivery'],{'DB_CONNECTION_STRING':connection(db)},False,timeout=180),db)
    preserved=snapshot('delivery_prior',('CampaignDispatches','CampaignDeliveryJobs','CampaignDeliveryAttempts'))
    check('nonempty_down_preserves_dispatch_payload_and_attempt_history',migrate('delivery_prior',previous,False).returncode!=0 and snapshot('delivery_prior',('CampaignDispatches','CampaignDeliveryJobs','CampaignDeliveryAttempts'))==preserved)
    from campaign_delivery_http_fixture import run_http_fixture
    run_http_fixture(root,f,env,connection('delivery_http'),a.port,a.nginx,checks)
   check('fixture_uses_actual_postgresql',version.startswith('16.'))
  finally:
   if started:run(['pg_ctl','-D',owned+'/pg','-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode!=0)
 check('owned_data_removed',not f.exists())
except Exception as e:checks['fixture_completed']=False;print(str(e) if isinstance(e,RuntimeError) else type(e).__name__,flush=True)
finally:
 a.report.parent.mkdir(parents=True,exist_ok=True);report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,
  'scope':'Owned real PostgreSQL/HTTPS/Nginx restart, concurrency, lifecycle and fault acceptance; synthetic faults, no real email.' if not a.live_only else 'One Resend delivered@resend.dev simulator message through persistent campaign pipeline. API acceptance only; no personal recipient, DKIM/mailbox/production claim.'};a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
