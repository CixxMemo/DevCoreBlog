#!/usr/bin/env python3
"""Check private MVC preview and unchanged rows against disposable PostgreSQL."""
import argparse
import hashlib
import json
import os
import subprocess
from http_probe_support import (
    is_private_admin_challenge,cookie_opener, submit_login, request, request_with_headers,
    extract_antiforgery_token, extract_hidden_value, multipart_payload)
from f01_http_baseline import extract_markdown_content, build_f04_checks
from f11_optional_thumbnail_probe import post_fields

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', default='http://127.0.0.1:15166')
parser.add_argument('--pg-port', default='55439')
args = parser.parse_args()
base = args.base_url.rstrip('/')
admin, _ = cookie_opener()
_, _, status, url, _ = submit_login(admin, base,
    os.environ.get('DEVCORE_TEST_ADMIN_USERNAME', 'f17-admin'), os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url, 'Fixture login failed'
visitor, _ = cookie_opener()
checks = {}

def sql(statement):
    return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', args.pg_port,
        '-U', os.environ['USER'], '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1',
        '-c', statement]).decode().strip()

def snapshot():
    rows = sql('SELECT row_to_json(p) FROM "Posts" p ORDER BY "Id"; SELECT row_to_json(c) FROM "Categories" c ORDER BY "Id";')
    return hashlib.sha256(rows.encode()).hexdigest()

before = snapshot()
_, _, create = request(admin, base + '/AdminPost/Create')
token = extract_antiforgery_token(create)
fields = post_fields('F33 unsaved preview', '# F33_UNSAVED\n\n**Only in the preview**')
fields['__RequestVerificationToken'] = token
s, _, body, headers = request_with_headers(admin, base + '/AdminPost/Preview', data=fields)
checks['unsaved_create_returns_html_without_receipt'] = (s == 200 and 'text/html' in headers.get('Content-Type', '')
    and '<h2 id="markdown-section-1">F33_UNSAVED</h2>' in body and 'Private preview' in body and 'post-recovery-receipt' not in body)
checks['no_store_and_no_index'] = ('no-store' in headers.get('Cache-Control', '')
    and 'noindex' in headers.get('X-Robots-Tag', ''))
checks['button_is_native_separate_target_without_changing_save_form'] = ( 'formtarget="_blank"' in create
    and 'formaction="/AdminPost/Preview"' in create and 'formenctype="application/x-www-form-urlencoded"' in create
    and 'rel="noopener"' in create and 'action="/AdminPost/Create"' in create)
checks['anonymous_preview_is_not_rendered'] = is_private_admin_challenge(request_with_headers(visitor, base + '/AdminPost/Preview', data=fields))
checks['authenticated_get_cannot_render_input'] = request(admin, base + '/AdminPost/Preview')[0] in (404, 405)
checks['missing_token_rejected'] = request(admin, base + '/AdminPost/Preview', data={k:v for k,v in fields.items() if k != '__RequestVerificationToken'})[0] == 400
checks['invalid_token_rejected'] = request(admin, base + '/AdminPost/Preview', data={**fields, '__RequestVerificationToken':'invalid'})[0] == 400

for name, override, message in [
    ('blank_title', {'Title':'   '}, 'Title is required.'),
    ('blank_content', {'Content':'   '}, 'Content is required.'),
    ('long_content', {'Content':'x'*200001}, 'Content cannot exceed'),
    ('long_title', {'Title':'x'*201}, 'Title cannot exceed'),
    ('missing_category', {'CategoryId':''}, 'is invalid.'),
    ('inactive_category', {'CategoryId':'1002'}, 'Select an active category.'),
    ('unknown_category', {'CategoryId':'2147483647'}, 'Select an active category.'),
    ('invalid_date', {'PublishDate':'not-a-date'}, 'PublishDate'),
]:
    s, _, page, h = request_with_headers(admin, base + '/AdminPost/Preview', data={**fields, **override})
    checks[name+'_rejects_without_rendering'] = (s == 400 and message in page and not extract_markdown_content(page)
        and 'no-store' in h.get('Cache-Control', ''))

xss_markdown = sql('SELECT "Content" FROM "Posts" WHERE "Id"=2005;')
_, _, xss_form = request(admin, base + '/AdminPost/Edit/2005')
xss_fields = {**fields, 'Id':'2005', 'Content':xss_markdown, 'Title':'<img src=x onerror="window.__f33TitleXss=true">',
    '__RequestVerificationToken':extract_antiforgery_token(xss_form), 'SaveAction':'Publish', 'IsPublished':'true'}
s, _, preview = request(admin, base + '/AdminPost/Preview', data=xss_fields)
_, _, detail = request(admin, base + '/post/f01-markdown-xss')
checks['preview_matches_actual_detail_markup'] = (s == 200 and extract_markdown_content(preview).strip() == extract_markdown_content(detail).strip())
checks['title_is_encoded'] = '&lt;img src=x onerror=' in preview and '<img src=x' not in preview
checks.update({'xss_'+name: passed for name,passed in build_f04_checks(preview).items()})

_, _, edit = request(admin, base + '/AdminPost/Edit/2003')
s, _, draft = request(admin, base + '/AdminPost/Preview', data={**fields, 'Id':'2003', 'SaveAction':'Publish', 'IsPublished':'true',
    'ThumbnailUrl':'data:text/html,evil', '__RequestVerificationToken':extract_antiforgery_token(edit)})
checks['draft_preview_does_not_publish_and_ignores_client_cover'] = (s == 200 and 'F33_UNSAVED' in draft
    and 'data:text/html,evil' not in draft and request(visitor, base + '/post/f01-draft')[0] == 404)
raw, content_type = multipart_payload(fields, file_field='thumbnailFile', file_name='attack.html', content_type='text/html', content=b'<script>attack()</script>')
s, _, upload = request(admin, base + '/AdminPost/Preview', raw_data=raw, headers={'Content-Type':content_type})
checks['preview_never_uploads_selected_files'] = s == 200 and 'F33_UNSAVED' in upload
s, _, _, _ = request_with_headers(admin, base + '/AdminPost/Preview', raw_data=b'x'*1048577,
    headers={'Content-Type':'application/x-www-form-urlencoded','X-CSRF-TOKEN':token})
checks['oversize_body_is_rejected'] = s == 413
checks['unknown_stored_post_is_not_found'] = request(admin, base + '/AdminPost/Preview', data={**fields,'Id':'2147483647'})[0] == 404
checks['all_rows_counts_publication_versions_and_counters_unchanged'] = before == snapshot()
print(json.dumps({'checks':checks, 'count':len(checks)}, indent=2))
assert all(checks.values()), 'Failed: '+', '.join(k for k,v in checks.items() if not v)
