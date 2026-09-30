#!/usr/bin/env python3
"""Check shared editor form contracts and saves with fixture storage only."""
import argparse
import base64
import json
import os
from http_probe_support import (cookie_opener, submit_login, request, post_file,
                                extract_antiforgery_token, extract_hidden_value)
from f11_optional_thumbnail_probe import post_fields

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', required=True)
args = parser.parse_args()
base = args.base_url.rstrip('/')
opener, cookies = cookie_opener()
_, _, status, url, _ = submit_login(opener, base,
    os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url
checks = {}
png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
for mode, path in [('create', '/AdminPost/Create'), ('edit', '/AdminPost/Edit/2001')]:
    status, _, page = request(opener, base + path)
    token = extract_antiforgery_token(page)
    checks[mode + '_shared_contract'] = (status == 200 and token is not None
        and f'data-mode="{mode}"' in page
        and 'data-upload-url="/AdminPost/UploadEditorImage"' in page
        and page.count('/js/post-editor.js?') == 1
        and 'admin-post-editor.js' not in page
        and 'type="datetime-local" step="1"' in page)
    fields = post_fields('F30 ' + mode + ' with cover', 'F30 synthetic Markdown content',
        post_id=2001 if mode == 'edit' else None)
    if mode == 'edit':
        fields['EditVersion'] = extract_hidden_value(page, 'EditVersion') or ''
    status, url, body = post_file(opener, base + path, token or '',
        file_field='thumbnailFile', file_name='valid.png', content_type='image/png',
        content=png, fields=fields)
    checks[mode + '_cover_save_with_fixture_storage'] = (
        status == 200 and url.rstrip('/').endswith('/AdminPost')
        and fields['Title'] in body)
    bad_fields = dict(fields, CategoryId='', Title='F30 <script>alert(1)</script> "quoted"')
    bad_fields['__RequestVerificationToken'] = token or ''
    if mode == 'edit':
        _, _, after = request(opener, base + path)
        bad_fields['EditVersion'] = extract_hidden_value(after, 'EditVersion') or ''
    status, url, body = request(opener, base + path, data=bad_fields)
    checks[mode + '_validation_retains_encoded_input'] = (
        status == 200 and path in url and 'validation-summary-errors' in body
        and '&lt;script&gt;' in body and '<script>alert(1)</script>' not in body
        and 'F30 synthetic Markdown content' in body)
    status, _, body = post_file(opener, base + '/AdminPost/UploadEditorImage', token or '',
        file_field='file', file_name='valid.png', content_type='image/png', content=png)
    payload = json.loads(body)
    checks[mode + '_editor_upload_with_fixture_storage'] = (
        status == 200 and payload.get('success') is True
        and payload.get('url') == 'https://images.example.test/f30.png')
    status, _, _ = post_file(opener, base + '/AdminPost/UploadEditorImage', '',
        file_field='file', file_name='valid.png', content_type='image/png', content=png)
    checks[mode + '_editor_upload_requires_csrf'] = status == 400
print(json.dumps({'checks': checks, 'storage': 'synthetic; Cloudinary not verified'}, indent=2))
raise SystemExit(0 if all(checks.values()) else 1)
