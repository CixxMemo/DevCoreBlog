#!/usr/bin/env python3
"""Actual MVC JSON writes on the disposable migration fixture; no injected save adapter."""
import argparse, html, json, os, re, subprocess
from datetime import datetime, timedelta, timezone
from concurrent.futures import ThreadPoolExecutor
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
def private(r): return 'no-store' in r[3].get('Cache-Control', '')
def envelope(body):
    match = re.search(r'<textarea[^>]*name="DocumentJson"[^>]*>(.*?)</textarea>', body, re.S)
    return html.unescape(match[1]).lstrip('\n') if match else ''
admin, cookies = cookie_opener()
login = submit_login(admin, base, os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert login[2] == 200
create = request_with_headers(admin, base + '/AdminWriting/Create')
token = extract_antiforgery_token(create[2])
navigation = request_with_headers(admin, base + '/AdminPost')
checks['admin_navigation_opens_private_shared_json_form'] = create[0] == 200 and '/AdminWriting/Create' in create[1] and private(create) and 'data-document-form' in create[2] and 'href="/AdminWriting/Create"' in navigation[2]
doc = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [
    {'type': 'heading', 'attrs': {'level': 2, 'textAlign': 'center'}, 'content': [{'type': 'text', 'text': 'Türkçe başlık 👋'}]},
    {'type': 'paragraph', 'content': [{'type': 'text', 'text': '<script> metin', 'marks': [
        {'type': 'bold'}, {'type': 'italic'}, {'type': 'underline'}, {'type': 'code'},
        {'type': 'link', 'attrs': {'href': 'https://example.test/ok', 'title': 'Türkçe', 'target': '_blank', 'rel': 'noopener noreferrer nofollow'}}]}, {'type': 'hardBreak'}]},
    {'type': 'blockquote', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Alıntı'}]}]},
    {'type': 'bulletList', 'content': [{'type': 'listItem', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Madde'}]}]}]},
    {'type': 'orderedList', 'attrs': {'start': 7, 'type': '1'}, 'content': [{'type': 'listItem', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Sıra'}]}]}]},
    {'type': 'codeBlock', 'attrs': {'language': 'csharp'}, 'content': [{'type': 'text', 'text': 'var örnek = 1;\n'}]}
]}}, ensure_ascii=False)
common = {'Title': 'F09 HTTP yazısı', 'DocumentJson': doc, 'CategoryId': '1001', 'Summary': 'Özet', 'Excerpt': 'Açıklama',
    'IsActive': 'true', 'PublishDate': field(create[2], 'PublishDate'), 'SaveAction': 'SaveDraft', '__RequestVerificationToken': token}
before = snapshot()
anonymous = request_with_headers(cookie_opener()[0], base + '/AdminWriting/Create', data=common)
no_csrf = request_with_headers(admin, base + '/AdminWriting/Create', data={k:v for k,v in common.items() if k != '__RequestVerificationToken'})
checks['write_requires_admin_and_csrf_without_mutation'] = anonymous[0] in (400,404) and no_csrf[0] == 400 and snapshot() == before
missing = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': ''})
invalid = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': '{', 'Title': 'Korunan başlık'})
checks['missing_and_invalid_document_400_preserves_encoded_input'] = missing[0] == invalid[0] == 400 and private(invalid) and envelope(invalid[2]) == '{' and field(invalid[2], 'Title') == 'Korunan başlık' and snapshot() == before
large = ' ' * (1048577 - len(doc.encode())) + doc
limited = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': large})
checks['one_mib_utf8_limit_413_retains_document_without_write'] = limited[0] == 413 and private(limited) and envelope(limited[2]) == large and snapshot() == before
badcat = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'CategoryId': '999999'})
badtitle = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'Title': ' '})
checks['metadata_validation_has_no_partial_document_insert'] = badcat[0] == badtitle[0] == 400 and snapshot() == before
created = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'Id': '2001', 'EditVersion': '999',
    'Slug': 'forged', 'ContentKind': 'Newsletter', 'AccessScope': 'Subscribers', 'DocumentPlainText': 'forged', 'IsPublished': 'true', 'ViewCount': '999'})
id = int(field(created[2], 'Id'))
checks['create_is_single_complete_json_row_and_server_owned_identity'] = created[0] == 200 and id != 2001 and private(created) and envelope(created[2]) == doc and sql(f'SELECT "Content" = \'\' AND "DocumentVersion"=1 AND "EditVersion"=1 AND "ViewCount"=0 AND NOT "IsPublished" AND "ContentKind"=0 AND "AccessScope"=0 FROM "Posts" WHERE "Id"={id};') == 't'
checks['server_derives_reading_without_trusting_posted_facts'] = 'Türkçe başlık' in sql(f'SELECT "DocumentPlainText" FROM "Posts" WHERE "Id"={id};') and sql(f'SELECT "DocumentWordCount">0 AND "DocumentReadingMinutes"=1 FROM "Posts" WHERE "Id"={id};') == 't'
url = base + f'/AdminWriting/Edit/{id}'
edit = request_with_headers(admin, base + f'/AdminPost/Edit/{id}')
checks['saved_json_reopens_via_existing_admin_edit_link'] = edit[0] == 200 and edit[1] == url and envelope(edit[2]) == doc
token = extract_antiforgery_token(edit[2])
editform = {**common, 'Id': str(id), 'EditVersion': '1', '__RequestVerificationToken': token, 'SaveAction': 'Save', 'PublishDate': field(edit[2], 'PublishDate')}
protected = sql(f'SELECT "Slug","CreatedDate","Content","ThumbnailUrl","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id};')
sql(f'UPDATE "Posts" SET "ViewCount"="ViewCount"+11 WHERE "Id"={id};')
saved = request_with_headers(admin, url, data={**editform, 'Title': 'F09 ilk değişiklik', 'Summary': 'İlk özet', 'ViewCount': '0', 'Slug': 'changed'})
checks['edit_atomically_saves_metadata_document_and_advances_revision'] = saved[0] == 200 and field(saved[2], 'EditVersion') == '2' and envelope(saved[2]) == doc and sql(f'SELECT "Title"=\'F09 ilk değişiklik\' AND "Summary"=\'İlk özet\' AND "UpdatedDate" IS NOT NULL FROM "Posts" WHERE "Id"={id};') == 't'
checks['edit_preserves_counter_slug_creation_legacy_and_access_fields'] = protected == sql(f'SELECT "Slug","CreatedDate","Content","ThumbnailUrl","ContentKind","AccessScope" FROM "Posts" WHERE "Id"={id};') and sql(f'SELECT "ViewCount" FROM "Posts" WHERE "Id"={id};') == '11'
before = snapshot()
stale = request_with_headers(admin, url, data={**editform, 'Title': 'F09 stale korunacak', 'DocumentJson': doc.replace('Alıntı','Kaybolmayan metin')})
checks['stale_409_keeps_submitted_document_metadata_and_old_revision'] = stale[0] == 409 and private(stale) and envelope(stale[2]) == doc.replace('Alıntı','Kaybolmayan metin') and field(stale[2], 'Title') == 'F09 stale korunacak' and field(stale[2], 'EditVersion') == '1' and snapshot() == before
checks['strict_200_400_409_csp_never_needs_inline_or_eval'] = all("style-src-attr 'none'" in r[3].get('Content-Security-Policy','') and "script-src 'self'" in r[3].get('Content-Security-Policy','') and 'unsafe-eval' not in r[3].get('Content-Security-Policy','') for r in (created, invalid, stale))
unsafe = request_with_headers(admin, url + '?returnUrl=https://example.test', data={**editform, 'EditVersion': '2'})
checks['unsafe_return_url_rejected_before_mutation'] = unsafe[0] == 400 and snapshot() == before
def race(title):
    http = __import__('urllib.request', fromlist=['build_opener', 'HTTPCookieProcessor'])
    worker = http.build_opener(http.HTTPCookieProcessor(cookies))
    return request_with_headers(worker, url, data={**editform, 'EditVersion':'2', 'Title':title})[0]
with ThreadPoolExecutor(max_workers=2) as pool: statuses = list(pool.map(race, ['F09 yarış A','F09 yarış B']))
checks['concurrent_http_edits_have_one_winner_and_one_409'] = sorted(statuses) == [200,409] and sql(f'SELECT "EditVersion" FROM "Posts" WHERE "Id"={id};') == '3'
fresh = request_with_headers(admin, url)
current = {**editform, 'EditVersion':'3', 'PublishDate':field(fresh[2], 'PublishDate')}
before = snapshot()
past_schedule = request_with_headers(admin, url, data={**current, 'SaveAction':'Schedule', 'PublishDate':'2020-01-01T00:00:00'})
bad_action = request_with_headers(admin, url, data={**current, 'SaveAction':'999'})
checks['invalid_action_and_past_schedule_preserve_document_without_write'] = past_schedule[0] == bad_action[0] == 400 and snapshot() == before
site = timezone(timedelta(hours=3))
future = (datetime.now(site) + timedelta(days=2)).replace(microsecond=0)
scheduled = request_with_headers(admin, url, data={**current, 'SaveAction':'Schedule', 'PublishDate':future.isoformat(timespec='seconds')[:19]})
checks['schedule_uses_explicit_future_site_time_in_utc'] = scheduled[0] == 200 and sql(f'SELECT "IsPublished" FROM "Posts" WHERE "Id"={id};') == 't' and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id};')) == future.astimezone(timezone.utc)
now_before = datetime.now(timezone.utc)
published = request_with_headers(admin, url, data={**current, 'EditVersion':'4', 'SaveAction':'Publish', 'PublishDate':future.isoformat(timespec='seconds')[:19]})
publish_date = datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id};'))
checks['publish_now_uses_server_utc_and_save_preserves_exact_instant'] = published[0] == 200 and now_before <= publish_date <= datetime.now(timezone.utc)
kept = request_with_headers(admin, url, data={**current, 'EditVersion':'5', 'SaveAction':'Save', 'PublishDate':'2020-01-01T00:00:00'})
checks['normal_save_keeps_publication_flag_and_saved_date'] = kept[0] == 200 and sql(f'SELECT "IsPublished" FROM "Posts" WHERE "Id"={id};') == 't' and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id};')) == publish_date
fresh = request_with_headers(admin, url)
default_draft = request_with_headers(admin, url, data={k:v for k,v in {**current, 'EditVersion':'6', 'PublishDate':field(fresh[2],'PublishDate')}.items() if k != 'SaveAction'})
checks['missing_action_is_safe_draft_and_unchanged_subseconds_are_preserved'] = default_draft[0] == 200 and sql(f'SELECT NOT "IsPublished" FROM "Posts" WHERE "Id"={id};') == 't' and datetime.fromisoformat(sql(f'SELECT "PublishDate" FROM "Posts" WHERE "Id"={id};')) == publish_date
before = snapshot()
legacy = request_with_headers(admin, base + '/AdminWriting/Edit/2001', data={**editform, 'Id':'2001', 'EditVersion':'1'})
checks['json_writer_never_silently_converts_legacy_row'] = legacy[0] == 409 and snapshot() == before
Path(a.report).write_text(json.dumps({'count':len(checks),'checks':checks,'fixturePostId':id,'concurrentStatuses':statuses,'scope':'Actual MVC and disposable PostgreSQL; no real application data.'},indent=2)+'\n')
print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}))
assert all(checks.values())
