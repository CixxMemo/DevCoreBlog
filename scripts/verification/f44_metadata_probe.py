#!/usr/bin/env python3
"""Exercise SEO encoding and editorial timestamps only on the owned, migrated F44 fixture."""
import argparse
from datetime import datetime, timezone
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import shlex
import subprocess
import urllib.parse
from http_probe_support import admin_login_path, cookie_opener, request, request_with_headers, submit_login, extract_antiforgery_token, extract_hidden_value
parser=argparse.ArgumentParser()
parser.add_argument('--fixture-root',required=True)
a=parser.parse_args()
root=Path(a.fixture_root).resolve()
assert str(root).startswith('/private/tmp/devcoreblog-f17.') and (root/'source/.env').read_text()==''
opts=shlex.split((root/'postgres/postmaster.opts').read_text())
assert opts[opts.index('-p')+1]=='55452'
base='http://127.0.0.1:15180'
def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p','55452','-U',os.environ['USER'],'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement],text=True).strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')=='f01-visible'
assert sql('SELECT count(*) FROM "Posts" WHERE "Id" BETWEEN 4401 AND 4416')=='0'
payload='F44 </script><script>window.f44Injected=1</script> & "quoted" ğüş'
# Fixed synthetic literals are local to this disposable database, never production inputs.
sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion")
VALUES (4401,'F44 </script><script>window.f44Injected=1</script> & "quoted" ğüş','f44-malicious','A **bold** [label](https://example.test) <b>safe</b>','F44 body',1001,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','',true,'',1),
(4402,'F44 cover','f44-cover','','F44 body',1001,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','Cover **excerpt**',true,'https://blog.example.test/cover.png?x=1&y=2',1),
(4403,'F44 legacy image','f44-legacy-image','','F44 body',1001,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','',true,'javascript:alert(1)',1),
(4404,'F44 revision','f44-revision','Revision summary','Revision body',1001,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','Revision excerpt',true,'',1);''')
sql("""INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion")
SELECT i,'F44 Page '||i,'f44-page-'||i,'Page summary','Page body',1001,CURRENT_TIMESTAMP,true,0,'2020-01-01T00:00:00Z','',true,'',1 FROM generate_series(4405,4416) i;""")
class Head(HTMLParser):
    def __init__(self,body):
        super().__init__(convert_charrefs=True);self.meta={};self.canonical=[];self.scripts=[];self.active=None;self.title='';self.in_title=False;self.feed(body)
    def handle_starttag(self,tag,attrs):
        attrs=dict(attrs)
        if tag=='meta': self.meta.setdefault(attrs.get('name',attrs.get('property')),[]).append(attrs.get('content'))
        if tag=='link' and attrs.get('rel')=='canonical': self.canonical.append(attrs.get('href'))
        if tag=='script': self.active={'type':attrs.get('type'),'text':''};self.scripts.append(self.active)
        if tag=='title':self.in_title=True
    def handle_endtag(self,tag):
        if tag=='script':self.active=None
        if tag=='title':self.in_title=False
    def handle_data(self,data):
        if self.active is not None:self.active['text']+=data
        if self.in_title:self.title+=data
    def schemas(self):return [json.loads(s['text']) for s in self.scripts if s['type']=='application/ld+json']
checks={}
def check(name,value):checks[name]=bool(value)
visitor,_=cookie_opener()
def get(path,headers=None):
    status,url,body,h=request_with_headers(visitor,base+path,headers=headers);return status,Head(body),body,h
status,head,body,_=get('/post/f44-malicious?utm_source=fixture')
check('article_200_and_one_canonical',status==200 and head.canonical==['https://blog.example.test/post/f44-malicious'])
check('title_is_Razor_encoded',head.title==payload+' — DevCoreBlog' and '<script>window.f44Injected=1</script>' not in body)
check('summary_is_plain_bounded_text',head.meta.get('description')==['A bold label safe'])
check('one_safe_parseable_BlogPosting',len(head.schemas())==1 and head.schemas()[0]['@type']=='BlogPosting')
schema=head.schemas()[0]
check('schema_headline_roundtrips_malicious_text',schema['headline']==payload)
check('script_terminator_escaped',all('</script' not in s['text'].lower() for s in head.scripts if s['type']=='application/ld+json'))
check('schema_has_stored_UTC_date',datetime.fromisoformat(schema['datePublished'])==datetime(2020,1,1,tzinfo=timezone.utc))
check('no_invented_modified_author_publisher_rating',not any(k in schema for k in ['dateModified','author','publisher','aggregateRating','inLanguage']))
check('schema_uses_trusted_url',schema['url']==head.canonical[0] and schema['mainEntityOfPage']==head.canonical[0])
check('missing_cover_omits_image_meta_and_schema','image' not in schema and 'og:image' not in head.meta and 'twitter:image' not in head.meta and head.meta['twitter:card']==['summary'])
for path,expected in [('/','https://blog.example.test/'),('/?page=2&pageSize=9&tracking=drop','https://blog.example.test/?page=2'),('/category/f01-active','https://blog.example.test/category/f01-active'),('/category/f01-active?page=1&pageSize=18','https://blog.example.test/category/f01-active?pageSize=18')]:
    status,h,_,_=get(path)
    check('canonical_'+path,status==200 and h.canonical==[expected])
    check('website_meta_unique_'+path,all(len(h.meta.get(k,[]))==1 for k in ['description','og:title','og:url','twitter:title']) and h.meta['og:url']==[expected] and not h.schemas())
    check('indexable_'+path,'robots' not in h.meta)
_,h,_,_=get('/post/f44-cover')
s=h.schemas()[0];image='https://blog.example.test/cover.png?x=1&y=2'
check('valid_cover_shared_across_meta_schema',h.meta['og:image']==[image] and h.meta['twitter:image']==[image] and s['image']==image)
check('excerpt_fallback_is_plain_text',h.meta['description']==['Cover excerpt'])
check('large_image_card',h.meta['twitter:card']==['summary_large_image'])
_,h,_,_=get('/post/f44-legacy-image')
check('unsafe_legacy_cover_not_in_metadata','og:image' not in h.meta and 'image' not in h.schemas()[0])
check('missing_summary_has_safe_non_markdown_fallback',h.meta['description']==['Read F44 legacy image on DevCoreBlog.'])
_,h,_,headers=get('/ara?query=F44&category=f01-active&pageSize=18')
check('search_noindex_meta_and_header',h.meta['robots']==['noindex, nofollow, noarchive'] and headers.get('X-Robots-Tag')=='noindex, nofollow, noarchive')
check('search_single_normalized_canonical',h.canonical==['https://blog.example.test/ara?query=F44&category=f01-active&pageSize=18'])
check('search_no_article_schema',not h.schemas())
spoof={'Host':'attacker.example.test','X-Forwarded-Host':'forwarded.example.test','X-Forwarded-Proto':'http'}
_,h,_,_=get('/post/f44-malicious',spoof)
check('spoof_cannot_change_canonical_OG_schema',h.canonical==['https://blog.example.test/post/f44-malicious'] and h.meta['og:url']==h.canonical and h.schemas()[0]['url']==h.canonical[0])
_,h,_,_=get(admin_login_path())
check('login_noindex_single_canonical',h.meta.get('robots')==['noindex, nofollow, noarchive'] and len(h.canonical)==1)
admin,_=cookie_opener();_,_,status,url,_=submit_login(admin,base,'f17-admin',os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
check('admin_login_success',status==200 and '/Admin/Dashboard' in url)
for path in ['/Admin/Dashboard','/AdminPost','/AdminPost/Edit/4404','/AdminCategory']:
    status,_,body=request(admin,base+path);h=Head(body)
    check(path+'_private_meta',status==200 and h.meta.get('robots')==['noindex, nofollow, noarchive'] and len(h.canonical)==1 and not h.schemas())
def row():return json.loads(sql('SELECT row_to_json(p) FROM "Posts" p WHERE "Id"=4404'))
def edit(overrides=None,version=None):
    r=row();_,_,form=request(admin,base+'/AdminPost/Edit/4404')
    fields={k:str(r[k]) for k in ['Title','Content','Summary','Excerpt','CategoryId']}
    fields.update(Id='4404',IsActive=str(r['IsActive']).lower(),SaveAction='Save',PublishDate='2020-01-01T03:00:00',EditVersion=version or extract_hidden_value(form,'EditVersion'),__RequestVerificationToken=extract_antiforgery_token(form))
    fields.update(overrides or {})
    return request(admin,base+'/AdminPost/Edit/4404',data=fields)
check('migrated_and_created_revision_dates_null',row()['UpdatedDate'] is None)
edit({'UpdatedDate':'1999-01-01T00:00:00Z'})
check('unchanged_save_and_spoofed_modified_date_ignored',row()['UpdatedDate'] is None)
edit({'SaveAction':'SaveDraft'});check('draft_only_does_not_change_revision_date',row()['UpdatedDate'] is None)
edit({'SaveAction':'Publish'});check('publish_only_does_not_change_revision_date',row()['UpdatedDate'] is None)
start=datetime.now(timezone.utc);old=row();status,_,_=edit({'Content':'Meaningful revision body'});r=row();end=datetime.now(timezone.utc)
check('content_edit_sets_server_UTC',status==200 and r['UpdatedDate'] is not None and start<=datetime.fromisoformat(r['UpdatedDate'])<=end)
check('content_edit_keeps_slug_created_date',r['Slug']==old['Slug'] and r['CreatedDate']==old['CreatedDate'])
_,h,_,_=get('/post/f44-revision');check('schema_modified_matches_saved_date',datetime.fromisoformat(h.schemas()[0]['dateModified'])==datetime.fromisoformat(r['UpdatedDate']))
_,_,body,_=get('/post/f44-revision');check('modified_date_is_visible','Updated <time datetime=' in body)
new=row();check('counter_does_not_change_modified_date_or_version',new['UpdatedDate']==r['UpdatedDate'] and new['EditVersion']==r['EditVersion'] and new['ViewCount']==r['ViewCount']+2)
for field,value in [('Title','F44 title revision'),('Summary','New summary'),('Excerpt','New excerpt')]:
    start=datetime.now(timezone.utc);edit({field:value});r=row();check(field+'_edit_sets_UTC',start<=datetime.fromisoformat(r['UpdatedDate'])<=datetime.now(timezone.utc))
before=row();edit();check('unchanged_save_preserves_existing_modified_date',row()['UpdatedDate']==before['UpdatedDate'])
before=row();status,_,_=edit({'Title':''});check('validation_failure_preserves_modified_date',status==200 and row()['UpdatedDate']==before['UpdatedDate'])
before=row();status,_,body=edit({'Title':'Stale rejected title'},str(before['EditVersion']-1));check('conflict_409_preserves_content_and_date',status==409 and row()['UpdatedDate']==before['UpdatedDate'] and row()['Title']==before['Title'])
sql("""INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (4401,'F44 Revised category','f44-revised-category',CURRENT_TIMESTAMP,true,1);""")
start=datetime.now(timezone.utc);edit({'CategoryId':'4401'});r=row()
check('category_edit_sets_server_UTC',start<=datetime.fromisoformat(r['UpdatedDate'])<=datetime.now(timezone.utc))
before=row();edit({'IsActive':'false'});check('active_flag_only_preserves_modified_date',row()['UpdatedDate']==before['UpdatedDate'])
edit({'IsActive':'true'})
sql("""UPDATE "Posts" SET "Summary"=repeat('ğüş😀 ',50) WHERE "Id"=4403;""")
_,h,_,_=get('/post/f44-legacy-image');check('long_Unicode_description_bounded_by_runes',len(h.meta['description'][0])==160 and '😀' in h.meta['description'][0])
sql("""UPDATE "Posts" SET "Summary"='<script>window.f44DescriptionInjected=1</script>' WHERE "Id"=4403;""")
_,h,_,_=get('/post/f44-legacy-image');check('script_summary_omitted_for_safe_fallback',h.meta['description']==['Read F44 legacy image on DevCoreBlog.'])
sql("""UPDATE "Posts" SET "ThumbnailUrl"='https://fixture:fixture@blog.example.test/cover.png' WHERE "Id"=4403;""")
_,h,_,_=get('/post/f44-legacy-image');check('credential_image_omitted','og:image' not in h.meta and 'image' not in h.schemas()[0])
_,_,previewform=request(admin,base+'/AdminPost/Edit/4404')
fields={'Id':'4404','Title':'Private preview','Content':'Preview **body**','Summary':'','Excerpt':'','CategoryId':'1001','PublishDate':'2020-01-01T03:00:00','__RequestVerificationToken':extract_antiforgery_token(previewform)}
before=row();status,_,body,headers=request_with_headers(admin,base+'/AdminPost/Preview',data=fields);h=Head(body)
check('preview_noindex_and_no_article_schema',status==200 and h.meta.get('robots')==['noindex, nofollow, noarchive'] and not h.schemas() and len(h.canonical)==1 and headers.get('X-Robots-Tag')=='noindex, nofollow, noarchive')
check('preview_has_no_date_content_counter_side_effect',row()==before)
print(json.dumps({'checks':checks,'count':len(checks)},indent=2))
assert all(checks.values()),'Failed: '+', '.join(k for k,v in checks.items() if not v)
