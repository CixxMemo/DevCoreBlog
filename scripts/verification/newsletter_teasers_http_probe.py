"""Anonymous teaser/privacy and actual SQL-shape checks only on the owned F08 PostgreSQL/MVC fixture."""
import argparse,html,json,subprocess,urllib.parse,urllib.request,urllib.error
from pathlib import Path
from html.parser import HTMLParser
from http_probe_support import cookie_opener,request_with_headers
p=argparse.ArgumentParser();p.add_argument('--base-url',required=True);p.add_argument('--database-port',required=True);p.add_argument('--report-dir',type=Path,required=True);a=p.parse_args()
base=a.base_url.rstrip('/');assert urllib.parse.urlparse(base).hostname=='127.0.0.1';assert a.database_port in ['55485','55478']
a.report_dir.mkdir(parents=True,exist_ok=True)
def sql(q):return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],text=True).strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')=='f01-visible'
def lit(v):return "'"+v.replace("'","''")+"'"
def snapshot():return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id"; SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";')
original=snapshot();checks={};client,_=cookie_opener()
def check(name,ok):checks[name]=bool(ok);assert ok,name
def get(path,method='GET',headers=None):
 req=urllib.request.Request(base+path,method=method,headers=headers or {},data=b'' if method=='POST' else None)
 try:response=client.open(req,timeout=10)
 except urllib.error.HTTPError as e:response=e
 with response:return response.status,response.geturl(),response.read().decode('utf-8','replace'),dict(response.headers.items())
class Page(HTMLParser):
 def __init__(self,text):super().__init__();self.items=[];self.inside=False;self.field=None;self.links=[];self.feed(text)
 def handle_starttag(self,tag,attrs):
  attrs=dict(attrs)
  if tag=='article' and 'newsletter-teaser' in attrs.get('class','').split():self.items.append({'title':'','summary':''});self.inside=True
  if self.inside and tag in ['h2','p']:self.field='title' if tag=='h2' else 'summary'
  if tag=='a':self.links.append(attrs.get('href',''))
 def handle_data(self,value):
  if self.inside and self.field:self.items[-1][self.field]+=value
 def handle_endtag(self,tag):
  if tag in ['h2','p']:self.field=None
  if tag=='article':self.inside=False
existing=json.loads(sql('SELECT coalesce(json_agg(json_build_object(\'id\',"Id",\'active\',"IsActive")),\'[]\') FROM "Posts"'))
# Hide fixture's previously seeded newsletters only while constructing an exact expected set.
active_ids=[r['id'] for r in existing if r['active']];ids=[]
secret='F32_PRIVATE_BODY_CANARY';title='<script>window.f32Injected=1</script> & Başlık';long_summary='😀'*320+'F32_BEYOND_APPROVED_SUMMARY';category=int(sql('SELECT "Id" FROM "Categories" WHERE "IsActive" ORDER BY "Id" LIMIT 1'));inactive=int(sql('SELECT "Id" FROM "Categories" WHERE NOT "IsActive" ORDER BY "Id" LIMIT 1'))
try:
 sql('UPDATE "Posts" SET "IsActive"=false')
 status,_,body,headers=get('/bulten');check('empty_200_honest',status==200 and 'Henüz yayımlanmış bülten tanıtımı yok.' in html.unescape(body) and not Page(body).items)
 fixture=[('allow',True,True,category,4,1,'2020-01-01'),('draft',True,False,category,4,1,'2020-01-01'),('future',True,True,category,4,1,'2999-01-01'),('inactive',False,True,category,4,1,'2020-01-01'),('inactive-category',True,True,inactive,4,1,'2020-01-01'),('wrong-kind',True,True,category,1,0,'2020-01-01')]
 for i in range(1,11):fixture.append(('page-'+str(i),True,True,category,4,1,'2020-01-01'))
 for i,(name,active,published,cat,kind,scope,date) in enumerate(fixture):
  id=932000+i;ids.append(id);t=title if name=='allow' else 'F32 '+name;summary=long_summary if name=='allow' else ('F32_'+name+'_SUMMARY' if name!='page-1' else '')
  doc=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':secret}]}]}})
  sql(f'''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","Excerpt","ThumbnailUrl","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","IsPublished","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes","EditVersion") VALUES ({id},{lit(t)},{lit('f32-'+name)},{lit(summary)},{lit(secret)},{lit('F32_PRIVATE_EXCERPT')},{lit('https://example.test/F32_PRIVATE_COVER.png')},{cat},'2020-01-01',{str(active).lower()},789,'{date}',{str(published).lower()},{kind},{scope},1,{lit(doc)},{lit(secret)},1,1,1)''')
 seeded=snapshot();status,_,body,headers=get('/bulten?tracking=drop');page=Page(body);check('bounded_first_page',status==200 and len(page.items)==9)
 check('only_title_approved_unicode_summary',page.items[0]=={'title':title,'summary':'😀'*320})
 check('empty_summary_no_private_fallback',any(x['title']=='F32 page-1' and not x['summary'] for x in page.items))
 check('encoding_not_executable','<script>window.f32Injected=1</script>' not in body and title in html.unescape(body))
 check('private_fields_absent',all(v not in body for v in [secret,'F32_PRIVATE_EXCERPT','F32_PRIVATE_COVER','F32_BEYOND_APPROVED_SUMMARY','932000','789']))
 check('unpublished_ineligible_absent',all('F32 '+v not in html.unescape(body) for v in ['draft','future','inactive','inactive-category','wrong-kind']))
 check('static_meta_no_article_or_private','BlogPosting' not in body and 'https://blog.example.test/bulten' in body and secret not in body)
 check('no_store_and_CSP','no-store' in headers.get('Cache-Control','') and "script-src 'self'" in headers.get('Content-Security-Policy',''))
 status,_,second,_=get('/bulten?page=2');secondpage=Page(second);check('stable_disjoint_second_page',status==200 and len(secondpage.items)==2 and not set(x['title'] for x in page.items)&set(x['title'] for x in secondpage.items))
 check('paging_native_links',any('/bulten?page=2'==u for u in page.links) and any('/bulten?page=1'==u for u in secondpage.links))
 for value in ['0','1001','bad']:
  status,_,_,h=get('/bulten?page='+value);check('bad_page_'+value,status==400 and 'no-store' in h.get('Cache-Control',''))
 status,_,_,_=get('/bulten?page=3');check('out_of_range_404',status==404)
 status,_,b,h=get('/bulten',method='HEAD');check('HEAD_no_body',status==200 and not b and 'no-store' in h.get('Cache-Control',''))
 status,_,_,_=get('/bulten',method='POST');check('POST_no_mutation',status in [400,405])
 status,_,_,_=get('/post/f32-allow');check('private_body_route_stays_404',status==404)
 for route in ['/?f32=fixture','/ara?query='+secret,'/sitemap.xml','/rss.xml','/api/public/posts/latest']:
  status,_,b,_=get(route);check('full_body_not_in_other_surface_'+route,(secret not in b or route.startswith('/ara?')) and 'f32-allow' not in b)
 check('GET_HEAD_no_DB_write',snapshot()==seeded)
 # Real persistence fault: restore table name in finally, never disguise it as an empty list.
 sql('ALTER TABLE "Posts" RENAME TO "F32OwnedPostsUnavailable"')
 try:
  status,_,b,h=get('/bulten');check('real_storage_failure_503',status==503 and 'tanıtımları şu anda okunamıyor' in b and 'no-store' in h.get('Cache-Control','') and 'Npgsql' not in b)
 finally:sql('ALTER TABLE "F32OwnedPostsUnavailable" RENAME TO "Posts"')
 # Moving one scheduled record into eligibility becomes visible on the next no-cache read.
 sql('UPDATE "Posts" SET "PublishDate"=\'2020-01-01\' WHERE "Slug"=\'f32-future\'')
 status,_,b,_=get('/bulten');check('publication_boundary_next_request',any(x['title']=='F32 future' for x in Page(b).items))
 capture=(a.report_dir/'sql-projection.txt').read_text();commands=[c for c in capture.split('---F32---') if 'F32 public newsletter teaser fields' in c]
 check('actual_SQL_captured',bool(commands))
 check('actual_SQL_excludes_all_body_and_extra_columns',all(v not in capture for v in ['"Content"','"DocumentJson"','"DocumentPlainText"','"Excerpt"','"ThumbnailUrl"','"ViewCount"']))
 check('actual_SQL_projection_only_approved_columns',all('"Title"' in c.split('FROM')[0] and '"Summary"' in c.split('FROM')[0] and 'substring' in c.split('FROM')[0].lower() and '320' in c.split('FROM')[0] for c in commands))
 check('actual_SQL_database_paging',all('LIMIT' in c and 'OFFSET' in c and 'ORDER BY' in c for c in commands))
 (a.report_dir/'newsletter-teasers-http.json').write_text(json.dumps({'passed':True,'count':len(checks),'checks':checks,'scope':'actual owned MVC/PG; title/320-rune summary; captured tagged SQL'},indent=2)+'\n')
finally:
 if ids:sql('DELETE FROM "Posts" WHERE "Id" IN ('+','.join(map(str,ids))+')')
 if active_ids:sql('UPDATE "Posts" SET "IsActive"=true WHERE "Id" IN ('+','.join(map(str,active_ids))+')')
 assert snapshot()==original,'Owned fixture was not restored'
