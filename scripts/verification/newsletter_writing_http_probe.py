#!/usr/bin/env python3
"""Actual private MVC newsletter writing against the owned full-migration PostgreSQL fixture."""
import argparse, html, json, os, re, subprocess, urllib.request
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from urllib.parse import urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers, extract_antiforgery_token, is_private_admin_challenge
from f22_edit_conflict_probe import field

parser=argparse.ArgumentParser();parser.add_argument('--base-url',required=True);parser.add_argument('--database-port',required=True);parser.add_argument('--report',required=True)
a=parser.parse_args();base=a.base_url.rstrip('/');assert urlparse(base).hostname=='127.0.0.1'
checks={}
def sql(query):return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',query],text=True).strip()
def snapshot():return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";')
def other():return sql('SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id"; SELECT to_jsonb(r) FROM "WebhookReceipts" r ORDER BY "ContractVersion","Key";')
def private(response):return 'no-store' in response[3].get('Cache-Control','') and response[3].get('X-Robots-Tag')=='noindex, nofollow, noarchive'
def document(body):
 match=re.search(r'<textarea[^>]*name="DocumentJson"[^>]*>(.*?)</textarea>',body,re.S)
 return html.unescape(match[1]).lstrip('\n') if match else ''
admin,cookies=cookie_opener();assert submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2]==200
anon,_=cookie_opener()
def get(path,client=admin):return request_with_headers(client,base+path)
def post(path,data):return request_with_headers(admin,base+path,data=data)
def check(name,value):checks[name]=bool(value)
other_before=other();before=snapshot();creation=get('/AdminNewsletter/Create');listing=get('/AdminNewsletter');token=extract_antiforgery_token(creation[2])
check('private_dedicated_routes_and_navigation',creation[0]==listing[0]==200 and private(creation) and private(listing) and 'href="/AdminNewsletter"' in creation[2] and 'data-recovery-scope="newsletter"' in creation[2] and 'action="/AdminNewsletter/Create"' in creation[2])
check('get_is_readonly_and_no_send_button',snapshot()==before and 'Abonelere gönder' not in creation[2] and '/AdminCampaign/Compose?postId=' in listing[2] and bool(re.search(r'href="/AdminCampaign(?:/Index)?\?postId=[1-9]\d*"', listing[2])) and 'E-posta gönderimi: Başlatılmadı' not in listing[2])
check('anonymous_reads_are_denied',all(is_private_admin_challenge(get(path,anon)) for path in ['/AdminNewsletter','/AdminNewsletter/Create','/AdminNewsletter/Edit/2001']))
body=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'F33_PRIVATE_BODY Türkçe 👋 <script> düz metin'}]}]}},ensure_ascii=False)
common={'Title':'F33 bülten konu başlığı','DocumentJson':body,'CategoryId':'1001','Summary':'F33_PRIVATE_SUMMARY','Excerpt':'','ThumbnailAlt':'','IsActive':'true','PublishDate':field(creation[2],'PublishDate'),'__RequestVerificationToken':token}
no_csrf=post('/AdminNewsletter/Create',{k:v for k,v in common.items() if k!='__RequestVerificationToken'})
check('csrf_400_is_private_and_writes_nothing',no_csrf[0]==400 and private(no_csrf) and snapshot()==before)
invalid=post('/AdminNewsletter/Create',{**common,'DocumentJson':'{','Title':'Korunan bülten başlığı'})
check('invalid_json_retains_form_fields',invalid[0]==400 and private(invalid) and document(invalid[2])=='{' and field(invalid[2],'Title')=='Korunan bülten başlığı' and snapshot()==before)
limited=post('/AdminNewsletter/Create',{**common,'DocumentJson':' '*(1048577-len(body.encode()))+body})
check('json_limit_413_preserves_input_and_database',limited[0]==413 and private(limited) and document(limited[2])==' '*(1048577-len(body.encode()))+body and snapshot()==before)
for name,data in [('summary',{**common,'Summary':'x'*501}),('subject',{**common,'Title':'x'*201}),('subject_header',{**common,'Title':'konu\r\nBcc: test@example.test'}),('category',{**common,'CategoryId':'1002'}),('action',{**common,'SaveAction':'999'})]:
 response=post('/AdminNewsletter/Create',data);check(name+'_invalid_without_partial_write',response[0]==400 and snapshot()==before)
created=post('/AdminNewsletter/Create',{**common,'Id':'2001','EditVersion':'99','ContentKind':'AiNews','AccessScope':'Public','IsNewsletter':'false','IsPublished':'true','ViewCount':'99','Slug':'forged','DocumentPlainText':'forged'})
assert created[0]==200,'newsletter_create_failed'
id=int(field(created[2],'Id'));path='/AdminNewsletter/Edit/'+str(id);slug=sql(f'SELECT "Slug" FROM "Posts" WHERE "Id"={id}')
check('server_owns_newsletter_kind_private_scope_identity_and_default_draft',id!=2001 and created[1]==base+path and sql(f'SELECT "ContentKind"=4 AND "AccessScope"=1 AND NOT "IsPublished" AND "DocumentVersion"=1 AND "ViewCount"=0 AND "EditVersion"=1 FROM "Posts" WHERE "Id"={id}')=='t')
check('committed_header_and_site_status_are_truthful',private(created) and 'Bülten kaydedildi. E-posta gönderilmedi.' in html.unescape(created[2]) and 'Site yayını:' in created[2] and 'Taslak' in html.unescape(created[2]) and document(created[2])==body)
before=snapshot();checks['wrong_article_route_cannot_read_newsletter']=get('/AdminWriting/Edit/'+str(id))[0]==404
form={**common,'Id':str(id),'EditVersion':'1','SaveAction':'Save'}
check('wrong_article_route_cannot_write_newsletter',post('/AdminWriting/Edit/'+str(id),form)[0]==404 and snapshot()==before)
check('newsletter_route_cannot_adopt_other_post_identity',get('/AdminNewsletter/Edit/2001')[0]==404 and post('/AdminNewsletter/Edit/2001',{**form,'Id':'2001'})[0]==404 and snapshot()==before)
check('old_admin_edit_link_enters_newsletter_writer',get('/AdminPost/Edit/'+str(id))[1]==base+path)
check('unsafe_return_is_rejected',post(path+'?returnUrl=https://example.test',form)[0]==400 and snapshot()==before)
site=timezone(timedelta(hours=3));future=(datetime.now(site)+timedelta(days=2)).replace(microsecond=0)
scheduled=post(path,{**form,'SaveAction':'Schedule','PublishDate':future.isoformat(timespec='seconds')[:19]})
check('future_site_schedule_stores_utc_and_displays_scheduled',scheduled[0]==200 and 'Planlandı' in html.unescape(scheduled[2]) and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id}'))==future.astimezone(timezone.utc))
protected=sql(f'SELECT "Slug","CreatedDate","Content","ThumbnailUrl","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id}')
sql(f'UPDATE "Posts" SET "ViewCount"="ViewCount"+7 WHERE "Id"={id}')
kept=post(path,{**form,'EditVersion':'2','PublishDate':'2020-01-01T00:00:00','Title':'F33 bülten değiştirilmiş konu'})
check('normal_save_preserves_schedule_counter_slug_and_access',kept[0]==200 and 'Planlandı' in html.unescape(kept[2]) and sql(f'SELECT "ViewCount"=7 AND "IsPublished" FROM "Posts" WHERE "Id"={id}')=='t' and protected==sql(f'SELECT "Slug","CreatedDate","Content","ThumbnailUrl","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id}') and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id}'))==future.astimezone(timezone.utc))
now=datetime.now(timezone.utc);published=post(path,{**form,'EditVersion':'3','SaveAction':'Publish','PublishDate':future.isoformat(timespec='seconds')[:19]})
date=datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id}'))
check('publish_now_uses_server_clock_without_sending',published[0]==200 and 'Yayımlandı' in html.unescape(published[2]) and now<=date<=datetime.now(timezone.utc) and other()==other_before)
# Authenticated preview snapshots have no persistence or send side effects.
before=snapshot()
for route in ['/AdminDocument/Preview','/AdminEmail/Preview']:
 response=post(route,{'Input.DocumentJson':body,'Input.FromEditor':'true','__RequestVerificationToken':token})
 check(route.split('/')[1]+'_snapshot_is_private_and_readonly',response[0]==200 and private(response) and snapshot()==before and 'F33_PRIVATE_BODY' in response[2])
public_paths=['/post/'+slug,'/','/ara?query=F33_PRIVATE','/category/f01-active','/sitemap.xml','/rss.xml','/api/public/posts/latest']
public=[get(route,anon) for route in public_paths]
check('published_newsletter_remains_absent_from_every_public_surface',public[0][0]==404 and all('F33_PRIVATE_BODY' not in response[2] and 'F33_PRIVATE_SUMMARY' not in response[2] and slug not in response[2] for response in public))
check('public_reads_do_not_increment_newsletter_counter',sql(f'SELECT "ViewCount" FROM "Posts" WHERE "Id"={id}')=='7')
before=snapshot();stale=post(path,{**form,'EditVersion':'3','Title':'Stale korunacak konu','DocumentJson':body.replace('Türkçe','Kaybolmayan')})
check('stale_409_retains_submitted_json_metadata_revision',stale[0]==409 and private(stale) and field(stale[2],'Title')=='Stale korunacak konu' and field(stale[2],'EditVersion')=='3' and 'Kaybolmayan' in document(stale[2]) and snapshot()==before)
current={**form,'EditVersion':'4','PublishDate':field(published[2],'PublishDate')}
def race(title):
 worker=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
 return request_with_headers(worker,base+path,data={**current,'Title':title})[0]
with ThreadPoolExecutor(max_workers=2) as pool:statuses=list(pool.map(race,['F33 race A','F33 race B']))
check('parallel_edits_have_one_commit_one_conflict',sorted(statuses)==[200,409] and sql(f'SELECT "EditVersion" FROM "Posts" WHERE "Id"={id}')=='5')
fresh=get(path);before=snapshot()
check('past_schedule_is_rejected_without_write',post(path,{**current,'EditVersion':'5','SaveAction':'Schedule','PublishDate':'2020-01-01T00:00:00'})[0]==400 and snapshot()==before)
# Force a real PostgreSQL write failure, retaining posted values in the rendered 503 form.
sql(f'''CREATE FUNCTION f33_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Id"={id} THEN RAISE EXCEPTION 'synthetic write failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER f33_fail BEFORE UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION f33_fail();''')
try:
 failure=post(path,{**current,'EditVersion':'5','Title':'503 korunacak konu'})
 check('real_db_failure_is_503_with_retained_input',failure[0]==503 and private(failure) and field(failure[2],'Title')=='503 korunacak konu' and document(failure[2])==body and snapshot()==before)
finally:sql('DROP TRIGGER f33_fail ON "Posts"; DROP FUNCTION f33_fail();')
draft=post(path,{**current,'EditVersion':'5','SaveAction':'SaveDraft'})
check('save_draft_revokes_site_publication_preserves_exact_date',draft[0]==200 and 'Taslak' in html.unescape(draft[2]) and sql(f'SELECT NOT "IsPublished" FROM "Posts" WHERE "Id"={id}')=='t' and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id}'))==date)
check('invalid_paging_is_400_and_private',all(get('/AdminNewsletter?page='+page)[0]==400 and private(get('/AdminNewsletter?page='+page)) for page in ['0','1001','bad']))
# Inventory is a 25-row metadata projection; duplicates are test-only SQL data, never external sends.
sql(f'''INSERT INTO "Posts" ("Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope") SELECT 'F33 page '||n,'f33-page-{id}-'||n,'Summary','','',1001,'2020-01-01Z',true,false,'2020-01-01Z','',0,1,4,1 FROM generate_series(1,26) n;''')
first=get('/AdminNewsletter');second=get('/AdminNewsletter?page=2')
links=lambda response:set(re.findall(r'href="/AdminNewsletter/Edit/(\d+)"',response[2]))
check('inventory_paging_is_bounded_disjoint_and_private',first[0]==second[0]==200 and private(first) and private(second) and len(links(first))==25 and len(links(second))>=2 and not links(first)&links(second) and 'F33_PRIVATE_BODY' not in first[2]+second[2])
check('inventory_does_not_mix_public_articles',not links(first)&set(sql('SELECT "Id" FROM "Posts" WHERE "ContentKind"<>4').splitlines()))
sql("INSERT INTO \"Categories\" (\"Name\",\"Slug\",\"CreatedDate\",\"IsActive\",\"EditVersion\") SELECT 'F33 bound '||n,'f33-bound-"+str(id)+"-'||n,'2020-01-01Z',true,1 FROM generate_series(1,257) n")
try:
 before=snapshot();bounded=get('/AdminNewsletter/Create');rejected=post('/AdminNewsletter/Create',common)
 check('bounded_category_options_fail_closed_even_for_direct_post',bounded[0]==503 and rejected[0]==503 and private(bounded) and private(rejected) and 'data-editor-enabled="false"' in bounded[2] and snapshot()==before)
finally:sql("DELETE FROM \"Categories\" WHERE \"Slug\" LIKE 'f33-bound-"+str(id)+"-%'")
check('no_subscriber_receipt_or_send_state_mutations' ,other()==other_before)
check('strict_csp_on_newsletter_form_errors',all("style-src-attr 'none'" in response[3].get('Content-Security-Policy','') and 'unsafe-eval' not in response[3].get('Content-Security-Policy','') for response in [creation,created,invalid,stale,failure]))
report={'passed':all(checks.values()),'count':len(checks),'checks':checks,'fixturePostId':id,'concurrentStatuses':statuses,'scope':'Actual MVC/PostgreSQL; no email provider, account or production data'}
Path(a.report).write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}));assert report['passed']
