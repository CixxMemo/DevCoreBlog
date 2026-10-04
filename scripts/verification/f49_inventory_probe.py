#!/usr/bin/env python3
"""Probe conservative inventory classification and failure exits in the owned F49 cluster."""
import argparse, json, os, subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--pg-user',required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
assert a.source.name=='source' and 'devcoreblog-f17.' in str(a.source) and not (a.source/'.git').exists()
def sql(statement):
    return subprocess.run(['psql','-X','-h','127.0.0.1','-p','55450','-U',a.pg_user,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement],capture_output=True,text=True,check=True).stdout.strip()
# Only synthetic fixture data; a delivery transformation must not be treated as definitely unused.
sql('UPDATE "Posts" SET "Content"=\'![transform](https://res.cloudinary.com/test/image/upload/c_fill/DevCoreBlog/f49-transform.png)\' WHERE "Id"=2002')
legacy=sql('SELECT "ThumbnailUrl" FROM "Posts" WHERE "Id"=2003')
assets=[{'publicId':'DevCoreBlog/f49-transform','urls':['https://res.cloudinary.com/test/image/upload/DevCoreBlog/f49-transform.png']},
        {'publicId':'explicit-provider-legacy-id','urls':[legacy]}]
manifest=a.source.parent/'f49-inventory-input.json'
base_env={**os.environ,'MEDIA_INVENTORY_CONNECTION':f'Host=127.0.0.1;Port=55450;Database=devcoreblog_f01_test;Username={a.pg_user}'}
def run(data,env=base_env):
    manifest.write_text(json.dumps(data))
    return subprocess.run(['dotnet','run','--no-build','--project','tools/DevCoreBlog.MediaInventoryTool','--',str(manifest)],env=env,capture_output=True,text=True)
result=run(assets); assert result.returncode==0
report=json.loads(result.stdout)
checks={'transformed_url_requires_review':report['assets'][0]['status']=='Review required' and 2002 in report['assets'][0]['samplePostIds'],
        'legacy_explicit_alias_protects_without_inventing_stored_id':report['assets'][1]['status']=='Referenced' and sql('SELECT "ThumbnailPublicId" IS NULL FROM "Posts" WHERE "Id"=2003')=='t'}
for name,data in [('duplicate_identity',[assets[0],assets[0]]),('missing_alias',[{'publicId':'x'}]),
                 ('unsafe_url',[{'publicId':'x','urls':['https://user:placeholder@example.test/a.png']}]),('oversized_asset_count',[assets[0]]*1001)]:
    result=run(data);checks[name+'_fails_without_report']=result.returncode==1 and not result.stdout.strip()
# Read-only DB credentials must be enough; no migration or write permission is required.
sql('CREATE ROLE f49_inventory_readonly LOGIN; GRANT CONNECT ON DATABASE devcoreblog_f01_test TO f49_inventory_readonly; GRANT USAGE ON SCHEMA public TO f49_inventory_readonly; GRANT SELECT ON "Posts" TO f49_inventory_readonly;')
readonly_env={**base_env,'MEDIA_INVENTORY_CONNECTION':'Host=127.0.0.1;Port=55450;Database=devcoreblog_f01_test;Username=f49_inventory_readonly'}
result=run(assets,readonly_env)
checks['select_only_role_produces_inventory']=result.returncode==0 and json.loads(result.stdout)['dryRun']
checks['role_has_no_post_write_permission']=sql("SELECT has_table_privilege('f49_inventory_readonly','\"Posts\"','INSERT,UPDATE,DELETE')")=='f'
a.report.write_text(json.dumps({'checks':checks,'classification':report},indent=2))
print(json.dumps(checks,indent=2));assert all(checks.values())
