#!/usr/bin/env python3
"""Real PostgreSQL acceptance for the finite operator manifest; never reads application dotenv."""
import argparse,hashlib,json,os,subprocess,tempfile,time,getpass,shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--report',required=True);p.add_argument('--port',default='55502');a=p.parse_args();root=Path(__file__).resolve().parents[2];checks={}
env={k:v for k,v in os.environ.items() if k in ['PATH','HOME','TMPDIR','DOTNET_ROOT','NUGET_PACKAGES','LANG','LC_ALL']}
env['PGPASSFILE']='/dev/null';env['PGCONNECT_TIMEOUT']='5';tool=root/'tools/DevCoreBlog.DocumentTransitionTool/bin/Debug/net10.0/DevCoreBlog.DocumentTransitionTool.dll'
def run(cmd,extra=None,ok=True):
 r=subprocess.run(cmd,env={**env,**(extra or {})},capture_output=True,text=True,timeout=50)
 if ok:assert r.returncode==0,('command_failed',cmd[0])
 return r
def check(name,v):checks[name]=bool(v);print('transition_'+name+'='+str(bool(v)).lower(),flush=True)
with tempfile.TemporaryDirectory(prefix='devcoreblog-f17.',dir='/tmp') as owned:
 f=Path(owned);(f/'socket').mkdir();pg=f/'postgres';started=False
 connection=f'Host=127.0.0.1;Port={a.port};Database=devcoreblog_f01_test;Username={getpass.getuser()}'
 db_env={'TRANSITION_CONNECTION':connection,'DB_CONNECTION_STRING':connection}
 def sql(q,ok=True):return run(['psql','-X','-h','127.0.0.1','-p',a.port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],ok=ok).stdout.strip()
 def call(mode,*args,ok=True):
  r=run(['dotnet',str(tool),mode,*map(str,args)],db_env,ok);return r.returncode,json.loads(r.stdout)
 n=0
 def export():
  global n;n+=1;t=f/f'export-{n}.json';call('export',t);return json.loads(t.read_text()),t
 def proof(s):
  global n;n+=1;archive=f/'backups'/f'archive-{n}.dump';archive.parent.mkdir(exist_ok=True);code,backup=call('backup',archive)
  dest=f'f56_transition_{n}'
  run(['python3',str(root/'scripts/operations/test_database_archive.py'),'restore','--fixture-root',str(f),'--port',a.port,'--database',dest,'--archive',str(archive),'--sha256',backup['archiveSha256'].lower()])
  target=f/'restored.json';target.unlink(missing_ok=True)
  restored=connection.replace('Database=devcoreblog_f01_test','Database='+dest)
  run(['dotnet',str(tool),'export',str(target)],{'TRANSITION_CONNECTION':restored});d=json.loads(target.read_text());assert s['digest']==d['digest']
  proof=f/f'proof-{n}.json';proof.write_text(json.dumps({'version':1,'targetFingerprint':s['targetFingerprint'],'archiveSha256':backup['archiveSha256'],'sourceDigest':s['digest'],'restoredDigest':d['digest']}));return archive,proof
 def raw_rows(s):
  decoder=json.JSONDecoder();text=s['posts'];offset=1;out=[]
  while True:
   while offset<len(text) and text[offset] in ' ,\n':offset+=1
   if text[offset]==']':break
   row,end=decoder.raw_decode(text,offset);out.append((row,hashlib.sha256(text[offset:end].encode()).hexdigest().upper()));offset=end
  return out
 def make_plan(s,sfile):
  global n;n+=1;docs=f/f'documents-{n}.json';rows=[]
  for row,hash in raw_rows(s):
   if row['IsActive'] and row['DocumentVersion'] is None:
    canonical=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':row['Content']}]}]}},ensure_ascii=False)
    rows.append({'id':row['Id'],'sourceHash':hash,'documentJson':canonical})
  docs.write_text(json.dumps({'version':1,'rows':rows}));plan=f/f'plan-{n}.json';call('plan',sfile,docs,plan);return plan
 try:
  run(['initdb','-D',str(pg),'--auth=trust','--encoding=UTF8','--locale=en_US.UTF-8'])
  run(['pg_ctl','-D',str(pg),'-l',str(f/'server.log'),'-o',f'-h 127.0.0.1 -p {a.port} -k {f}/socket','-w','start']);started=True
  run(['createdb','-h','127.0.0.1','-p',a.port,'devcoreblog_f01_test'])
  run(['dotnet','ef','database','update','--no-build','--project',str(root/'DevCoreBlog.csproj'),'--startup-project',str(root/'DevCoreBlog.csproj'),'--context','ApplicationDbContext','--no-color'],db_env)
  empty,sfile=export();ep=make_plan(empty,sfile);archive,bp=proof(empty)
  check('empty_migration_and_manifest_no_write',call('dry-run',ep)[1]['noWrites'] and call('apply',ep,archive,bp)[1]['changed']==0 and export()[0]['digest']==empty['digest'])
  sql('INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (1,\'Synthetic\',\'synthetic\',\'2020-01-01Z\',true,1); INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion") VALUES (1,\'One\',\'one\',\'Summary\',\'Excerpt\',\'Türkçe 😊 <script>literal</script>\',1,\'2020-01-01Z\',true,false,\'2030-01-01Z\',\'\',7,1),(2,\'Two\',\'two\',\'\',\'\',\'Second body\',1,\'2020-01-02Z\',true,true,\'2020-01-03Z\',\'https://example.test/legacy.png\',9,3);')
  source,sfile=export();plan=make_plan(source,sfile);archive,bp=proof(source)
  check('dry_run_has_zero_write',call('dry-run',plan)[1]['candidates']==2 and export()[0]['digest']==source['digest'])
  check('private_artifacts_owner_only',(plan.stat().st_mode&0o777)==0o600 and (archive.stat().st_mode&0o777)==0o600)
  original=json.loads(plan.read_text())
  for name,mutator in [('target',lambda d:d.update(targetFingerprint='0'*64)),('snapshot',lambda d:d.update(sourceDigest='0'*64)),('row_hash',lambda d:d['rows'][0].update(sourceHash='0'*64)),('revision',lambda d:d['rows'][0].update(editVersion=99)),('preserved',lambda d:d['rows'][0].update(preservedHash='0'*64)),('schema',lambda d:d['rows'][0].update(documentJson='{"version":99}')),('limit',lambda d:d['rows'][0].update(documentJson=' '*1048577)),('duplicate',lambda d:d['rows'].append(d['rows'][0])),('omitted',lambda d:d['rows'].pop()),('empty-manifest',lambda d:d.update(rows=[]))]:
   d=json.loads(json.dumps(original));mutator(d);bad=f/(name+'.json');bad.write_text(json.dumps(d));code,r=call('apply',bad,archive,bp,ok=False);check('reject_'+name,code==1 and export()[0]['digest']==source['digest'])
  badproof=f/'bad-proof.json';d=json.loads(bp.read_text());d['restoredDigest']='0'*64;badproof.write_text(json.dumps(d));check('unverified_restore_refuses_write',call('apply',plan,archive,badproof,ok=False)[0]==1 and export()[0]['digest']==source['digest'])
  d=json.loads(bp.read_text());d['sourceDigest']=d['restoredDigest']='0'*64;badproof.write_text(json.dumps(d));check('unrelated_verified_backup_refuses_write',call('apply',plan,archive,badproof,ok=False)[0]==1 and export()[0]['digest']==source['digest'])
  broken=f/'corrupt.dump';broken.write_bytes(b'corrupt');check('bad_archive_checksum_refuses_write',call('apply',plan,broken,bp,ok=False)[0]==1 and export()[0]['digest']==source['digest'])
  sql('UPDATE "Posts" SET "ViewCount"="ViewCount"+1 WHERE "Id"=1');changed=export()[0]['digest'];check('counter_race_is_not_overwritten',call('apply',plan,archive,bp,ok=False)[0]==1 and export()[0]['digest']==changed);sql('UPDATE "Posts" SET "ViewCount"="ViewCount"-1 WHERE "Id"=1')
  sql('UPDATE "Posts" SET "Title"=\'Changed\',"EditVersion"="EditVersion"+1 WHERE "Id"=2');changed=export()[0]['digest'];check('edit_race_is_not_overwritten',call('apply',plan,archive,bp,ok=False)[0]==1 and export()[0]['digest']==changed);sql('UPDATE "Posts" SET "Title"=\'Two\',"EditVersion"="EditVersion"-1 WHERE "Id"=2')
  blocker=subprocess.Popen(['psql','-X','-h','127.0.0.1','-p',a.port,'-d','devcoreblog_f01_test','-c','BEGIN; LOCK TABLE "Posts" IN EXCLUSIVE MODE; SELECT pg_sleep(4); COMMIT;'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  time.sleep(.5);start=time.monotonic();code,_=call('apply',plan,archive,bp,ok=False);check('lock_conflict_bounded',code==1 and time.monotonic()-start<3);blocker.wait(timeout=6)
  sql('CREATE FUNCTION transition_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Id"=2 THEN RAISE EXCEPTION \'synthetic transition failure\'; END IF; RETURN NEW; END $$; CREATE TRIGGER transition_fail BEFORE UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION transition_fail();')
  check('second_row_failure_rolls_back_first',call('apply',plan,archive,bp,ok=False)[0]==1 and export()[0]['digest']==source['digest']);sql('DROP TRIGGER transition_fail ON "Posts"; DROP FUNCTION transition_fail()')
  code,result=call('apply',plan,archive,bp);after,af=export();check('all_active_documents_atomically_known',code==0 and result['changed']==2 and result['activeKnownDocuments']==2)
  ar=[r for r,h in raw_rows(after)];br=[r for r,h in raw_rows(source)]
  mutable={'DocumentVersion','DocumentJson','DocumentPlainText','DocumentWordCount','DocumentReadingMinutes','EditVersion','UpdatedDate'}
  check('all_unrelated_fields_preserved',all({k:v for k,v in x.items() if k not in mutable}=={k:v for k,v in y.items() if k not in mutable} for x,y in zip(ar,br)))
  check('category_receipt_migration_sequence_preserved',all(after[k]==source[k] for k in ['categories','receipts','migrations','sequences']))
  check('revision_and_typed_derivatives_consistent',all(x['EditVersion']==y['EditVersion']+1 and x['DocumentVersion']==1 and x['DocumentPlainText']==y['Content'] and x['DocumentWordCount']>0 and x['DocumentReadingMinutes']==1 and x['UpdatedDate'] is not None for x,y in zip(ar,br)))
  again=call('apply',plan,archive,bp)[1];check('repeated_apply_has_zero_write',again['status']=='already_applied' and again['changed']==0 and export()[0]['digest']==after['digest'])
  # Active inconsistent storage is rejected rather than silently repaired or treated as legacy.
  sql('ALTER TABLE "Posts" DROP CONSTRAINT "CK_Posts_DocumentFacts"; UPDATE "Posts" SET "DocumentPlainText"=\'tampered\' WHERE "Id"=1');changed=export()[0]['digest'];check('inconsistent_existing_document_rejected',call('dry-run',plan,ok=False)[0]==1 and export()[0]['digest']==changed)
 finally:
  if started:run(['pg_ctl','-D',str(pg),'-m','fast','-w','stop'],ok=False)
report={'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks};Path(a.report).write_text(json.dumps(report,indent=2)+'\n');assert report['passed'],[k for k,v in checks.items() if not v]
