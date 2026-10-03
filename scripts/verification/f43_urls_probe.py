#!/usr/bin/env python3
"""Verify canonical routing, trusted origins and counter/slug integrity on the named disposable fixture."""
import argparse
import html
import json
import os
from pathlib import Path
import re
import subprocess
import shlex
import urllib.error
import urllib.parse
import urllib.request
from http_probe_support import cookie_opener, request, submit_login

parser=argparse.ArgumentParser()
parser.add_argument('--fixture-root',required=True)
args=parser.parse_args()
root=Path(args.fixture_root).resolve()
assert str(root).startswith('/private/tmp/devcoreblog-f17.') and (root/'postgres/PG_VERSION').exists() and (root/'source/.env').read_text()=='', 'Owned empty-env fixture required'
pg_options=shlex.split((root/'postgres/postmaster.opts').read_text())
assert pg_options[pg_options.index('-p')+1]=='55450', 'Named F43 PostgreSQL required'
base='http://127.0.0.1:15178'
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,req,fp,code,msg,headers,newurl): return None
visitor=urllib.request.build_opener(NoRedirect())
def get(path,method='GET',headers=None):
    req=urllib.request.Request(base+path,method=method,headers=headers or {})
    try: response=visitor.open(req,timeout=10)
    except urllib.error.HTTPError as error: response=error
    with response: return response.code,response.headers,response.read().decode('utf8','replace')
def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p','55450','-U',os.environ['USER'],'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement]).decode().strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')=='f01-visible'
assert sql('SELECT count(*) FROM "Posts" WHERE "Id" BETWEEN 4301 AND 4312')=='0','Fresh fixture required'
slug='f43-istanbul-ğüş %?#'
category='f43-category-ğüş %?#'
sql('''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (4301,'F43 Unicode category','f43-category-ğüş %?#',CURRENT_TIMESTAMP,true,1);
INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion")
SELECT i,'F43 Article '||i, CASE WHEN i=4301 THEN 'f43-istanbul-ğüş %?#' ELSE 'f43-page-'||i END,'F43 summary','F43 body',4301,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','F43 excerpt',true,'',1 FROM generate_series(4301,4312) i;''')
ownership=sql('SELECT "Id","Slug" FROM "Posts" ORDER BY "Id"; SELECT "Id","Slug" FROM "Categories" ORDER BY "Id";')
checks={}
def check(name,value): checks[name]=bool(value)
def count(): return int(sql('SELECT "ViewCount" FROM "Posts" WHERE "Id"=2001'))
query='?tag=%C4%B0stanbul%20%26%20C%23&tag=two&returnUrl=https%3A%2F%2Fevil.example%2F'
before=count()
status,headers,_=get('/yazi/f01-visible'+query)
check('old_post_single_301_preserves_raw_query',status==301 and headers.get('Location')=='/post/f01-visible'+query)
check('alias_does_not_count',count()==before)
status,headers,detail=get('/post/f01-visible'+query)
check('canonical_post_200_without_redirect',status==200 and 'Location' not in headers)
check('one_followed_GET_counts_once',count()==before+1)
check('canonical_OG_uses_SITE_URL', 'content="https://blog.example.test/post/f01-visible"' in detail)
for name,path,expected in [('old_category','/kategori/f01-active','/category/f01-active'),('conventional_post','/Home/Detail?slug=f01-visible','/post/f01-visible?slug=f01-visible'),('conventional_category','/Home/Category?slug=f01-active','/category/f01-active?slug=f01-active'),('post_trailing_slash','/post/f01-visible/','/post/f01-visible'),('category_trailing_slash','/category/f01-active/','/category/f01-active')]:
    status,headers,_=get(path)
    check(name+'_single_301', status==301 and headers.get('Location')==expected)
status,headers,body=get('/kategori/f01-active?page=1&pageSize=18')
check('category_filters_survive_redirect',status==301 and headers.get('Location')=='/category/f01-active?page=1&pageSize=18')
status,headers,body=get('/category/f01-active?page=1&pageSize=18')
check('canonical_category_200_without_loop',status==200 and 'Location' not in headers)
check('navigation_marks_canonical_category_current', re.search(r'href="/category/f01-active" aria-current="page"',body) is not None)
for slug_value in ['f43-missing','f01-draft','f01-future-visible-marker','f01-inactive-post','f01-duplicate-title']:
    for prefix in ['post','yazi']:
        status,headers,_=get('/'+prefix+'/'+slug_value)
        check(prefix+'_'+slug_value+'_404_without_location',status==404 and 'Location' not in headers)
for prefix in ['category','kategori']:
    for slug_value in ['f43-missing','f01-inactive']:
        status,headers,_=get('/'+prefix+'/'+slug_value)
        check(prefix+'_'+slug_value+'_404',status==404 and 'Location' not in headers)
before=count()
for prefix,status_expected in [('post',200),('yazi',301)]:
    status,_,body=get('/'+prefix+'/f01-visible',method='HEAD')
    check(prefix+'_HEAD_status_and_empty_body',status==status_expected and body=='')
check('HEAD_does_not_count', count()==before)
post_path='/post/'+urllib.parse.quote(slug,safe='')
cat_path='/category/'+urllib.parse.quote(category,safe='')
status,headers,_=get('/yazi/'+urllib.parse.quote(slug,safe='')+query)
check('Unicode_reserved_slug_is_encoded_once',status==301 and headers.get('Location')==post_path+query)
status,headers,unicode_detail=get(post_path)
check('encoded_slug_roundtrip_200',status==200 and 'F43 Article 4301' in unicode_detail)
check('encoded_category_link_uses_same_builder', 'href="'+cat_path+'"' in unicode_detail)
status,headers,_=get('/kategori/'+urllib.parse.quote(category,safe='')+'?page=2')
check('encoded_category_redirect_preserves_page',status==301 and headers.get('Location')==cat_path+'?page=2')
status,headers,category_page=get(cat_path)
check('category_paging_uses_canonical_encoded_path',status==200 and 'href="'+cat_path+'?page=2"' in category_page)
spoof={'Host':'attacker.example.test','X-Forwarded-Host':'forwarded.example.test','X-Forwarded-Proto':'http'}
for name,path,expected in [('home','/','https://blog.example.test/'),('detail','/post/f01-visible','https://blog.example.test/post/f01-visible'),('category','/category/f01-active','https://blog.example.test/category/f01-active')]:
    status,_,body=get(path,headers=spoof)
    check(name+'_host_spoof_cannot_change_OG',status==200 and 'content="'+expected+'"' in body and 'attacker.example.test' not in body and 'forwarded.example.test' not in body)
    links=re.findall(r'href="([^\"]+)"',body)
    check(name+'_has_no_legacy_article_category_links', all(not link.startswith(('/yazi/','/kategori/')) for link in links))
status,_,sitemap=get('/sitemap.xml',headers=spoof)
check('sitemap_uses_trusted_canonical_origins',status==200 and '<loc>https://blog.example.test/post/' in sitemap and '<loc>https://blog.example.test/category/' in sitemap and 'attacker.example.test' not in sitemap and '/yazi/' not in sitemap and '/kategori/' not in sitemap)
check('sitemap_escapes_reserved_slugs',post_path in sitemap and cat_path in sitemap)
status,_,feed=get('/api/public/posts/latest',headers=spoof)
items=json.loads(feed)
check('feed_contract_and_origin_preserved',status==200 and len(items)<=3 and all(set(p)=={'id','title','slug','summary','excerpt','coverImageUrl','publishDate','url','categoryName'} and p['url'].startswith('https://blog.example.test/post/') for p in items))
status,headers,_=get('/yazi/f01-visible',headers=spoof)
check('redirect_remains_local_under_host_spoof',status==301 and headers.get('Location')=='/post/f01-visible')
admin,_=cookie_opener()
_,_,status,url,_=submit_login(admin,base,'f17-admin',os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
check('synthetic_admin_signs_in',status==200 and '/Admin/Dashboard' in url)
status,_,admin_list=request(admin,base+'/AdminPost')
check('admin_live_link_encodes_stored_slug',status==200 and 'href="'+post_path+'"' in admin_list)
before=count()
status,_,_=request(admin,base+'/post/f01-visible')
check('authenticated_canonical_GET_does_not_count',status==200 and count()==before)
check('all_existing_post_category_slugs_stay_unchanged',ownership==sql('SELECT "Id","Slug" FROM "Posts" ORDER BY "Id"; SELECT "Id","Slug" FROM "Categories" ORDER BY "Id";'))
print(json.dumps({'checks':checks,'count':len(checks)},indent=2))
assert all(checks.values()),'Failed: '+', '.join(k for k,v in checks.items() if not v)
