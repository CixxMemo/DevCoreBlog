#!/usr/bin/env python3
"""Verify raw crawler documents only against an owned, migrated PostgreSQL fixture."""
import argparse
from datetime import datetime
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

parser = argparse.ArgumentParser()
parser.add_argument('--fixture-root', required=True)
parser.add_argument('--app-port', type=int, required=True)
parser.add_argument('--pg-port', type=int, required=True)
parser.add_argument('--baseline', action='store_true')
a = parser.parse_args()
root = Path(a.fixture_root).resolve()
assert str(root).startswith('/private/tmp/devcoreblog-f17.')
assert (root/'source/.env').read_text() == '' and (root/'postgres/PG_VERSION').exists()
opts = shlex.split((root/'postgres/postmaster.opts').read_text())
assert opts[opts.index('-p')+1] == str(a.pg_port)
base = f'http://127.0.0.1:{a.app_port}'
origin = 'https://blog.example.test'
ns = {'s': 'http://www.sitemaps.org/schemas/sitemap/0.9'}

def sql(statement):
    return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', str(a.pg_port), '-U', os.environ['USER'], '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c', statement], text=True).strip()
assert sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001') == 'f01-visible'
assert sql('SELECT count(*) FROM "Posts" WHERE "Id" BETWEEN 4501 AND 4504') == '0'
sql('''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion")
VALUES (4501,'F45 Empty category','f45-empty',CURRENT_TIMESTAMP,true,1);
INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion","UpdatedDate")
VALUES (4501,'F45 legacy','f45-legacy','Summary','Body',1001,'2019-01-01Z',true,0,'2020-01-01Z','',true,'',1,NULL),
(4502,'F45 revision','f45-revision','Summary','Body',1001,'2019-01-01Z',true,0,'2020-01-01Z','',true,'',1,'2021-02-03T04:05:06Z'),
(4503,'F45 draft revision','f45-draft-revision','Summary','Body',1001,'2019-01-01Z',true,0,'2020-01-01Z','',true,'',1,'2019-02-03Z'),
(4504,'F45 escaped','f45-ğüş&quoted','Summary','Body',1001,'2019-01-01Z',true,0,'2020-01-01Z','',true,'',1,NULL);''')

def fetch(path, method='GET', headers=None):
    req = urllib.request.Request(base+path, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, dict(response.headers.items()), response.read()
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers.items()), e.read()

def rows():
    status, headers, data = fetch('/sitemap.xml')
    assert status == 200
    doc = ET.fromstring(data)
    return {entry.find('s:loc', ns).text: entry.findtext('s:lastmod', namespaces=ns) for entry in doc.findall('s:url', ns)}, headers, data

def snapshot():
    return sql('SELECT md5(string_agg(row_to_json(p)::text,\'\' ORDER BY "Id")) FROM "Posts" p')

status, headers, data = fetch('/sitemap.xml')
if a.baseline:
    try:
        ET.fromstring(data); parsed = True
    except ET.ParseError:
        parsed = False
    body = data.decode('utf-8')
    result = {'raw_XML_rejected': not parsed, 'UTF16_declaration_in_UTF8_response': b'encoding="utf-16"' in data and 'utf-8' in headers['Content-Type'], 'real_revision_date_missing': '2021-02-03' not in body, 'robots_missing': fetch('/robots.txt')[0] == 404}
    assert all(result.values())
    print(json.dumps(result, indent=2))
    raise SystemExit(0)

checks = {}
def check(name, condition):
    checks[name] = bool(condition)
    assert condition, name
before = snapshot()
urls, headers, data = rows()
check('raw_bytes_parse_and_UTF8_declaration', b'encoding="utf-8"' in data and b'utf-16' not in data)
check('XML_content_type_UTF8', headers['Content-Type'] == 'application/xml; charset=utf-8')
check('no_store', 'no-store' in headers['Cache-Control'])
check('namespace_and_unique_URLs', ET.fromstring(data).tag == '{'+ns['s']+'}urlset' and len(urls) == len(ET.fromstring(data)))
check('trusted_origin_and_only_canonical_paths', all(u.startswith(origin+'/') and (u == origin+'/' or u.startswith(origin+'/post/') or u.startswith(origin+'/category/')) for u in urls))
check('home_and_active_category_present', origin+'/' in urls and origin+'/category/f01-active' in urls)
check('inactive_category_absent', origin+'/category/f01-inactive' not in urls)
for slug in ['f01-draft', 'f01-inactive-post', 'f01-future-visible-marker', 'f01-duplicate-title']:
    check('hidden_'+slug, origin+'/post/'+slug not in urls)
check('legacy_uses_publication_not_created_date', datetime.fromisoformat(urls[origin+'/post/f45-legacy']) == datetime.fromisoformat('2020-01-01T00:00:00+00:00'))
check('revision_uses_real_updated_date', datetime.fromisoformat(urls[origin+'/post/f45-revision']) == datetime.fromisoformat('2021-02-03T04:05:06+00:00'))
check('draft_edit_before_publication_uses_publication', urls[origin+'/post/f45-draft-revision'] == urls[origin+'/post/f45-legacy'])
check('unversioned_home_category_omit_lastmod', urls[origin+'/'] is None and urls[origin+'/category/f01-active'] is None)
check('every_lastmod_is_UTC', all(v is None or v.endswith('Z') for v in urls.values()))
check('unicode_reserved_slug_escaped', any('f45-%C4%9F%C3%BC%C5%9F' in u and ('%26' in u or '&quoted' in u) for u in urls))
check('XML_ampersand_safe', b'&quoted' not in data)
_, _, spoof = fetch('/sitemap.xml', headers={'Host':'attacker.example','X-Forwarded-Host':'attacker.example','X-Forwarded-Proto':'http'})
check('spoofed_host_ignored', spoof == data)
status, rh, robots = fetch('/robots.txt', headers={'Host':'attacker.example'})
check('robots_configured_sitemap', status == 200 and robots.decode('utf-8') == 'User-agent: *\nAllow: /\nSitemap: '+origin+'/sitemap.xml\n')
check('robots_plain_UTF8_no_store', rh['Content-Type'] == 'text/plain; charset=utf-8' and 'no-store' in rh['Cache-Control'])
status, hh, head = fetch('/sitemap.xml', 'HEAD')
check('HEAD_no_body_same_content_type', status == 200 and head == b'' and hh['Content-Type'] == headers['Content-Type'])
check('reads_do_not_change_any_post', snapshot() == before)
check('post_not_accepted_as_crawler_read', fetch('/sitemap.xml', 'POST')[0] in [400, 405])
admin, _ = cookie_opener()
_, _, status, url, _ = submit_login(admin, base, 'f17-admin', os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
check('admin_login', status == 200 and '/Admin/Dashboard' in url)
def edit(overrides):
    r = json.loads(sql('SELECT row_to_json(p) FROM "Posts" p WHERE "Id"=4502'))
    _, _, form = request(admin, base+'/AdminPost/Edit/4502')
    fields = {k:str(r[k]) for k in ['Title','Content','Summary','Excerpt','CategoryId']}
    fields.update(Id='4502', PublishDate=datetime.fromisoformat(r['PublishDate']).astimezone(ZoneInfo('Europe/Istanbul')).strftime('%Y-%m-%dT%H:%M:%S'), IsActive=str(r['IsActive']).lower(), SaveAction='Save', EditVersion=extract_hidden_value(form,'EditVersion'), __RequestVerificationToken=extract_antiforgery_token(form))
    fields.update(overrides)
    return request(admin,base+'/AdminPost/Edit/4502',data=fields)
check('draft_save_success', edit({'SaveAction':'SaveDraft'})[0] == 200)
check('draft_removal_immediate', origin+'/post/f45-revision' not in rows()[0])
check('publish_success', edit({'SaveAction':'Publish'})[0] == 200)
check('publish_inclusion_immediate', origin+'/post/f45-revision' in rows()[0])
check('meaningful_edit_success', edit({'Content':'F45 meaningful revision body'})[0] == 200)
r = json.loads(sql('SELECT row_to_json(p) FROM "Posts" p WHERE "Id"=4502'))
check('lastmod_refresh_matches_persisted_UTC', datetime.fromisoformat(rows()[0][origin+'/post/f45-revision']) == datetime.fromisoformat(r['UpdatedDate']))
_, _, form = request(admin,base+'/AdminCategory')
status, _, _ = request(admin,base+'/AdminCategory/Delete/4501',data={'__RequestVerificationToken':extract_antiforgery_token(form)})
check('category_delete_success', status == 200 and sql('SELECT count(*) FROM "Categories" WHERE "Id"=4501') == '0')
check('category_delete_immediate', origin+'/category/f45-empty' not in rows()[0])
sql('UPDATE "Categories" SET "IsActive"=false WHERE "Id"=1001')
check('category_deactivation_removes_category_and_posts', origin+'/category/f01-active' not in rows()[0] and origin+'/post/f45-legacy' not in rows()[0])
sql('UPDATE "Categories" SET "IsActive"=true WHERE "Id"=1001')
check('category_reactivation_visible', origin+'/post/f45-legacy' in rows()[0])
sql('UPDATE "Posts" SET "PublishDate"=CURRENT_TIMESTAMP + interval \'3 seconds\' WHERE "Id"=4503')
check('future_boundary_hidden', origin+'/post/f45-draft-revision' not in rows()[0])
time.sleep(3.5)
check('publication_boundary_visible_without_write', origin+'/post/f45-draft-revision' in rows()[0])
assert sql('SELECT count(*) FROM "Posts" WHERE "Id" BETWEEN 60000 AND 110000') == '0'
sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion")
SELECT i,'F45 limit','f45-limit-'||i,'Summary','Body',1001,'2020-01-01Z',true,0,'2020-01-01Z','',true,'',1 FROM generate_series(60000,110000) i;''')
try:
    status, limit_headers, limit_body = fetch('/sitemap.xml')
    check('protocol_overflow_returns_503_not_truncated_XML', status == 503 and b'<urlset' not in limit_body and 'no-store' in limit_headers['Cache-Control'])
finally:
    sql('DELETE FROM "Posts" WHERE "Id" BETWEEN 60000 AND 110000')
check('normal_sitemap_recovers_after_overflow', fetch('/sitemap.xml')[0] == 200)
check('robots_HEAD_no_body', fetch('/robots.txt', 'HEAD')[0] == 200 and fetch('/robots.txt', 'HEAD')[2] == b'')
print(json.dumps({'checks':checks,'passed':len(checks),'postgres_version':sql('SHOW server_version')}, indent=2))
