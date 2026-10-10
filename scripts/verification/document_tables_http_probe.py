#!/usr/bin/env python3
"""Table schema/limit and atomic-write acceptance on the owned real MVC/PostgreSQL fixture."""
import argparse, copy, html, json, os, re, subprocess
from pathlib import Path
from urllib.parse import urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers, extract_antiforgery_token
from f22_edit_conflict_probe import field

p = argparse.ArgumentParser()
p.add_argument('--base-url', required=True)
p.add_argument('--database-port', required=True)
p.add_argument('--report', required=True)
a = p.parse_args()
assert urlparse(a.base_url).hostname == '127.0.0.1'
base = a.base_url.rstrip('/')
checks = {}
def sql(query):
    return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', a.database_port,
        '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c', query], text=True).strip()
def snapshot(): return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";')
def envelope(body):
    match = re.search(r'<textarea[^>]*name="DocumentJson"[^>]*>(.*?)</textarea>', body, re.S)
    return html.unescape(match[1]).lstrip('\n') if match else ''
def table(rows=2, columns=2):
    return {'type':'table', 'content':[{'type':'tableRow','content':[
        {'type':'tableHeader' if row == 0 else 'tableCell', 'attrs':{'colspan':1,'rowspan':1,'colwidth':None,'align':['left','center','right'][col%3]},
         'content':[{'type':'paragraph','content':[{'type':'text','text':f'Hücre {row}:{col} 👋'}]}]}
        for col in range(columns)]} for row in range(rows)]}
def doc(tables): return json.dumps({'version':1,'document':{'type':'doc','content':tables}}, ensure_ascii=False)
admin, _ = cookie_opener()
login = submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert login[2] == 200, f'Synthetic admin login status={login[2]}'
create = request_with_headers(admin,base+'/AdminWriting/Create')
common = {'Title':'F10 tablo sınırı','DocumentJson':doc([table()]),'CategoryId':'1001','IsActive':'true',
 'PublishDate':field(create[2],'PublishDate'),'SaveAction':'SaveDraft','__RequestVerificationToken':extract_antiforgery_token(create[2])}

def rejects(name, value, status):
    before=snapshot()
    result=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'DocumentJson':value})
    checks[name]=result[0]==status and snapshot()==before and envelope(result[2])==value and 'no-store' in result[3].get('Cache-Control','')
rejects('row_21_is_413_without_partial_insert',doc([table(21,2)]),413)
rejects('column_11_is_413_without_partial_insert',doc([table(2,11)]),413)
rejects('document_over_1000_cells_is_413',doc([table(20,10) for _ in range(5)]+[table(1,1)]),413)
for key,value in [('colspan',2),('rowspan',2),('colwidth',[120]),('align','justify'),('style','background:red')]:
    node=table();node['content'][0]['content'][0]['attrs'][key]=value
    rejects('invalid_cell_'+key+'_is_400_with_original_retained',doc([node]),400)
node=table();node['content'][0]['content'][0]['content']=[{'type':'blockquote','content':[table(1,1)]}]
rejects('nested_table_inside_quote_in_cell_is_400',doc([node]),400)
node=table();node['content'][1]['content'].pop()
rejects('non_rectangular_table_is_400',doc([node]),400)
valid=doc([table(20,10) for _ in range(5)])
created=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'DocumentJson':valid})
id=int(field(created[2],'Id'))
checks['exact_1000_cells_and_20_by_10_tables_create_and_reopen']=created[0]==200 and envelope(created[2])==valid and sql(f'SELECT "DocumentVersion"=1 AND "EditVersion"=1 AND NOT "IsPublished" AND "DocumentWordCount">0 FROM "Posts" WHERE "Id"={id};')=='t'
url=base+f'/AdminWriting/Edit/{id}'
edit={**common,'Id':str(id),'EditVersion':'1','DocumentJson':doc([table(2,3)]),'SaveAction':'Save',
 '__RequestVerificationToken':extract_antiforgery_token(created[2])}
protected=sql(f'SELECT "Slug","CreatedDate","Content","ViewCount","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id};')
saved=request_with_headers(admin,url,data=edit)
checks['table_edit_commits_same_document_and_derived_facts']=saved[0]==200 and envelope(saved[2])==edit['DocumentJson'] and field(saved[2],'EditVersion')=='2' and sql(f'SELECT "DocumentPlainText" FROM "Posts" WHERE "Id"={id};').count('Hücre')==6
checks['table_edit_preserves_identity_access_counter_and_legacy_fields']=protected==sql(f'SELECT "Slug","CreatedDate","Content","ViewCount","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id};')
before=snapshot()
stale=request_with_headers(admin,url,data={**edit,'Title':'F10 çakışmada korunan tablo','DocumentJson':valid})
checks['table_409_preserves_input_and_expected_revision']=stale[0]==409 and snapshot()==before and envelope(stale[2])==valid and field(stale[2],'EditVersion')=='1'
limit=request_with_headers(admin,url,data={**edit,'EditVersion':'2','DocumentJson':doc([table(21,1)])})
checks['table_edit_413_preserves_saved_row_and_submitted_table']=limit[0]==413 and snapshot()==before and envelope(limit[2])==doc([table(21,1)])
invalid=request_with_headers(admin,url,data={**edit,'EditVersion':'2','CategoryId':'999999'})
checks['table_edit_metadata_400_preserves_saved_row_and_input']=invalid[0]==400 and snapshot()==before and envelope(invalid[2])==edit['DocumentJson']
checks['table_200_400_413_409_keep_strict_csp']=all("style-src-attr 'none'" in r[3].get('Content-Security-Policy','') and 'unsafe-eval' not in r[3].get('Content-Security-Policy','') for r in [created,saved,stale,limit,invalid])
Path(a.report).write_text(json.dumps({'count':len(checks),'checks':checks,'fixturePostId':id,'scope':'Real MVC and owned disposable PostgreSQL. No configured database.'},indent=2)+'\n')
print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}))
assert all(checks.values())
