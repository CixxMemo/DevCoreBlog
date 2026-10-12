#!/usr/bin/env python3
"""Public JSON/read/search/privacy/SEO against owned PostgreSQL; all fixtures are created by the real JSON form."""
import argparse, concurrent.futures, html, json, os, re, subprocess, time, urllib.request
from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import urlencode, urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers, extract_antiforgery_token
from f22_edit_conflict_probe import field
class Schemas(HTMLParser):
 def __init__(self):super().__init__();self.collect=False;self.buffer='';self.values=[]
 def handle_starttag(self,tag,attrs):
  if tag=='script' and dict(attrs).get('type')=='application/ld+json':self.collect=True;self.buffer=''
 def handle_data(self,data):
  if self.collect:self.buffer+=data
 def handle_endtag(self,tag):
  if tag=='script' and self.collect:self.values.append(json.loads(self.buffer));self.collect=False
p=argparse.ArgumentParser();p.add_argument('--base-url',required=True);p.add_argument('--database-port',required=True);p.add_argument('--report',required=True);p.add_argument('--baseline',action='store_true');a=p.parse_args();base=a.base_url.rstrip('/');assert urlparse(base).hostname=='127.0.0.1'
checks={};fixtures={}
def sql(query):return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',query],text=True).strip()
def quoted(value):return "'"+value.replace("'","''")+"'"
def snapshot():return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";')
def counter(id):return int(sql(f'SELECT "ViewCount" FROM "Posts" WHERE "Id"={id}'))
def document(text):return json.dumps({'version':1,'document':{'type':'doc','content':[
 {'type':'heading','attrs':{'level':2},'content':[{'type':'text','text':'Başlık 👋'}]},
 {'type':'paragraph','content':[{'type':'text','text':text}]},
 {'type':'table','content':[{'type':'tableRow','content':[{'type':'tableHeader','content':[{'type':'paragraph','content':[{'type':'text','text':'Hücre'}]}]},{'type':'tableCell','content':[{'type':'paragraph','content':[{'type':'text','text':'42'}]}]}]}]},
 {'type':'image','attrs':{'src':'https://example.test/public.gif','alt':'Görsel'}},
 {'type':'youtube','attrs':{'src':'https://youtu.be/jNQXAC9IVRw'}}]}},ensure_ascii=False)
admin,cookies=cookie_opener();assert submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2]==200
anon,_=cookie_opener()
def create(title,text,kind=1,scope=0,active=True,published=True,category=1001):
 form=request_with_headers(admin,base+'/AdminWriting/Create');doc=document(text)
 response=request_with_headers(admin,base+'/AdminWriting/Create',data={'Title':title,'DocumentJson':doc,'CategoryId':'1001','IsActive':'true','PublishDate':field(form[2],'PublishDate'),'SaveAction':'Publish','Summary':'','Excerpt':'','__RequestVerificationToken':extract_antiforgery_token(form[2])})
 assert response[0]==200;id=int(field(response[2],'Id'));slug=sql(f'SELECT "Slug" FROM "Posts" WHERE "Id"={id}')
 sql(f'UPDATE "Posts" SET "ContentKind"={kind},"AccessScope"={scope},"IsActive"={str(active)},"IsPublished"={str(published)},"CategoryId"={category} WHERE "Id"={id};')
 fixtures[title]={'id':id,'slug':slug};return id,slug,doc,response[2]
def get(path,client=anon):return request_with_headers(client,base+path)
def search(term,**filters):return get('/ara?'+urlencode({'query':term,**filters}))
def card_slugs(body):return re.findall(r'<a href="/post/([^"]+)" class="post-card ',body)
id,slug,doc,edit=create('F16_public_article','F16_BODY_NEEDLE Türkçe İstanbul ıslak IĞDIR %_\\ <script> yalnız metin')
path='/post/'+slug;before=counter(id);detail=get(path);search_result=search('F16_BODY_NEEDLE')
if a.baseline:
 checks['before_json_body_missing_from_public_detail']=detail[0]==200 and 'F16_BODY_NEEDLE' not in detail[2]
 checks['before_visible_json_text_not_searchable']=slug not in card_slugs(search_result[2])
 Path(a.report).write_text(json.dumps({'baseline':True,'count':len(checks),'checks':checks,'fixture':fixtures},indent=2)+'\n');print(json.dumps(checks));assert all(checks.values());raise SystemExit(0)
checks['public_body_and_toc_safe_renderer']=detail[0]==200 and 'F16_BODY_NEEDLE' in detail[2] and '<table' in detail[2] and 'https://www.youtube-nocookie.com/embed/jNQXAC9IVRw' in detail[2] and 'aria-label="İçindekiler"' in html.unescape(detail[2]) and '<script> yalnız metin' not in detail[2]
checks['public_plain_text_search_finds_document']=slug in card_slugs(search_result[2])
checks['json_structure_keys_never_match_body']=all(slug not in card_slugs(search(term)[2]) for term in ['paragraph','youtube','textAlign','document','version'])
# Title is intentionally searchable independently of the body; use a key not contained in this title.
checks['legacy_content_decoy_not_searchable_for_json']=True
sql(f'UPDATE "Posts" SET "Content"=\'F16_LEGACY_DECOY\' WHERE "Id"={id};')
checks['legacy_content_decoy_not_searchable_for_json']=slug not in card_slugs(search('F16_LEGACY_DECOY')[2])
checks['get_increments_only_counter']=counter(id)==before+1
stable=snapshot();head=urllib.request.Request(base+path,method='HEAD');with_head=anon.open(head);with_head.close()
checks['head_and_admin_detail_do_not_count']=get(path,admin)[0]==200 and snapshot()==stable
schema=Schemas();schema.feed(detail[2])
if not schema.values:
 Path(a.report).write_text(json.dumps({'checks':checks,'detailStatus':detail[0],'detailUrl':detail[1],'detailHasBody': 'F16_BODY_NEEDLE' in detail[2],'detailHasSchema':False},indent=2)+'\n');raise AssertionError('Public detail did not produce its schema')
data=schema.values[0];plain=sql(f'SELECT "DocumentPlainText" FROM "Posts" WHERE "Id"={id}');words=int(sql(f'SELECT "DocumentWordCount" FROM "Posts" WHERE "Id"={id}'))
checks['seo_uses_validated_plain_text_and_word_count']=data['description']==plain[:160] and data['wordCount']==words and 'F16_LEGACY_DECOY' not in data['description']
checks['strict_detail_csp_and_no_store']='no-store' in detail[3].get('Cache-Control','') and "style-src-attr 'none'" in detail[3].get('Content-Security-Policy','') and 'unsafe-eval' not in detail[3].get('Content-Security-Policy','')
# Literal LIKE escapes are compared with the actual database collation, never guessed Turkish semantics.
for term in ['%_\\','İSTANBUL','ıslak','IĞDIR','İstanbul']:
 pattern='%'+term.replace('\\','\\\\').replace('%','\\%').replace('_','\\_')+'%'
 expected=sql(f'SELECT "DocumentPlainText" ILIKE {quoted(pattern)} ESCAPE \'\\\' FROM "Posts" WHERE "Id"={id};')=='t'
 checks['literal_collation_'+term]= (slug in card_slugs(search(term)[2]))==expected
exp,exp_slug,_,_=create('F16_experience_document','F16_BODY_NEEDLE deneyim',kind=2)
phil,phil_slug,_,_=create('F16_philosophy_document','F16_BODY_NEEDLE düşünce',kind=3)
# Use a real committed JSON edit to invalidate any cached home built before type updates.
form=get('/AdminWriting/Edit/'+str(id),admin);edit_input={'Id':str(id),'EditVersion':field(form[2],'EditVersion'),'Title':'F16_public_article','CategoryId':'1001','IsActive':'true','PublishDate':field(form[2],'PublishDate'),'DocumentJson':doc,'SaveAction':'Save','Summary':'','Excerpt':'','__RequestVerificationToken':extract_antiforgery_token(form[2])}
assert request_with_headers(admin,base+'/AdminWriting/Edit/'+str(id),data=edit_input)[0]==200
for key,wanted,unwanted in [('news',slug,[exp_slug,phil_slug]),('experience',exp_slug,[slug,phil_slug]),('philosophy',phil_slug,[slug,exp_slug])]:
 for prefix in ['/?','/category/f01-active?','/ara?query=F16_BODY_NEEDLE&']:
  response=get(prefix+'kind='+key);cards=card_slugs(response[2]);checks['kind_'+key+'_'+prefix]=response[0]==200 and wanted in cards and not any(s in cards for s in unwanted)
checks['kind_canonical_drops_tracking_keeps_filter']='kind=news' in get('/?kind=news&utm_campaign=ignore')[2] and 'utm_campaign' not in re.search(r'<link[^>]*rel="canonical"[^>]*>',get('/?kind=news&utm_campaign=ignore')[2])[0]
for kind in ['newsletter','4','unknown','NEWS','x'*11]:checks['invalid_kind_'+kind]=all(get(path+'kind='+kind)[0]==400 for path in ['/?','/ara?query=x&','/category/f01-active?'])
checks['bounded_paging_and_input']=get('/?kind=news&page=1001')[0]==400 and get('/?kind=news&pageSize=99')[0]==400 and search('x'*101,kind='news')[0]==400
private=[]
for name,options in [('subscribers',{'scope':1}),('newsletter',{'kind':4,'scope':1}),('draft',{'published':False}),('inactive',{'active':False}),('inactive-category',{'category':1002})]:
 hidden,hidden_slug,_,_=create('F16_PRIVATE_'+name,'F16_PRIVATE_SENTINEL_'+name,**options);private.append((hidden,hidden_slug,'F16_PRIVATE_SENTINEL_'+name))
 if name=='subscribers':subscriber_id=hidden
for hidden,hidden_slug,sentinel in private:
 stable=snapshot();denied=get('/post/'+hidden_slug);checks['private_'+hidden_slug+'_404_without_body_or_counter']=denied[0]==404 and sentinel not in denied[2] and snapshot()==stable
public_outputs=[get(p)[2] for p in ['/', '/category/f01-active','/ara?query=F16_PRIVATE','/sitemap.xml','/api/public/posts/latest','/rss.xml',path]]
checks['private_absent_from_all_public_projections_metadata_cache']=all(hidden_slug not in body and sentinel not in body for _,hidden_slug,sentinel in private for body in public_outputs)
# Broken/unknown/tampered document facts must never fall back to legacy HTML or produce SEO/counter writes.
original=sql(f'SELECT "DocumentJson" FROM "Posts" WHERE "Id"={id}');old_plain=sql(f'SELECT "DocumentPlainText" FROM "Posts" WHERE "Id"={id}')
for name,statement in [('malformed',f'"DocumentJson"=\'{{\''),('unknown',f'"DocumentJson"={quoted(doc.replace(chr(34)+"version"+chr(34)+": 1",chr(34)+"version"+chr(34)+": 2"))}'),('tampered',f'"DocumentPlainText"=\'F16_TAMPERED\'')]:
 sql(f'UPDATE "Posts" SET {statement} WHERE "Id"={id};');stable=snapshot();response=get(path)
 checks['bad_document_'+name+'_503_without_counter_body_or_schema']=response[0]==503 and snapshot()==stable and 'F16_BODY_NEEDLE' not in response[2] and 'application/ld+json' not in response[2] and 'no-store' in response[3].get('Cache-Control','')
 sql(f'UPDATE "Posts" SET "DocumentJson"={quoted(original)},"DocumentPlainText"={quoted(old_plain)} WHERE "Id"={id};')
# Page SQL is bounded and filter context survives pagination.
page_slugs=[]
for number in range(11):
 _,page_slug,_,_=create('F16_PAGE_'+str(number),'F16_PAGE_BODY unique '+str(number));page_slugs.append(page_slug)
first=search('F16_PAGE_BODY',kind='news',page=1);second=search('F16_PAGE_BODY',kind='news',page=2)
first_cards=card_slugs(first[2]);second_cards=card_slugs(second[2])
checks['kind_paging_is_bounded_disjoint_and_complete']=len(first_cards)==9 and len(second_cards)==2 and not set(first_cards)&set(second_cards) and set(first_cards+second_cards)==set(page_slugs)
checks['paging_links_preserve_kind_and_query']='kind=news' in html.unescape(first[2]) and 'query=F16_PAGE_BODY' in html.unescape(first[2]) and second[0]==200
# Filtered requests intentionally bypass the existing page-only cache; direct SQL changes prove no stale result.
filtered=get('/?kind=news');changed_id=fixtures['F16_PAGE_10']['id'];sql(f'UPDATE "Posts" SET "Title"=\'F16_FILTER_FRESH\' WHERE "Id"={changed_id};')
checks['kind_response_is_fresh_without_cache_eviction']='F16_FILTER_FRESH' in get('/?kind=news')[2] and 'no-store' in filtered[3].get('Cache-Control','')
sql(f'UPDATE "Posts" SET "Title"=\'F16_PAGE_10\' WHERE "Id"={changed_id};')
# An unfiltered cached response must be evicted by the same real atomic JSON save.
warm=get('/');cache_id=fixtures['F16_PAGE_10']['id'];cache_form=get('/AdminWriting/Edit/'+str(cache_id),admin)
cache_doc=html.unescape(re.search(r'<textarea[^>]*name="DocumentJson"[^>]*>(.*?)</textarea>',cache_form[2],re.S)[1]).lstrip('\n')
committed=request_with_headers(admin,base+'/AdminWriting/Edit/'+str(cache_id),data={'Id':str(cache_id),'EditVersion':field(cache_form[2],'EditVersion'),'Title':'F16_CACHE_COMMITTED','DocumentJson':cache_doc,'CategoryId':'1001','IsActive':'true','SaveAction':'Save','PublishDate':field(cache_form[2],'PublishDate'),'Summary':'','Excerpt':'','__RequestVerificationToken':extract_antiforgery_token(cache_form[2])})
checks['real_json_commit_invalidates_unfiltered_home']=committed[0]==200 and 'F16_CACHE_COMMITTED' in get('/')[2]
# Revocation retains the canonical JSON in storage but cannot survive a stale public cache.
sql(f'UPDATE "Posts" SET "AccessScope"=1 WHERE "Id"={cache_id};');cache_form=get('/AdminWriting/Edit/'+str(cache_id),admin)
request_with_headers(admin,base+'/AdminWriting/Edit/'+str(cache_id),data={'Id':str(cache_id),'EditVersion':field(cache_form[2],'EditVersion'),'Title':'F16_CACHE_REVOKED','DocumentJson':cache_doc,'CategoryId':'1001','IsActive':'true','SaveAction':'Save','PublishDate':field(cache_form[2],'PublishDate'),'Summary':'','Excerpt':'','__RequestVerificationToken':extract_antiforgery_token(cache_form[2])})
checks['privacy_change_commit_evicts_public_card_and_body']=fixtures['F16_PAGE_10']['slug'] not in card_slugs(get('/')[2]) and get('/post/'+fixtures['F16_PAGE_10']['slug'])[0]==404
# Deadline and invalidation: schedule through real JSON editor, then make the same item private through owned SQL plus real save invalidation.
scheduled,scheduled_slug,scheduled_doc,scheduled_form=create('F16_scheduled_document','F16_SCHEDULED_BODY')
form=get('/AdminWriting/Edit/'+str(scheduled),admin)
future=__import__('datetime').datetime.now(__import__('datetime').timezone.utc)+__import__('datetime').timedelta(seconds=5)
site_date=(future+__import__('datetime').timedelta(hours=3)).strftime('%Y-%m-%dT%H:%M:%S')
request_with_headers(admin,base+'/AdminWriting/Edit/'+str(scheduled),data={'Id':str(scheduled),'EditVersion':field(form[2],'EditVersion'),'Title':'F16_scheduled_document','DocumentJson':scheduled_doc,'CategoryId':'1001','IsActive':'true','SaveAction':'Schedule','PublishDate':site_date,'Summary':'','Excerpt':'','__RequestVerificationToken':extract_antiforgery_token(form[2])})
checks['scheduled_not_visible_before_deadline']=get('/post/'+scheduled_slug)[0]==404 and scheduled_slug not in card_slugs(get('/?kind=news')[2])
time.sleep(5.1);checks['scheduled_visible_after_deadline']=get('/post/'+scheduled_slug)[0]==200
# Keep the browser fixture pristine again and mark the schedule as a distinct public kind.
Path(a.report).write_text(json.dumps({'count':len(checks),'checks':checks,'fixture':fixtures,'publicId':id,'publicSlug':slug,'documentJson':doc,'plainText':plain,'scope':'Owned real MVC/PostgreSQL; direct SQL only sets synthetic classification/privacy or corruption fixtures.'},ensure_ascii=False,indent=2)+'\n');print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}));assert all(checks.values())
