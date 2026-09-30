#!/usr/bin/env python3
"""Reject invalid public origins before serving requests, using an isolated source copy."""
import argparse
import os
import subprocess
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--source', required=True)
args=parser.parse_args()
source=Path(args.source)
if not str(source).startswith('/tmp/devcoreblog-f17.') or (source/'.env').read_text()!='':
    raise SystemExit('Only the disposable fixture with an empty .env is allowed.')
base={key:os.environ[key] for key in ['PATH','HOME','TMPDIR','LANG'] if key in os.environ}
base.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',
    DB_CONNECTION_STRING='Host=127.0.0.1;Port=1;Database=f29_configuration;Username=f29_fixture',
    ADMIN_USERNAME='',ADMIN_PASSWORD_HASH='',CLOUDINARY_CLOUD_NAME='f29-cloud',
    CLOUDINARY_API_KEY='f29-key',CLOUDINARY_API_SECRET='f29-fixture',
    SITE_URL='https://blog.example.test', PORTFOLIO_CORS_ORIGIN='')
cases=[
 ('production_missing_site',None,'Production','SITE_URL must'),
 ('production_http_site','http://localhost:5000','Production','SITE_URL must'),
 ('site_path','https://blog.example.test/path','Development','SITE_URL must'),
 ('site_query','https://blog.example.test/?q=1','Development','SITE_URL must'),
 ('site_fragment','https://blog.example.test/#x','Development','SITE_URL must'),
 ('site_credentials','https://fixture:fixture@example.test','Development','SITE_URL must'),
 ('site_bad_scheme','javascript:alert(1)','Development','SITE_URL must'),
 ('site_remote_http','http://blog.example.test','Development','SITE_URL must'),
 ('cors_wildcard','*','Development','PORTFOLIO_CORS_ORIGIN must'),
 ('cors_path','https://portfolio.example.test/path','Development','PORTFOLIO_CORS_ORIGIN must'),
]
passed=True
for name,value,environment,message in cases:
    env=dict(base)
    env['ASPNETCORE_ENVIRONMENT']=env['DOTNET_ENVIRONMENT']=environment
    key='PORTFOLIO_CORS_ORIGIN' if name.startswith('cors') else 'SITE_URL'
    if value is None: env.pop(key,None)
    else: env[key]=value
    result=subprocess.run(['dotnet',str(source/'bin/Debug/net10.0/DevCoreBlog.dll')],
        cwd=source,env=env,capture_output=True,text=True,timeout=15)
    check=result.returncode!=0 and message in result.stdout+result.stderr
    print('f29_'+name+'_rejected='+str(check).lower(),flush=True)
    passed=passed and check
raise SystemExit(0 if passed else 1)
