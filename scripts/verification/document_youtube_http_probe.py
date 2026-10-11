#!/usr/bin/env python3
"""YouTube admission, persistence and private preview against owned PostgreSQL/MVC."""
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
video_id = 'jNQXAC9IVRw'
sources = [f'https://youtu.be/{video_id}', f'https://www.youtube.com/watch?v={video_id}',
    f'https://youtube.com/watch?v={video_id}', f'https://www.youtube.com/embed/{video_id}',
    f'https://youtube.com/embed/{video_id}', f'https://www.youtube-nocookie.com/embed/{video_id}']

def snapshot():
    return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', a.database_port,
        '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c',
        'SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";'], text=True)

def envelope(body):
    match = re.search(r'<textarea[^>]*name="DocumentJson"[^>]*>(.*?)</textarea>', body, re.S)
    return html.unescape(match[1]).lstrip('\n') if match else ''

def document(attrs, count=1):
    return json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [
        {'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Korunan Türkçe metin 👋'}]},
        {'type': 'table', 'content': [{'type': 'tableRow', 'content': [
            {'type': 'tableCell', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Korunan hücre'}]}]}]}]},
        {'type': 'image', 'attrs': {'src': 'https://example.test/retained.png', 'alt': 'Korunan görsel'}},
        *[{'type': 'youtube', 'attrs': attrs} for _ in range(count)]
    ]}}, ensure_ascii=False)

admin, _ = cookie_opener()
assert submit_login(admin, base, os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2] == 200
create = request_with_headers(admin, base + '/AdminWriting/Create')
common = {'Title': 'F13 HTTP video belgesi', 'CategoryId': '1001', 'IsActive': 'true', 'SaveAction': 'SaveDraft',
    'PublishDate': field(create[2], 'PublishDate'), '__RequestVerificationToken': extract_antiforgery_token(create[2])}
before = snapshot()
invalid = [sources[0].replace('https:', 'http:'), sources[0] + '?t=1', sources[0] + '#x',
    sources[0].replace('youtu.be', 'youtu.be.evil.test'), sources[0].replace('youtu.be', 'user:pass@youtu.be'),
    sources[0].replace('youtu.be', 'youtu.be:443'), sources[0].replace(video_id, '%6a' + video_id[1:]),
    sources[0].replace(video_id, '../' + video_id), sources[0][:-1], sources[0] + 'a', sources[0] + '\n',
    sources[0].replace('https', 'HTTPS'), sources[1] + '&list=anything', '<iframe src="' + sources[5] + '"></iframe>']
cases = [(f'url_{i}', {'src': source}, 400) for i, source in enumerate(invalid)]
cases += [(name, {'src': sources[0], **attrs}, 400) for name, attrs in [
    ('event', {'onload': 'alert(1)'}), ('width', {'width': 641}), ('height', {'height': 479}),
    ('start', {'start': 1}), ('null', {'width': None}), ('string', {'width': '640'})]]
for name, attrs, status in cases:
    doc = document(attrs)
    response = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': doc})
    checks[name + '_rejects_and_retains_without_write'] = response[0] == status and envelope(response[2]) == doc and snapshot() == before
doc = document({'src': sources[0]}, 11)
response = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': doc})
checks['eleven_videos_413_without_write'] = response[0] == 413 and envelope(response[2]) == doc and snapshot() == before
no_csrf = request_with_headers(admin, base + '/AdminWriting/Create', data={k: v for k, v in {**common, 'DocumentJson': doc}.items() if k != '__RequestVerificationToken'})
checks['write_requires_csrf'] = no_csrf[0] == 400 and snapshot() == before
anon, _ = cookie_opener()
checks['anonymous_cannot_open_writing_or_preview'] = all(request_with_headers(anon, base + path)[0] == 404 for path in ['/AdminWriting/Create', '/AdminDocument/Preview'])

for i, source in enumerate(sources):
    doc = document({'src': source, 'width': 640, 'height': 480, 'start': 0}, 10 if i == 5 else 1)
    saved = request_with_headers(admin, base + '/AdminWriting/Create', data={**common, 'DocumentJson': doc})
    post_id = int(field(saved[2], 'Id'))
    checks[f'allowed_{i}_saves_and_reopens_with_text_table_image'] = saved[0] == 200 and post_id > 0 and envelope(saved[2]) == doc
    original = snapshot()
    preview_form = request_with_headers(admin, base + '/AdminDocument/Preview')
    preview = request_with_headers(admin, base + '/AdminDocument/Preview', data={
        'Input.DocumentJson': doc, '__RequestVerificationToken': extract_antiforgery_token(preview_form[2])})
    iframe_sources = re.findall(r'<iframe[^>]+src="([^"]+)"', preview[2])
    checks[f'allowed_{i}_preview_uses_only_safe_origin_without_write'] = preview[0] == 200 and iframe_sources == [sources[5]] * (10 if i == 5 else 1) and snapshot() == original
    checks[f'allowed_{i}_preview_private_and_strict_csp'] = 'no-store' in preview[3].get('Cache-Control', '') and "frame-src https://www.youtube-nocookie.com" in preview[3].get('Content-Security-Policy', '') and "style-src-attr 'none'" in preview[3].get('Content-Security-Policy', '')
    if i == 0:
        fixture_id = post_id

Path(a.report).write_text(json.dumps({'checks': checks, 'count': len(checks), 'fixturePostId': fixture_id,
    'scope': 'Real MVC and owned disposable PostgreSQL. URL references do not prove external playback.'}, ensure_ascii=False, indent=2) + '\n')
print(json.dumps({'count': len(checks), 'failed': [k for k, v in checks.items() if not v]}))
assert all(checks.values())
