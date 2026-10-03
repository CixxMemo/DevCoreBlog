#!/usr/bin/env python3
"""Verify RSS against raw bytes and an owned, migrated PostgreSQL fixture only."""
import argparse
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import shlex
import subprocess
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from zoneinfo import ZoneInfo
from http_probe_support import cookie_opener, request, submit_login, extract_antiforgery_token, extract_hidden_value

parser=argparse.ArgumentParser()
parser.add_argument('--fixture-root',required=True)
parser.add_argument('--app-port',type=int,default=15180)
parser.add_argument('--pg-port',type=int,default=55452)
a=parser.parse_args()
root=Path(a.fixture_root).resolve()
assert str(root).startswith('/private/tmp/devcoreblog-f17.') and (root/'source/.env').read_text()==''
opts=shlex.split((root/'postgres/postmaster.opts').read_text())
assert opts[opts.index('-p')+1]==str(a.pg_port)
base=f'http://127.0.0.1:{a.app_port}'
origin='https://blog.example.test'
def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',str(a.pg_port),'-U',os.environ['USER'],'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',statement],text=True).strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')=='f01-visible'
assert sql('SELECT count(*) FROM "Posts" WHERE "Id" BETWEEN 4601 AND 4625')=='0'
sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion")
SELECT i,'F46 Post '||i,'f46-rss-'||i,'Summary','Full body is not a feed field',1001,'2019-01-01Z',true,0,CURRENT_TIMESTAMP - interval '1 hour','Excerpt',true,'',1 FROM generate_series(4601,4625) i;
UPDATE "Posts" SET "Title"='F46 </title><script>window.f46Injected=1</script> ğüş & "quoted"', "Summary"='**Bold** [label](https://example.test) `<script>window.f46Injected=1</script>` & 😀' WHERE "Id"=4601;
UPDATE "Posts" SET "Summary"='', "Excerpt"='**Excerpt** only' WHERE "Id"=4602;
UPDATE "Posts" SET "Summary"=repeat('😀',400) WHERE "Id"=4603;
UPDATE "Posts" SET "Summary"='', "Excerpt"='' WHERE "Id"=4604;
UPDATE "Posts" SET "Title"='F46 emoji 😀'||chr(1) WHERE "Id"=4605;
UPDATE "Posts" SET "Summary"='<script>window.f46Injected=1</script>', "Excerpt"='Safe fallback' WHERE "Id"=4606;''')
checks={}
def check(name,condition):
    checks[name]=bool(condition)
    assert condition,name

def fetch(path='/rss.xml',method='GET',headers=None):
    req=urllib.request.Request(base+path,method=method,headers=headers or {})
    try:
        with urllib.request.urlopen(req,timeout=10) as r:return r.status,dict(r.headers.items()),r.read()
    except urllib.error.HTTPError as e:return e.code,dict(e.headers.items()),e.read()

def feed():
    status,headers,data=fetch()
    assert status==200
    doc=ET.fromstring(data)
    return doc,doc.find('channel').findall('item'),headers,data

def post_row():return json.loads(sql('SELECT row_to_json(p) FROM "Posts" p WHERE "Id"=4601'))
def snapshot():return sql('SELECT md5(string_agg(row_to_json(p)::text,\'\' ORDER BY "Id")) FROM "Posts" p')
def by_link(items):return {item.findtext('link'):item for item in items}
class PlainDescription(HTMLParser):
    def __init__(self,value):super().__init__(convert_charrefs=True);self.tags=[];self.text='';self.feed(value)
    def handle_starttag(self,tag,attrs):self.tags.append(tag)
    def handle_data(self,data):self.text+=data

before=snapshot();doc,items,headers,data=feed();links=by_link(items)
check('raw_UTF8_XML_and_declaration', b'encoding="utf-8"' in data and b'utf-16' not in data and not data.startswith(b'\xef\xbb\xbf'))
check('RSS_version_and_one_channel',doc.tag=='rss' and doc.attrib=={'version':'2.0'} and len(doc.findall('channel'))==1)
check('RSS_content_type',headers['Content-Type']=='application/rss+xml; charset=utf-8')
check('RSS_no_store', 'no-store' in headers['Cache-Control'])
channel=doc.find('channel')
check('required_channel_metadata',channel.findtext('title')=='DevCoreBlog' and channel.findtext('link')==origin+'/' and bool(channel.findtext('description')))
check('Atom_self_link_is_trusted_RSS',channel.find('{http://www.w3.org/2005/Atom}link').attrib=={'href':origin+'/rss.xml','rel':'self','type':'application/rss+xml'})
check('twenty_items_max_even_with_extra_query',len(items)==20 and len(ET.fromstring(fetch('/rss.xml?count=100000&page=2')[2]).find('channel').findall('item'))==20)
expected=sql('''SELECT p."Slug" FROM "Posts" p JOIN "Categories" c ON c."Id"=p."CategoryId" WHERE p."IsActive" AND p."IsPublished" AND c."IsActive" AND p."PublishDate" <= CURRENT_TIMESTAMP ORDER BY p."PublishDate" DESC,p."Id" LIMIT 20''').splitlines()
check('DB_latest_order_and_ties', [item.findtext('link') for item in items]==[origin+'/post/'+slug for slug in expected])
check('only_unique_canonical_urls',len(links)==20 and all(link.startswith(origin+'/post/') for link in links))
for slug in ['f01-draft','f01-future-visible-marker','f01-inactive-post','f01-duplicate-title']:
    check('hidden_'+slug,origin+'/post/'+slug not in links)
check('permalink_GUID_matches_stable_link',all(item.find('guid').attrib=={'isPermaLink':'true'} and item.findtext('guid')==item.findtext('link') for item in items))
check('publication_dates_UTC_RFC1123',all(parsedate_to_datetime(item.findtext('pubDate')).utcoffset().total_seconds()==0 and item.findtext('pubDate').endswith(' GMT') for item in items))
check('publication_uses_saved_date_not_created',parsedate_to_datetime(links[origin+'/post/f46-rss-4601'].findtext('pubDate')).replace(microsecond=0)==datetime.fromisoformat(post_row()['PublishDate']).replace(microsecond=0))
check('no_fake_author_language_or_build_timestamp',channel.find('language') is None and channel.find('lastBuildDate') is None and all(item.find('author') is None for item in items))
malicious=links[origin+'/post/f46-rss-4601']
check('title_is_XML_text_not_elements',malicious.findtext('title')==post_row()['Title'] and malicious.find('script') is None)
plain=PlainDescription(malicious.findtext('description') or '')
check('description_has_no_HTML_elements',plain.tags==[])
check('Markdown_summary_plain_and_literal_code_safe',plain.text=='Bold label <script>window.f46Injected=1</script> & 😀' and 'https://example.test' not in plain.text)
check('excerpt_fallback',PlainDescription(links[origin+'/post/f46-rss-4602'].findtext('description') or '').text=='Excerpt only')
check('summary_bound_is_320_Unicode_characters',PlainDescription(links[origin+'/post/f46-rss-4603'].findtext('description') or '').text=='😀'*320)
check('empty_description_no_invented_text',not links[origin+'/post/f46-rss-4604'].findtext('description'))
check('legacy_XML_control_removed_emoji_preserved',links[origin+'/post/f46-rss-4605'].findtext('title')=='F46 emoji 😀')
check('HTML_only_summary_falls_back',PlainDescription(links[origin+'/post/f46-rss-4606'].findtext('description') or '').text=='Safe fallback')
check('no_full_content_or_enclosure',b'Full body is not a feed field' not in data and all(item.find('enclosure') is None for item in items))
check('no_scripts_or_html_elements_anywhere',not doc.findall('.//script') and all(PlainDescription(item.findtext('description') or '').tags==[] for item in items))
check('spoofed_host_headers_ignored',fetch(headers={'Host':'attacker.example','X-Forwarded-Host':'attacker.example','X-Forwarded-Proto':'http'})[2]==data)
status,hh,head=fetch(method='HEAD')
check('HEAD_returns_no_body',status==200 and head==b'' and hh['Content-Type']==headers['Content-Type'])
check('POST_rejected',fetch(method='POST')[0] in [400,405])
check('RSS_reads_no_row_counter_date_or_version_changes',snapshot()==before)
class Links(HTMLParser):
    def __init__(self,body):super().__init__();self.discovery=[];self.follow=[];self.feed(body)
    def handle_starttag(self,tag,attrs):
        attrs=dict(attrs)
        if tag=='link' and attrs.get('type')=='application/rss+xml':self.discovery.append(attrs)
        if tag=='a' and attrs.get('id')=='rss-follow-link':self.follow.append(attrs)
for path in ['/','/category/f01-active','/post/f46-rss-4601','/ara?query=F46']:
    status,_,body=fetch(path);html=Links(body.decode('utf-8'))
    check(path+'_single_discovery_and_follow',status==200 and len(html.discovery)==1 and html.discovery[0].get('rel')=='alternate' and html.discovery[0].get('href')==origin+'/rss.xml' and len(html.follow)==1 and html.follow[0].get('href')=='/rss.xml')
admin,_=cookie_opener();_,_,status,url,_=submit_login(admin,base,'f17-admin',os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
check('admin_login',status==200 and '/Admin/Dashboard' in url)
original=post_row();original_guid=malicious.findtext('guid')
def edit(overrides):
    r=post_row();_,_,form=request(admin,base+'/AdminPost/Edit/4601')
    fields={k:str(r[k]) for k in ['Title','Content','Summary','Excerpt','CategoryId']}
    fields.update(Id='4601',PublishDate=datetime.fromisoformat(r['PublishDate']).astimezone(ZoneInfo('Europe/Istanbul')).strftime('%Y-%m-%dT%H:%M:%S'),IsActive=str(r['IsActive']).lower(),SaveAction='Save',EditVersion=extract_hidden_value(form,'EditVersion'),__RequestVerificationToken=extract_antiforgery_token(form))
    fields.update(overrides);return request(admin,base+'/AdminPost/Edit/4601',data=fields)
check('editor_revision_saved',edit({'Title':'F46 revised title','Summary':'New **summary** & 😀'})[0]==200 and post_row()['Title']=='F46 revised title')
updated=by_link(feed()[1])[origin+'/post/f46-rss-4601']
check('GUID_unchanged_after_editorial_edit',updated.findtext('guid')==original_guid and post_row()['Slug']==original['Slug'])
check('saved_summary_refreshed_immediately',PlainDescription(updated.findtext('description')).text=='New summary & 😀')
check('pubDate_unchanged_by_content_edit',updated.findtext('pubDate')==malicious.findtext('pubDate'))
check('draft_save_removes_item',edit({'SaveAction':'SaveDraft'})[0]==200 and origin+'/post/f46-rss-4601' not in by_link(feed()[1]))
check('republish_restores_same_GUID',edit({'SaveAction':'Publish'})[0]==200 and by_link(feed()[1])[origin+'/post/f46-rss-4601'].findtext('guid')==original_guid)
check('republish_uses_new_saved_date',parsedate_to_datetime(by_link(feed()[1])[origin+'/post/f46-rss-4601'].findtext('pubDate')).replace(microsecond=0)==datetime.fromisoformat(post_row()['PublishDate']).replace(microsecond=0))
check('inactive_edit_removes_item',edit({'IsActive':'false'})[0]==200 and origin+'/post/f46-rss-4601' not in by_link(feed()[1]))
check('active_edit_restores_same_GUID',edit({'IsActive':'true'})[0]==200 and by_link(feed()[1])[origin+'/post/f46-rss-4601'].findtext('guid')==original_guid)
sql('UPDATE "Categories" SET "IsActive"=false WHERE "Id"=1001')
check('category_deactivation_removes_items',origin+'/post/f46-rss-4601' not in by_link(feed()[1]))
sql('UPDATE "Categories" SET "IsActive"=true WHERE "Id"=1001')
sql('UPDATE "Posts" SET "PublishDate"=CURRENT_TIMESTAMP + interval \'3 seconds\' WHERE "Id"=4601')
check('future_scheduled_hidden',origin+'/post/f46-rss-4601' not in by_link(feed()[1]))
time.sleep(3.5)
check('scheduled_boundary_visible_without_write',origin+'/post/f46-rss-4601' in by_link(feed()[1]))
check('scheduled_boundary_GUID_unchanged',by_link(feed()[1])[origin+'/post/f46-rss-4601'].findtext('guid')==original_guid)
# Restore the synthetic malicious title so browser checks can observe escaping too.
check('browser_fixture_title_restored',edit({'Title':original['Title']})[0]==200 and post_row()['Title']==original['Title'])
snapshot_before_empty=snapshot()
# A transaction cannot span HTTP; snapshot fixture active flags explicitly and restore them.
active=json.loads(sql('SELECT json_agg("Id") FROM "Categories" WHERE "IsActive"'))
sql('UPDATE "Categories" SET "IsActive"=false')
try:
    empty_doc,empty_items,_,_=feed()
    check('empty_blog_still_valid_RSS_channel',not empty_items and empty_doc.find('channel/title').text=='DevCoreBlog')
finally:
    sql('UPDATE "Categories" SET "IsActive"=true WHERE "Id" IN ('+','.join(str(i) for i in active)+')')
check('empty_blog_probe_preserves_all_post_rows',snapshot()==snapshot_before_empty)
print(json.dumps({'checks':checks,'count':len(checks),'postgres_version':sql('SHOW server_version')},indent=2))
