#!/usr/bin/env python3
"""Read-only media inventory acceptance on an owned real PostgreSQL cluster with synthetic rows."""
import argparse, getpass, json, os, subprocess, tempfile, time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--report',required=True);p.add_argument('--port',default='55504');p.add_argument('--baseline',action='store_true');p.add_argument('--tool',type=Path);p.add_argument('--provider-export',type=Path);a=p.parse_args()
root=Path(__file__).resolve().parents[2];checks={};env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL')};env['PGPASSFILE']='/dev/null'
def run(cmd,extra=None):return subprocess.run(cmd,env={**env,**(extra or {})},capture_output=True,text=True,timeout=45)
def check(k,v):checks[k]=bool(v);print('media_'+k+'='+str(bool(v)).lower(),flush=True)
with tempfile.TemporaryDirectory(prefix='devcoreblog-media-',dir='/tmp') as d:
 f=Path(d);(f/'socket').mkdir();started=False
 def pg(cmd):
  r=run(cmd);assert r.returncode==0,'postgres_fixture_command_failed';return r.stdout.strip()
 def write_sql(q):
  path=f/'fixture.sql';path.write_text(q);return path
 def sql(q):return pg(['psql','-X','-h','127.0.0.1','-p',a.port,'-d','media_test','-At','-v','ON_ERROR_STOP=1','-f',str(write_sql(q))])
 def literal(v):return 'NULL' if v is None else "'"+str(v).replace("'","''")+"'"
 def doc(src):return json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'blockquote','content':[{'type':'image','attrs':{'src':src,'alt':'not identity'}}]}]}})
 def insert(id,version=None,body=None,plain=None,words=None,minutes=None,content='',cover='',coverid=None,summary='',excerpt=''):
  vals=[id,coverid,cover,content,summary,excerpt,version,body,plain,words,minutes]
  sql('INSERT INTO "Posts" ("Id","ThumbnailPublicId","ThumbnailUrl","Content","Summary","Excerpt","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes") VALUES ('+','.join(literal(v) for v in vals)+')')
 def call(assets,user=None):
  manifest=f/'provider.json';manifest.write_text(json.dumps(assets));r=run(['dotnet',str(a.tool or root/'tools/DevCoreBlog.MediaInventoryTool/bin/Debug/net10.0/DevCoreBlog.MediaInventoryTool.dll'),str(manifest)],{'MEDIA_INVENTORY_CONNECTION':f'Host=127.0.0.1;Port={a.port};Database=media_test;Username={user or getpass.getuser()}'});return r,json.loads(r.stdout) if r.stdout else None
 def snapshot():return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"')
 try:
  pg(['initdb','-D',str(f/'pg'),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
  pg(['pg_ctl','-D',str(f/'pg'),'-l',str(f/'pg.log'),'-o',f'-h 127.0.0.1 -p {a.port} -k {f}/socket','-w','start']);started=True
  pg(['createdb','-h','127.0.0.1','-p',a.port,'media_test'])
  # Projection-only fixture permits deliberately corrupt facts that production constraints reject.
  sql('CREATE TABLE "Posts" ("Id" integer PRIMARY KEY,"ThumbnailPublicId" text,"ThumbnailUrl" text NOT NULL,"Content" text NOT NULL,"Summary" text NOT NULL,"Excerpt" text NOT NULL,"DocumentVersion" integer,"DocumentJson" text,"DocumentPlainText" text,"DocumentWordCount" integer,"DocumentReadingMinutes" integer)')
  assets=[{'publicId':'owned/body','urls':['https://images.example.test/body.png']},{'publicId':'owned/cover','urls':['https://images.example.test/cover.png']},{'publicId':'owned/absent','urls':['https://images.example.test/absent.png']}]
  insert(1,1,doc(assets[0]['urls'][0]),'',0,0);insert(2,coverid='owned/cover')
  before=snapshot();r,out=call(assets)
  if a.baseline:
   check('baseline_json_image_is_missed',r.returncode==0 and out['assets'][0]['status']=='No stored reference found')
  else:
   check('nested_validated_image_alias_found',r.returncode==0 and out['assets'][0]['status']=='Referenced' and out['assets'][0]['samplePostIds']==[1])
   check('cover_identity_found',out['assets'][1]['status']=='Referenced')
   check('absent_not_delete_permission',out['assets'][2]['status']=='No stored reference found' and 'No deletion permission' in out['warning'])
   check('validated_and_legacy_counts',out['documentCounts']=={'Validated':1,'Legacy':1} and out['completeReferenceCoverage'])
   check('inventory_zero_database_writes',before==snapshot())
   sql('ALTER TABLE "Posts" ADD COLUMN "IsActive" boolean DEFAULT true, ADD COLUMN "AccessScope" integer DEFAULT 0; UPDATE "Posts" SET "IsActive"=false,"AccessScope"=1 WHERE "Id"=1')
   r,out=call(assets);check('hidden_private_body_still_protected',out['assets'][0]['status']=='Referenced')
   sql('ALTER TABLE "Posts" DROP COLUMN "IsActive",DROP COLUMN "AccessScope"')
   insert(3,content='![old](https:%2F%2Fimages.example.test%2Fbody.png)',summary='owned/absent')
   r,out=call(assets);check('legacy_encoded_alias_and_possible_id',out['assets'][0]['referencingPosts']==2 and out['assets'][2]['possibleReferencingPosts']==1)
   insert(4,1,doc(assets[0]['urls'][0]+'?different=1'),'',0,0)
   r,out=call(assets);check('json_alias_matches_exactly',out['assets'][0]['referencingPosts']==2)
   for name,version,body,plain,words,minutes in [('unknown',99,doc(assets[0]['urls'][0]),'',0,0),('partial',None,doc(assets[0]['urls'][0]),None,None,None),('syntax',1,'{', '',0,0),('schema',1,doc('javascript:alert(1)'),'',0,0),('facts',1,doc(assets[0]['urls'][0]),'wrong',0,0),('limit',1,' '*1048577,'',0,0)]:
    insert(10,version,body,plain,words,minutes);before=snapshot();r,out=call(assets)
    check(name+'_creates_global_uncertainty',r.returncode==0 and not out['completeReferenceCoverage'] and out['uncertainPosts']==1 and out['assets'][2]['status']=='Review required' and out['assets'][0]['referencingPosts']==2 and before==snapshot());sql('DELETE FROM "Posts" WHERE "Id"=10')
   for id in range(20,45):insert(id,coverid='owned/cover')
   r,out=call(assets);check('post_samples_bounded',len(out['assets'][1]['samplePostIds'])==20 and out['assets'][1]['samplesTruncated'] and out['assets'][1]['referencingPosts']==26)
   for id in range(50,75):insert(id,version=99)
   r,out=call(assets);check('uncertain_samples_bounded',len(out['uncertainSamplePostIds'])==20 and out['uncertainSamplesTruncated'] and out['uncertainPosts']==25)
   sql('DELETE FROM "Posts" WHERE "Id">=20')
   for name,bad in [('duplicate_identity',assets+assets[:1]),('credential_url',[{'publicId':'a','urls':['https://user:pass@example.test/x']}]),('http_url',[{'publicId':'a','urls':['http://example.test/x']}]),('unknown_export_field',[{'publicId':'a','urls':['https://example.test/x'],'secret':'forbidden'}]),('asset_limit',[{'publicId':str(n),'urls':['https://example.test/x']} for n in range(1001)])]:
    r,out=call(bad);check(name+'_fails_without_partial_output',r.returncode==1 and out is None and 'forbidden' not in r.stderr and 'pass@' not in r.stderr)
   sql('INSERT INTO "Posts" SELECT n,NULL,\'\',\'\',\'\',\'\',NULL,NULL,NULL,NULL,NULL FROM generate_series(100,10100) n');r,out=call(assets);check('post_limit_no_partial_report',r.returncode==1 and out is None);sql('DELETE FROM "Posts" WHERE "Id">=100')
   insert(100,content='x'*1048577);r,out=call(assets);check('oversize_legacy_no_partial_report',r.returncode==1 and out is None);sql('DELETE FROM "Posts" WHERE "Id"=100')
   sql('INSERT INTO "Posts" SELECT n,NULL,\'\',repeat(\'x\',1048576),\'\',\'\',NULL,NULL,NULL,NULL,NULL FROM generate_series(100,132) n');r,out=call(assets);check('total_budget_no_partial_report',r.returncode==1 and out is None);sql('DELETE FROM "Posts" WHERE "Id">=100')
   if a.provider_export:
    real=json.loads(a.provider_export.read_text());assert real
    insert(200,1,doc(real[0]['urls'][0]),'',0,0);insert(201,coverid=real[0]['publicId']);before=snapshot();r,out=call(real)
    check('real_provider_export_matches_json_and_cover_in_owned_db',r.returncode==0 and out['assets'][0]['referencingPosts']==2 and before==snapshot())
   sql('CREATE ROLE f20_reader LOGIN; GRANT USAGE ON SCHEMA public TO f20_reader; GRANT SELECT ON "Posts" TO f20_reader')
   r,out=call(assets,user='f20_reader');check('select_only_role_succeeds',r.returncode==0 and out['scannedPosts']>=4)
   r=run(['psql','-X','-h','127.0.0.1','-p',a.port,'-U','f20_reader','-d','media_test','-v','ON_ERROR_STOP=1','-c','UPDATE "Posts" SET "Content"=$$blocked$$ WHERE "Id"=999999']);check('fixture_role_cannot_write',r.returncode!=0 and 'permission denied' in r.stderr.lower())
   lockfile=f/'lock.sql';lockfile.write_text('BEGIN;LOCK TABLE "Posts" IN ACCESS EXCLUSIVE MODE;SELECT pg_sleep(12);COMMIT;')
   blocker=subprocess.Popen(['psql','-X','-h','127.0.0.1','-p',a.port,'-d','media_test','-f',str(lockfile)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   try:
    time.sleep(.5);begin=time.monotonic();r,out=call(assets);check('blocked_query_fails_bounded_without_partial_output',r.returncode==1 and out is None and time.monotonic()-begin<15)
   finally:blocker.wait(timeout=15)
   sql('ALTER TABLE "Posts" DROP COLUMN "DocumentJson"');r,out=call(assets);check('old_schema_fails_closed',r.returncode==1 and out is None)
 finally:
  if started:run(['pg_ctl','-D',str(f/'pg'),'-m','fast','-w','stop'])
report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'providerExportUsed':bool(a.provider_export),'projectionOnlyFixture':True};Path(a.report).write_text(json.dumps(report,indent=2)+'\n');assert report['passed']
