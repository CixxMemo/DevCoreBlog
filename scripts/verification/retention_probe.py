#!/usr/bin/env python3
"""Owned real PostgreSQL retention/migration/restore acceptance; no real dotenv or external sender."""
import argparse,getpass,json,os,subprocess,tempfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--port',default='55540');a=p.parse_args()
root=Path(__file__).resolve().parents[2];checks={};version='unverified'
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env.update(PGPASSFILE='/dev/null',PGCONNECT_TIMEOUT='5')
current='20261016185304_RetentionReduction';previous='20261016061853_PublicationPreferences'
a.report.parent.mkdir(parents=True,exist_ok=True)
def run(cmd,extra=None,required=True,timeout=180):
 r=subprocess.run(cmd,cwd=root,env={**env,**(extra or {})},capture_output=True,text=True,timeout=timeout)
 if required and r.returncode:raise RuntimeError('Owned retention fixture command failed: '+cmd[0]+' exit='+str(r.returncode))
 return r
def check(n,v):
 checks[n]=bool(v);print(n+'='+str(bool(v)).lower(),flush=True)
 if not v:raise RuntimeError('Acceptance failed: '+n)
if run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode==0:raise SystemExit('Owned retention port occupied.')
try:
 with tempfile.TemporaryDirectory(prefix='aether-retention-',dir='/private/tmp') as owned:
  f=Path(owned);(f/'socket').mkdir();started=False
  def connection(db):return f'Host=127.0.0.1;Port={a.port};Database={db};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
  def sql(db,q):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q]).stdout.strip()
  def migrate(db,target,required=True):return run(['dotnet','ef','database','update',target,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],{'DB_CONNECTION_STRING':connection(db)},required)
  try:
   run(['initdb','-D',str(f/'pg'),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
   run(['pg_ctl','-D',str(f/'pg'),'-l',str(f/'pg.log'),'-o',f'-h 127.0.0.1 -p {a.port} -k {f}/socket','-w','start']);started=True
   version=sql('postgres','SHOW server_version')
   for db in ['retention_empty','retention_prior','retention_restore','retention_http']:run(['createdb','-h','127.0.0.1','-p',a.port,db])
   migrate('retention_prior',previous)
   sql('retention_prior',"INSERT INTO \"Subscribers\" (\"Id\",\"EmailAddress\",\"AddressKey\",\"Status\",\"Suppression\",\"CreatedUtc\",\"UpdatedUtc\",\"Version\") VALUES ('00000000-0000-0000-0000-000000000050','preserved-retention@example.test','preserved-retention@example.test',0,4,'2026-01-01Z','2026-01-01Z',1)")
   baseline=sql('retention_prior','SELECT to_jsonb(t)::text FROM "Subscribers" t ORDER BY "Id"')
   migrate('retention_empty',current);migrate('retention_empty',previous)
   check('empty_forward_and_down_safe',sql('retention_empty',"SELECT to_regclass('\"ProviderEmailReceipts\"') IS NULL")=='t')
   for db in ['retention_empty','retention_prior']:migrate(db,current)
   check('previous_business_rows_unchanged',sql('retention_prior','SELECT to_jsonb(t)::text FROM "Subscribers" t ORDER BY "Id"')==baseline)
   check('model_snapshot_matches',run(['dotnet','ef','migrations','has-pending-model-changes','--no-build','--project','DevCoreBlog.csproj'],{'DB_CONNECTION_STRING':connection('postgres')},False).returncode==0)
   for db in ['retention_empty','retention_prior']:
    result=run(['dotnet',str(root/'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'),'--retention'],{'DB_CONNECTION_STRING':connection(db)},False)
    try:data=json.loads(result.stdout)
    except ValueError:data={'passed':False,'exceptionType':'InvalidSafeReport'}
    (a.report.parent/(db+'.json')).write_text(json.dumps(data,indent=2)+'\n')
    check(db+'_real_acceptance',result.returncode==0 and data.get('passed') and data['count']==len(data['checks']) and all(data['checks'].values()))
   migrate('retention_http',current)
   from retention_http_probe import verify_worker
   verify_worker(root,f,env,connection('retention_http'),sql,check)
   count=sql('retention_prior','SELECT count(*) FROM "ProviderEmailReceipts"')
   check('nonempty_down_refuses_evidence_loss',migrate('retention_prior',previous,False).returncode!=0 and sql('retention_prior','SELECT count(*) FROM "ProviderEmailReceipts"')==count)
   run(['pg_dump','-h','127.0.0.1','-p',a.port,'-d','retention_prior','-Fc','-f',str(f/'synthetic.dump')])
   run(['pg_restore','-h','127.0.0.1','-p',a.port,'-d','retention_restore','--exit-on-error',str(f/'synthetic.dump')])
   def fingerprint(db):
    tables=sql(db,"SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename").splitlines()
    return [(t,sql(db,f'SELECT md5(coalesce(string_agg(to_jsonb(t)::text,\'|\' ORDER BY to_jsonb(t)::text),\'\')) FROM "{t}" t')) for t in tables]
   check('synthetic_backup_restore_preserves_all_rows_and_checkpoint',fingerprint('retention_prior')==fingerprint('retention_restore'))
   # No web host is started: outgoing workers cannot run on a restored/quarantined database.
   check('restored_receipt_and_suppression_survive',sql('retention_restore','SELECT count(*) FROM "ProviderEmailReceipts"')==count and sql('retention_restore','SELECT count(*) FROM "ProviderAddressSuppressions"')==sql('retention_prior','SELECT count(*) FROM "ProviderAddressSuppressions"'))
  finally:
   if started:run(['pg_ctl','-D',str(f/'pg'),'-m','fast','-w','stop'],required=False)
  check('owned_cluster_stopped',run(['pg_isready','-h','127.0.0.1','-p',a.port],required=False).returncode!=0)
except Exception as ex:
 checks['fixture_completed']=False;print(type(ex).__name__,flush=True)
finally:
 report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'postgres_version':version,'scope':'Owned real PostgreSQL; synthetic rows and backup only. No real database, dotenv, key ring, sender, provider API or backup modified.'}
 a.report.write_text(json.dumps(report,indent=2)+'\n')
raise SystemExit(0 if report['passed'] else 1)
