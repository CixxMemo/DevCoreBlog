#!/usr/bin/env python3
"""Real MVC/owned PostgreSQL image writes and write-failure retention; no provider upload claim."""
import argparse, html, json, os, re, subprocess
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
def document(attrs=None, count=1):
    return json.dumps({'version':1,'document':{'type':'doc','content':[
        {'type':'paragraph','content':[{'type':'text','text':'Korunan Türkçe metin 👋'}]},
        *[{'type':'image','attrs':attrs or {'src':'https://example.test/synthetic.gif','alt':'Türkçe <script> " 👋','title':'Başlık'}} for _ in range(count)]
    ]}},ensure_ascii=False)
admin, _ = cookie_opener()
assert submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2] == 200
create = request_with_headers(admin,base+'/AdminWriting/Create')
common={'Title':'F12 HTTP görsel belgesi','CategoryId':'1001','IsActive':'true','SaveAction':'SaveDraft',
    'PublishDate':field(create[2],'PublishDate'),'__RequestVerificationToken':extract_antiforgery_token(create[2])}
before=snapshot()
cases=[('http',{'src':'http://example.test/p.png'},400),('data',{'src':'data:image/png;base64,AAAA'},400),
    ('javascript',{'src':'javascript:alert(1)'},400),('credential',{'src':'https://user:pass@example.test/p.png'},400),
    ('encoded_control',{'src':'https://example.test/%0a.png'},400),('event',{'src':'https://example.test/p.png','onerror':'alert(1)'},400),
    ('resize',{'src':'https://example.test/p.png','width':20},400),('forged_provider_id',{'src':'https://example.test/p.png','publicId':'forged'},400),
    ('alt_limit',{'src':'https://example.test/p.png','alt':'👋'*301},413)]
for name,attrs,status in cases:
    doc=document(attrs)
    response=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'DocumentJson':doc})
    checks[name+'_rejected_without_write_and_input_retained']=response[0]==status and envelope(response[2])==doc and snapshot()==before
doc=document(count=51)
limited=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'DocumentJson':doc})
checks['fifty_one_images_413_retains_input']=limited[0]==413 and envelope(limited[2])==doc and snapshot()==before
doc=document({'src':'https://example.test/synthetic.gif','alt':'👋'*300,'title':None,'width':None,'height':None},50)
saved=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'DocumentJson':doc})
id=int(field(saved[2],'Id'))
checks['fifty_images_and_three_hundred_runes_save_and_reopen']=saved[0]==200 and envelope(saved[2])==doc and id>0
edit_url=base+f'/AdminWriting/Edit/{id}'
edit={**common,'Id':str(id),'EditVersion':field(saved[2],'EditVersion'),'SaveAction':'Save',
    '__RequestVerificationToken':extract_antiforgery_token(saved[2]),'DocumentJson':document()}
original=snapshot()
sql('''CREATE FUNCTION f12_refuse_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
    IF NEW."Title" LIKE 'F12 rejected%' THEN RAISE EXCEPTION 'synthetic write refusal'; END IF;
    RETURN NEW; END $$;
    CREATE TRIGGER f12_refuse_write BEFORE INSERT OR UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION f12_refuse_write();''')
try:
    failed=request_with_headers(admin,edit_url,data={**edit,'Title':'F12 rejected update'})
    checks['real_database_update_failure_503_retains_json_revision_and_row']=failed[0]==503 and envelope(failed[2])==edit['DocumentJson'] and field(failed[2],'EditVersion')==edit['EditVersion'] and snapshot()==original
    failed_create=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'Title':'F12 rejected create','DocumentJson':document()})
    checks['real_database_insert_failure_503_retains_input_without_partial_row']=failed_create[0]==503 and envelope(failed_create[2])==document() and snapshot()==original
    checks['storage_errors_are_private_and_hide_provider_diagnostics']=all('no-store' in r[3].get('Cache-Control','') and 'synthetic write refusal' not in r[2] and 'Npgsql' not in r[2] and "style-src-attr 'none'" in r[3].get('Content-Security-Policy','') for r in (failed,failed_create))
finally:
    sql('DROP TRIGGER f12_refuse_write ON "Posts"; DROP FUNCTION f12_refuse_write();')
success=request_with_headers(admin,edit_url,data=edit)
checks['retry_after_real_write_failure_succeeds_with_same_revision']=success[0]==200 and envelope(success[2])==edit['DocumentJson'] and int(field(success[2],'EditVersion'))==int(edit['EditVersion'])+1
preview_form=request_with_headers(admin,base+'/AdminDocument/Preview')
preview=request_with_headers(admin,base+'/AdminDocument/Preview',data={'Input.DocumentJson':document(),'__RequestVerificationToken':extract_antiforgery_token(preview_form[2])})
image_tag=re.search(r'<img[^>]+alt="([^"]*)"[^>]*>',preview[2])
checks['safe_web_preview_encodes_image_labels']=preview[0]==200 and image_tag is not None and html.unescape(image_tag[1])=='Türkçe <script> " 👋' and '<script>' not in image_tag[0]
Path(a.report).write_text(json.dumps({'checks':checks,'count':len(checks),'scope':'Real MVC/owned PostgreSQL and forced database write failure. HTTPS references only; no actual Cloudinary upload or delivery proof.'},ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}))
assert all(checks.values())
