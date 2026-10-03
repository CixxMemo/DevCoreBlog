#!/usr/bin/env python3
"""Check English site dates and unchanged UTC form values in the owned F17 fixture."""
import argparse
import html
import json
import os
from pathlib import Path
import re
import subprocess
from urllib.parse import urlencode
from http_probe_support import cookie_opener, submit_login, request, extract_antiforgery_token, extract_hidden_value
from f11_optional_thumbnail_probe import post_fields

parser = argparse.ArgumentParser()
parser.add_argument('--fixture-root', required=True)
parser.add_argument('--base-url', required=True)
parser.add_argument('--pg-port', required=True)
args = parser.parse_args()
root = Path(args.fixture_root).resolve()
assert str(root).startswith('/private/tmp/devcoreblog-f17.') and (root/'postgres/PG_VERSION').exists(), 'Owned fixture required'
base = args.base_url.rstrip('/')
assert (base,args.pg_port) in [('http://127.0.0.1:15175','55447'),('http://127.0.0.1:15176','55448')], 'Use the named F42 fixture ports'
def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',args.pg_port,'-U',os.environ['USER'],'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement]).decode().strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2006') == 'f01-turkce-karakterler'
sql('UPDATE "Posts" SET "PublishDate"=\'2026-09-30 22:30:15+00\', "CreatedDate"=\'2026-09-30 22:30:15+00\' WHERE "Id"=2006')
before = sql('SELECT "PublishDate", "CreatedDate", "Title", "Content", "EditVersion" FROM "Posts" WHERE "Id"=2006')
admin,_ = cookie_opener()
_,_,status,url,_ = submit_login(admin,base,'f17-admin',os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url
visitor,_ = cookie_opener()
checks={}
for name,path in [('home','/?pageSize=18'),('search','/ara?' + urlencode({'query':'IĞDIR'})),('category','/kategori/f01-active?pageSize=18'),('detail','/post/f01-turkce-karakterler')]:
    status,_,body=request(visitor,base+path)
    checks[name+'_English_shell'] = status==200 and '<html lang="en"' in body
    checks[name+'_site_date'] = 'Oct 01, 2026' in body
    if name=='detail':
        checks['machine_time_remains_UTC'] = '2026-09-30T22:30:15.0000000Z' in body
        checks['Turkish_article_is_not_translated'] = 'Türkçe arama örneği' in html.unescape(body)
        checks['unknown_article_language_is_not_invented'] = re.search(r'<article\b[^>]*\blang=',body) is None
_,_,index=request(admin,base+'/AdminPost?' + urlencode({'query':'IĞDIR'}))
checks['admin_created_date_uses_same_site_date'] = 'Oct 01, 2026' in index
_,_,edit=request(admin,base+'/AdminPost/Edit/2006')
checks['form_value_keeps_site_datetime_seconds'] = extract_hidden_value(edit,'PublishDate') == '2026-10-01T01:30:15'
checks['recovery_receives_site_timezone'] = 'data-site-time-zone="Europe/Istanbul"' in edit
fields=post_fields('', '', post_id=2006)
fields.update(__RequestVerificationToken=extract_antiforgery_token(edit),EditVersion=extract_hidden_value(edit,'EditVersion'),SaveAction='Save',CategoryId='0',PublishDate='invalid')
status,_,invalid=request(admin,base+'/AdminPost/Edit/2006',data=fields)
checks['server_validation_stays_English'] = status==200 and 'Title is required.' in invalid and 'Content is required.' in invalid and 'Select an active category.' in invalid
checks['invalid_date_binding_is_English'] = 'is not valid for PublishDate' in invalid
checks['date_display_and_invalid_save_keep_stored_fields'] = before == sql('SELECT "PublishDate", "CreatedDate", "Title", "Content", "EditVersion" FROM "Posts" WHERE "Id"=2006')
print(json.dumps({'checks':checks,'count':len(checks)},indent=2))
assert all(checks.values()), 'Failed: '+', '.join(k for k,v in checks.items() if not v)
