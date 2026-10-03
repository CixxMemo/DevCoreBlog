#!/usr/bin/env python3
"""Inventory acceptance only against an owned disposable F17 PostgreSQL fixture."""
import argparse, json, os, re, subprocess, time
from html import unescape
from urllib.parse import urlencode, urlparse, parse_qs
from http_probe_support import cookie_opener, request, submit_login, extract_antiforgery_token, extract_hidden_value
from f11_optional_thumbnail_probe import post_fields
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--fixture-root', required=True)
parser.add_argument('--seed', action='store_true')
parser.add_argument('--baseline', action='store_true')
args=parser.parse_args()
root=Path(args.fixture_root).resolve()
assert root.parent==Path('/tmp').resolve() and root.name.startswith('devcoreblog-f17.')
assert (root/'postgres/postmaster.pid').read_text().splitlines()[3]=='55445'
base='http://127.0.0.1:15173'
def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p','55445','-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement]).decode().strip()
if args.seed:
    sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content","ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion")
    SELECT 5000+n,'F41 needle '||lpad(n::text,2,'0'),'f41-needle-'||n,'F41 summary','','',n%4 IN (0,2),
        'F41_PRIVATE_BODY_NEVER_IN_LIST',0,CASE WHEN n%4=2 THEN CURRENT_TIMESTAMP+interval '2 days' ELSE '2000-01-01Z' END,
        CASE WHEN n<=64 THEN 1001 ELSE 1002 END,'2026-09-01Z',n%4<>3,1 FROM generate_series(1,80) n;
    INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content","ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion")
    VALUES (5100,'F41 Literal %_','f41-literal','','','',false,'literal',0,'2000-01-01Z',1001,'2000-01-01Z',true,1);''')
admin,_=cookie_opener()
_,_,status,url,_=submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status==200 and '/Admin/Dashboard' in url
checks={}
def listing(**values):return request(admin,base+'/AdminPost?'+urlencode(values))
def rows(body):return [int(x) for x in re.findall(r'id="post-row-(\d+)"',body)]
filters={'query':'F41 needle','categoryId':1001,'status':'Published','pageSize':10}
s,u,first=listing(**filters)
second=listing(**filters,page=2)[2]
checks['only_one_page']=s==200 and len(rows(first))==10
checks['all_filters_and_total']=rows(first)==list(range(5004,5041,4)) and '16 matching posts' in first
checks['no_overlap_and_stable_secondary_id']=rows(second)==list(range(5044,5065,4)) and not set(rows(first))&set(rows(second))
checks['body_not_transferred']='F41_PRIVATE_BODY_NEVER_IN_LIST' not in first
for key,value in [('query','x'*101),('page',0),('page',1001),('page','bad'),('pageSize',999),('categoryId',0),('status','Bogus'),('status',99)]:
    checks[f'invalid_{key}_{str(value)[:5]}_400']=listing(**{key:value})[0]==400
checks['external_edit_return_rejected']=request(admin,base+'/AdminPost/Edit/5004?'+urlencode({'returnUrl':'https://example.test/'}))[0]==400
if args.baseline:
    print(json.dumps({'baseline':True,'checks':checks},indent=2));assert any(not x for x in checks.values());raise SystemExit(0)
for state,offset in [('Published',0),('Draft',1),('Scheduled',2),('Inactive',3)]:
    body=listing(query='F41 needle',categoryId=1001,status=state,pageSize=50)[2]
    checks['status_'+state]=rows(body)==[5000+n for n in range(1,65) if n%4==offset] and all(x==state.lower() for x in re.findall(r'data-status="(\w+)"',body))
body=listing(query='F41 needle',categoryId=1002,status='Inactive',pageSize=50)[2]
checks['inactive_category_overrides_all_flags']=rows(body)==list(range(5065,5081))
checks['category_name_search']=bool(rows(listing(query='F01 Active Category',pageSize=50)[2]))
checks['literal_wildcards']=rows(listing(query='%_')[2])==[5100]
checks['injection_is_literal']=not rows(listing(query="' OR 1=1--")[2])
checks['default_25']=len(rows(request(admin,base+'/AdminPost')[2]))==25
checks['size_50']=len(rows(listing(pageSize=50)[2]))==50
checks['unknown_category_404']=listing(categoryId=999999)[0]==404
checks['empty_first_page']=listing(query='F41 absent')[0]==200 and 'No posts match these filters' in listing(query='F41 absent')[2]
checks['stale_page_clamps_with_context']=parse_qs(urlparse(listing(**filters,page=999)[1]).query)=={k:[str(v)] for k,v in dict(filters,page=2).items()}
next_link=re.search(r'<a href="([^"]+)"[^>]*>Next',first)
checks['next_preserves_context']=next_link is not None and parse_qs(urlparse(unescape(next_link[1])).query)=={k:[str(v)] for k,v in dict(filters,page=2).items()}
context='/AdminPost?'+urlencode(dict(filters,page=2))
s,u,edit=request(admin,base+'/AdminPost/Edit/5044?'+urlencode({'returnUrl':context}))
checks['edit_hidden_context']=s==200 and extract_hidden_value(edit,'returnUrl')==context and 'Back to filtered posts' in edit
fields=post_fields('F41 needle 44 edited','F41 edited text',post_id=5044)
fields.update(EditVersion=extract_hidden_value(edit,'EditVersion'),__RequestVerificationToken=extract_antiforgery_token(edit),SaveAction='Save',PublishDate=extract_hidden_value(edit,'PublishDate'),returnUrl=context,recoveryRevision='48134f5a-983a-46f6-9400-60c9d535aa00')
s,u,saved=request(admin,base+'/AdminPost/Edit/5044',data=fields)
checks['edit_returns_filters_and_receipt']=s==200 and urlparse(u).path=='/AdminPost' and parse_qs(urlparse(u).query)==parse_qs(urlparse(context).query) and 'post-recovery-receipt' in saved and 'F41 needle 44 edited' in saved
fields['EditVersion']=extract_hidden_value(request(admin,base+'/AdminPost/Edit/5044')[2],'EditVersion')
fields['Title']='F41 invalid return must not save';fields['returnUrl']='//example.test/escape'
checks['external_post_return_rejected_before_write']=request(admin,base+'/AdminPost/Edit/5044',data=fields)[0]==400 and sql('SELECT "Title" FROM "Posts" WHERE "Id"=5044;')=='F41 needle 44 edited'
row=re.search(r'<tr[^>]*id="post-row-5048".*?</tr>',saved,re.S)[0]
token=extract_antiforgery_token(row)
before=sql('SELECT "IsPublished" FROM "Posts" WHERE "Id"=5048;')
for target in ['https://example.test','//example.test','/Account/Logout','/AdminPost%2f..%2fAccount','/AdminPost\nInjected']:
    checks['reject_toggle_return_'+target]=request(admin,base+'/AdminPost/TogglePublish/5048',data={'__RequestVerificationToken':token,'returnUrl':target})[0]==400 and sql('SELECT "IsPublished" FROM "Posts" WHERE "Id"=5048;')==before
checks['toggle_missing_csrf_400']=request(admin,base+'/AdminPost/TogglePublish/5048',data={'returnUrl':context})[0]==400
s,u,body=request(admin,base+'/AdminPost/TogglePublish/5048',data={'__RequestVerificationToken':token,'returnUrl':context})
checks['toggle_returns_context_and_recomputes_filter']=s==200 and parse_qs(urlparse(u).query)==parse_qs(urlparse(context).query) and 5048 not in rows(body) and '15 matching posts' in body
s,u,body=request(admin,base+'/AdminPost/TogglePublish/5048',data={'__RequestVerificationToken':token})
checks['legacy_json_toggle_preserved']=s==200 and json.loads(body)['success'] and json.loads(body)['isPublished']
anon,_=cookie_opener();checks['anonymous_has_no_inventory']=not rows(request(anon,base+'/AdminPost')[2])
# A scheduled boundary uses the same clock/state rule for SQL filters and display.
sql('UPDATE "Posts" SET "PublishDate"=CURRENT_TIMESTAMP+interval \'3 seconds\' WHERE "Id"=5002;')
checks['scheduled_before_boundary']=5002 in rows(listing(query='F41 needle 02',status='Scheduled')[2])
deadline=time.monotonic()+8
while time.monotonic()<deadline:
    body=listing(query='F41 needle 02',status='Published')[2]
    if 5002 in rows(body):break
    time.sleep(.25)
checks['published_at_boundary']=5002 in rows(body) and 'data-status="published"' in body
checks['scheduled_removed_at_boundary']=5002 not in rows(listing(query='F41 needle 02',status='Scheduled')[2])
log=(root/'application.log').read_text()
commands=[]
for block in log.split('info:'):
    if 'SELECT p."Id", p."Title", p."Slug", p."CategoryId"' in block:
        commands.append(block[block.index('SELECT'):])
checks['sql_filtered_count']=any('SELECT count(*)::int' in x and 'CASE' in x and 'ILIKE' in x and '"CategoryId" =' in x for x in log.split('info:'))
checks['sql_narrow_limited_stable']=bool(commands) and any(all(token in x for token in ['ORDER BY','"CreatedDate" DESC','p."Id"','LIMIT','OFFSET']) and '"Content"' not in x for x in commands)
checks['sql_status_and_search_before_materialization']=any('CASE' in x and 'ILIKE' in x and '"CategoryId" =' in x for x in commands)
counts=[x[x.index('SELECT'):] for x in log.split('info:') if 'SELECT count(*)::int' in x and 'CASE' in x and 'ILIKE' in x and '"CategoryId" =' in x]
Path('docs/uygulama-kayitlari/kanit/F41/sql.log').write_text('\n'.join(counts[:1]+commands[:2]))
print(json.dumps({'baseline':False,'checks':checks},indent=2));assert all(checks.values()),checks
