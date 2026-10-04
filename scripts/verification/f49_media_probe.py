#!/usr/bin/env python3
"""F49 acceptance against the disposable F17/F49 fixture, never a real provider account."""
import argparse, base64, html, json, os, re, subprocess
from html.parser import HTMLParser
from pathlib import Path
from http_probe_support import cookie_opener, request, submit_login, extract_antiforgery_token, extract_hidden_value, multipart_payload

p = argparse.ArgumentParser()
p.add_argument('--source', type=Path, required=True)
p.add_argument('--pg-user', required=True)
p.add_argument('--report', type=Path, required=True)
a = p.parse_args()
assert a.source.name == 'source' and 'devcoreblog-f17.' in str(a.source) and not (a.source / '.git').exists()
base = 'http://127.0.0.1:15183'
checks = {}
def sql(text):
    return subprocess.run(['psql','-X','-h','127.0.0.1','-p','55450','-U',a.pg_user,'-d','devcoreblog_f01_test',
        '-At','-v','ON_ERROR_STOP=1','-c',text], check=True, capture_output=True, text=True).stdout.strip()
def cover(id):
    return json.loads(sql(f'SELECT json_build_array("ThumbnailUrl","ThumbnailPublicId","ThumbnailWidth","ThumbnailHeight","ThumbnailAlt","EditVersion","UpdatedDate") FROM "Posts" WHERE "Id"={id}'))
admin,_ = cookie_opener()
_,_,s,u,_ = submit_login(admin,base,'f17-admin',os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert s == 200 and '/Admin/Dashboard' in u
png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
gif = base64.b64decode('R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==')
def save(id=None, image=None, **changes):
    path = '/AdminPost/Create' if id is None else f'/AdminPost/Edit/{id}'
    _,_,form = request(admin,base+path)
    fields={'Title':'F49 Cover','Content':'F49 media content','CategoryId':'1001','Summary':'F49', 'Excerpt':'',
        'PublishDate':extract_hidden_value(form,'PublishDate') or '2026-10-04T12:00:00',
        'IsActive':'true','SaveAction':'Publish' if id is None else 'Save',
        '__RequestVerificationToken':extract_antiforgery_token(form),
        'ThumbnailAlt':'A real cover description'}
    if id: fields.update(Id=str(id),EditVersion=extract_hidden_value(form,'EditVersion'))
    fields.update(changes)
    if image:
        data,kind=multipart_payload(fields,file_field='thumbnailFile',file_name='cover.'+image,
            content_type='image/'+image,content=png if image=='png' else gif)
        return request(admin,base+path,raw_data=data,headers={'Content-Type':kind})
    return request(admin,base+path,data=fields)

legacy=cover(2003)
s,u,b=save(2003,'gif',Title='F49 failed upload')
checks['failed_upload_preserves_legacy_cover_and_form_text'] = s==200 and '/Edit/2003' in u and legacy==cover(2003) and legacy[0] in b and 'F49 failed upload' in b
s,u,b=save(2003,ThumbnailPublicId='forged',ThumbnailWidth='99',ThumbnailUrl='https://evil.example/a.png')
checks['legacy_cover_works_and_client_cannot_set_metadata'] = u.endswith('/AdminPost') and cover(2003)[:4]==legacy[:4]
alt='A diagram " onerror="window.__f49Xss=true <script>'
s,u,b=save(image='png',ThumbnailAlt=alt)
id=int(sql('SELECT "Id" FROM "Posts" WHERE "Title"=\'F49 Cover\' AND "ThumbnailPublicId" IS NOT NULL ORDER BY "Id" DESC LIMIT 1'))
first=cover(id)
checks['new_cover_has_exact_provider_metadata'] = u.endswith('/AdminPost') and first[1].startswith('DevCoreBlog/f49-') and first[0]=='https://images.example.test/'+first[1]+'.png' and first[2:4]==[1,1] and first[4]==alt
class Images(HTMLParser):
    def __init__(self): super().__init__(); self.images=[]
    def handle_starttag(self,t,attrs):
        if t=='img': self.images.append(dict(attrs))
_,_,b=request(admin,base+'/post/f49-cover')
parsed=Images();parsed.feed(b)
checks['detail_description_is_encoded'] = any(i.get('src')==first[0] and i.get('alt')==alt and 'onerror' not in i for i in parsed.images)
_,_,form=request(admin,base+f'/AdminPost/Edit/{id}')
s,_,b=request(admin,base+'/AdminPost/Preview',data={'Id':str(id),'Title':'F49 preview','Content':'Preview body','CategoryId':'1001','PublishDate':extract_hidden_value(form,'PublishDate'),'ThumbnailAlt':alt,'__RequestVerificationToken':extract_antiforgery_token(form)})
parsed=Images();parsed.feed(b)
checks['preview_description_encoded_without_cover_write'] = s==200 and any(i.get('alt')==alt and 'onerror' not in i for i in parsed.images) and cover(id)==first
s,u,b=save(id,ThumbnailAlt='')
second=cover(id)
checks['decorative_empty_description_and_metadata_preservation'] = second[:4]==first[:4] and second[4] is None and second[6] is not None
s,u,b=save(id,'png')
replacement=cover(id)
checks['replacement_commits_new_identity'] = replacement[1]!=first[1] and replacement[0]!=first[0]
# Keep the previous cover in another draft's Markdown (including encoded URL presentation).
url=first[0].replace('/','%2F')
sql('UPDATE "Posts" SET "Content"=\'![shared]( '+url+' )\' WHERE "Id"=2004')
previous=cover(id)
journal=a.source/'bin/Debug/net10.0/f49-provider-journal.jsonl'
provider_before=journal.read_text()
try:
    sql('CREATE FUNCTION f49_reject_save() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Title"=\'F49_DB_FAIL\' THEN RAISE EXCEPTION \'synthetic persistence failure\'; END IF; RETURN NEW; END $$; CREATE TRIGGER f49_reject_save BEFORE UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION f49_reject_save();')
    s,u,b=save(id,'png',Title='F49_DB_FAIL')
    checks['db_failure_keeps_persisted_cover'] = s==500 and cover(id)==previous
    checks['db_failure_leaves_uploaded_asset_for_review'] = len(journal.read_text().splitlines())==len(provider_before.splitlines())+1
finally:
    sql('DROP TRIGGER f49_reject_save ON "Posts"; DROP FUNCTION f49_reject_save();')
try:
    sql('CREATE FUNCTION f49_reject_save() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Title"=\'F49_LATE_CONFLICT\' THEN RETURN NULL; END IF; RETURN NEW; END $$; CREATE TRIGGER f49_reject_save BEFORE UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION f49_reject_save();')
    s,u,b=save(id,'png',Title='F49_LATE_CONFLICT')
    checks['late_conflict_shows_committed_cover_and_preserves_input'] = s==409 and previous[0] in b and 'F49_LATE_CONFLICT' in b and cover(id)==previous
finally:
    sql('DROP TRIGGER f49_reject_save ON "Posts"; DROP FUNCTION f49_reject_save();')
provider_before=journal.read_text()
s,u,b=save(id,'png',ThumbnailAlt='x'*301)
checks['description_limit_rejects_without_changing_cover'] = s==200 and '/Edit/' in u and cover(id)==previous and 'Cover description cannot exceed' in b and journal.read_text()==provider_before
# Explicit provider export covers referenced cover, encoded Markdown in another post and absent asset.
assets=[json.loads(line) for line in journal.read_text().splitlines()]
assets.append({'publicId':'DevCoreBlog/f49-unused','urls':['https://images.example.test/DevCoreBlog/f49-unused.png']})
manifest=a.source.parent/'f49-provider-export.json';manifest.write_text(json.dumps(assets))
before=sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"')
env={**os.environ,'MEDIA_INVENTORY_CONNECTION':f'Host=127.0.0.1;Port=55450;Database=devcoreblog_f01_test;Username={a.pg_user}'}
run=subprocess.run(['dotnet','run','--no-build','--project','tools/DevCoreBlog.MediaInventoryTool','--',str(manifest)],env=env,check=True,capture_output=True,text=True)
report=json.loads(run.stdout)
checks['dry_run_checks_other_hidden_posts_and_encoded_markdown'] = report['assets'][0]['status']=='Referenced' and 2004 in report['assets'][0]['samplePostIds'] and report['assets'][1]['status']=='Referenced'
checks['dry_run_absent_asset_is_not_deletion_permission'] = report['dryRun'] and all(asset['status']=='No stored reference found' for asset in report['assets'][2:]) and 'No deletion permission' in report['warning']
checks['dry_run_changes_no_provider_assets'] = journal.read_text()==provider_before
checks['dry_run_changes_no_database_rows'] = before==sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"')
a.report.write_text(json.dumps({'checks':checks,'inventory':report},indent=2))
print(json.dumps(checks,indent=2))
assert all(checks.values())
