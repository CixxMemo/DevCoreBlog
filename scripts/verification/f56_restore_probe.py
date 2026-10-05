#!/usr/bin/env python3
"""Prove backup/restore and previous-code compatibility only in a disposable cluster."""
import argparse,getpass,hashlib,json,os,shutil,subprocess,time,urllib.request
from pathlib import Path
from http_probe_support import cookie_opener,submit_login,request_with_headers
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--database-port',type=int,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
source=a.source.resolve();root=source.parent
assert source.name=='source' and root.parent in (Path('/tmp'),Path('/private/tmp')) and root.name.startswith('devcoreblog-f17.') and not (source/'.git').exists()
checks={};timings={};proc=None
pg=['psql','-X','-h','127.0.0.1','-p',str(a.database_port),'-U',getpass.getuser()]
env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','LANG','LC_ALL','DOTNET_ROOT')};env['PGPASSFILE']='/dev/null'
def sql(db,query,expect=True):
 r=subprocess.run(pg+['-d',db,'-At','-v','ON_ERROR_STOP=1','-c',query],env=env,capture_output=True,text=True,timeout=20)
 if expect:assert r.returncode==0,'Owned test SQL failed'
 return r
assert Path(sql('postgres','SHOW data_directory').stdout.strip()).resolve()==root/'postgres'
original='devcoreblog_f01_test';restored='f56_restored'
# Include explicit UTC dates, Unicode and media metadata; no provider network calls.
sql(original,'''UPDATE "Posts" SET "ThumbnailUrl"='https://images.example.test/f56/cover.png', "ThumbnailPublicId"='DevCoreBlog/f56-cover', "ThumbnailWidth"=1200, "ThumbnailHeight"=800, "ThumbnailAlt"='Synthetic cover — test', "PublishDate"='2026-09-01T12:34:56Z', "UpdatedDate"='2026-09-02T01:02:03Z' WHERE "Id"=2005;''')
sql(original,'''INSERT INTO "WebhookReceipts" ("Key","PayloadHash","CreatedAt","CreatedPostId","Title","Slug","IsPublished","PublishDate","PostId") VALUES ('f56-synthetic-retry', repeat('a',64), '2026-09-01T12:00:00Z', 2005, 'Synthetic retained reply', 'f01-markdown-xss', true, '2026-09-01T12:34:56Z', 2005);''')
def snapshot(db):
 tables={}
 for table,key in [('Posts','Id'),('Categories','Id'),('__EFMigrationsHistory','MigrationId'),('WebhookReceipts','Key')]:
  text=sql(db,f'SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."{key}"),\'[]\'::jsonb) FROM "{table}" t;').stdout.strip()
  tables[table]=json.loads(text)
 tables['sequences']=sql(db,"SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY sequencename),'[]'::jsonb) FROM pg_sequences s WHERE schemaname='public';").stdout.strip()
 return tables
before=snapshot(original);checks['fixture_has_retained_webhook_reply']=len(before['WebhookReceipts'])==1
checks['fixture_has_visible_draft_future_and_media']=any(x['ThumbnailPublicId']=='DevCoreBlog/f56-cover' for x in before['Posts']) and any(not x['IsPublished'] for x in before['Posts']) and any(x['PublishDate'][:4]=='2026' for x in before['Posts'])
archive=root/'backups/database.dump';tool=source/'scripts/operations/test_database_archive.py'
base=['python3',str(tool),'--fixture-root',str(root),'--port',str(a.database_port)]
def archive_call(action,db,path=archive,digest=None):
 cmd=base+[action,'--database',db,'--archive',str(path)]
 if digest:cmd+=['--sha256',digest]
 return subprocess.run(cmd,env=env,capture_output=True,text=True,timeout=90)
started=time.monotonic();backup=archive_call('backup',original);assert backup.returncode==0;digest=backup.stdout.strip();timings['backup_seconds']=round(time.monotonic()-started,3)
checks['custom_archive_checksum_and_private_permissions']=digest==hashlib.sha256(archive.read_bytes()).hexdigest() and archive.stat().st_mode&0o777==0o600
checks['external_archive_path_denied']=archive_call('restore','f56_external',root/'outside.dump',digest).returncode!=0
checks['repository_root_denied']=subprocess.run(['python3',str(tool),'restore','--fixture-root',str(source),'--port',str(a.database_port),'--database','f56_wrong_root','--archive',str(archive),'--sha256',digest],env=env,capture_output=True,text=True).returncode!=0
checks['source_restore_denied']=archive_call('restore',original,digest=digest).returncode!=0
checks['non_test_database_denied']=archive_call('restore','production',digest=digest).returncode!=0
checks['wrong_checksum_denied']=archive_call('restore','f56_bad_hash',digest='0'*64).returncode!=0
bad=root/'backups/corrupt.dump';bad.write_bytes(b'not a PostgreSQL archive')
checks['corrupt_archive_denied_before_destination_create']=archive_call('restore','f56_corrupt',bad,hashlib.sha256(bad.read_bytes()).hexdigest()).returncode!=0 and sql('postgres',"SELECT count(*) FROM pg_database WHERE datname='f56_corrupt'").stdout.strip()=='0'
started=time.monotonic();restore=archive_call('restore',restored,digest=digest);assert restore.returncode==0;timings['restore_seconds']=round(time.monotonic()-started,3)
after=snapshot(restored)
for table in before:checks[table+'_restored_exactly']=before[table]==after[table]
checks['source_not_modified_by_dump_restore']=snapshot(original)==before
checks['existing_destination_never_overwritten']=archive_call('restore',restored,digest=digest).returncode!=0 and snapshot(restored)==after
# A failed transaction must not trigger a down migration or partially replace data.
failed=sql(restored,'''BEGIN; ALTER TABLE "Posts" ADD COLUMN "F56FailedMigration" text; UPDATE "Posts" SET "Title"='Must roll back'; DO $$ BEGIN RAISE EXCEPTION 'Owned migration failure'; END $$; COMMIT;''',False)
checks['failed_transactional_migration_rolls_back']=failed.returncode!=0 and snapshot(restored)==after and sql(restored,"SELECT count(*) FROM information_schema.columns WHERE table_name='Posts' AND column_name='F56FailedMigration'").stdout.strip()=='0'
# Rebuild the previous committed C# sources, preserving this test's private copied dependencies.
prior=root/'previous-source';shutil.copytree(source,prior,ignore=shutil.ignore_patterns('bin','node_modules','.env','.git'))
repo=Path(os.environ['DEVCORE_F56_REPO']).resolve()
paths=subprocess.check_output(['git','ls-tree','-r','--name-only','HEAD'],cwd=repo,text=True).splitlines()
for name in paths:
 if name.endswith(('.cs','.csproj')) and not any(x in ('bin','obj') for x in Path(name).parts):
  dest=prior/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(subprocess.check_output(['git','show','HEAD:'+name],cwd=repo))
(prior/'F30FixtureStorage.cs').unlink(missing_ok=True)
subprocess.run(['dotnet','build','DevCoreBlog.csproj','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false'],cwd=prior,env=env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=120)
release=root/'release'
subprocess.run(['dotnet','publish','DevCoreBlog.csproj','--no-restore','--no-build','-c','Debug','-o',str(release)],cwd=source,env=env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=90)
checks['published_release_contains_assets']=all((release/x).is_file() for x in ('DevCoreBlog.dll','wwwroot/generated/tailwind-public.css','wwwroot/generated/tailwind-admin.css'))
checks['published_release_contains_no_env_cookie_key_or_node_modules']=not any(p.name.startswith(('.env','cookies')) or p.suffix in ('.pem','.pfx','.key') or 'node_modules' in p.parts for p in release.rglob('*'))
checks['previous_committed_binary_built']=True
password='f56-isolated-password';hashed=subprocess.run(['dotnet','run','--no-build','--project',str(source/'tools/DevCoreBlog.PasswordHashTool'),'--','--stdin'],input=password+'\n',env=env,capture_output=True,text=True,check=True).stdout.strip()
keys=root/'f56-keys';keys.mkdir(mode=0o700)
baseurl='http://127.0.0.1:15197';connection=f'Host=127.0.0.1;Port={a.database_port};Database={restored};Username={getpass.getuser()}'
appenv={**env,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ASPNETCORE_URLS':baseurl,'SITE_URL':'https://blog.example.test','DB_CONNECTION_STRING':connection,'ADMIN_USERNAME':'f56-admin','ADMIN_PASSWORD_HASH':hashed,'DATA_PROTECTION_KEYS_PATH':str(keys),'ADMIN_SESSION_VERSION':'f56-stable'}
def stop():
 global proc
 if proc:
  proc.terminate()
  try:proc.wait(timeout=10)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
  proc=None
try:
 for label,path in [('current',release),('previous',prior)]:
  binary=path/'DevCoreBlog' if label=='current' else path/'bin/Debug/net10.0/DevCoreBlog'
  proc=subprocess.Popen([str(binary)],cwd=path,env=appenv,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  for _ in range(100):
   assert proc.poll() is None,'Owned app exited before ready'
   try:
    with urllib.request.urlopen(baseurl+'/css/error-page.css',timeout=1) as r:
     if r.status==200:break
   except OSError:time.sleep(.1)
  admin,_=cookie_opener();submit_login(admin,baseurl,'f56-admin',password)
  status,_,body,_=request_with_headers(admin,baseurl+'/Admin/Dashboard');checks[label+'_restored_db_dashboard_verified']=status==200 and 'Verified (PostgreSQL)' in body
  status,_,body,_=request_with_headers(admin,baseurl+'/AdminPost/Edit/2005');checks[label+'_media_and_dates_readable']=status==200 and 'Synthetic cover' in body and 'f56/cover.png' in body
  # HEAD avoids the eligible public detail GET counter write.
  for slug,expected in [('f01-markdown-xss',200),('f01-draft',404),('f01-future-visible-marker',404)]:
   r=urllib.request.Request(baseurl+'/post/'+slug,method='HEAD')
   try:
    with urllib.request.urlopen(r,timeout=5) as response:status=response.status
   except urllib.error.HTTPError as response:status=response.code
   checks[label+'_'+slug+'_visibility']=status==expected
  stop()
 checks['binary_smoke_reads_do_not_change_restored_data']=snapshot(restored)==after
finally:stop()
assert all(checks.values()),[k for k,v in checks.items() if not v]
a.report.write_text(json.dumps({'checks':checks,'count':len(checks),'timings':timings,'server_version':sql('postgres','SHOW server_version').stdout.strip(),'row_counts':{k:len(v) for k,v in before.items() if isinstance(v,list)},'migrations':[x['MigrationId'] for x in before['__EFMigrationsHistory']],'previous_code_ref':subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip(),'scope':'Synthetic local PostgreSQL/metadata archive only; no Cloudinary bytes, live host, keyring or secret backup.'},indent=2)+'\n')
print('f56_restore_and_previous_binary_acceptance=true')
