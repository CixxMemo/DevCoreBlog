#!/usr/bin/env python3
"""Verify MVC receipts only follow successful saves against an isolated fixture."""
import argparse
import json
import os
import uuid
from http_probe_support import (cookie_opener, submit_login, request,
    extract_antiforgery_token, extract_hidden_value)
from f11_optional_thumbnail_probe import post_fields

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', default='http://127.0.0.1:15163')
args = parser.parse_args()
base = args.base_url.rstrip('/')
opener, _ = cookie_opener()
_, _, status, url, _ = submit_login(opener, base,
    os.environ.get('DEVCORE_TEST_ADMIN_USERNAME', 'f17-admin'),
    os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url, 'Fixture login failed'
checks = {}
receipt = 'id="post-recovery-receipt"'
revision = str(uuid.uuid4())
_, _, query = request(opener, base + '/AdminPost?SavedPostRecoveryKey=devcore_editor_draft_create&SavedPostRecoveryRevision=' + revision)
checks['query_cannot_forge_receipt'] = receipt not in query
_, _, form = request(opener, base + '/AdminPost/Create')
create_marker = 'F31_HTTP_CREATE_' + uuid.uuid4().hex[:8]
fields = post_fields(create_marker, 'F31 HTTP recovery content')
fields.update(__RequestVerificationToken=extract_antiforgery_token(form), recoveryRevision=revision)
invalid = {**fields, 'CategoryId': ''}
s, u, body = request(opener, base + '/AdminPost/Create', data=invalid)
_, _, index = request(opener, base + '/AdminPost')
checks['invalid_create_retains_text_without_receipt_or_write'] = (
    s == 200 and '/Create' in u and create_marker in body and receipt not in body
    and receipt not in index and create_marker not in index)
s, u, body = request(opener, base + '/AdminPost/Create', data={**fields, 'recoveryRevision': 'malformed'})
checks['malformed_revision_fails_without_receipt'] = s == 200 and '/Create' in u and receipt not in body
s, u, body = request(opener, base + '/AdminPost/Create', data=fields)
checks['successful_create_has_exact_server_key_and_revision'] = (
    s == 200 and u.endswith('/AdminPost') and receipt in body
    and 'data-recovery-key="devcore_editor_draft_create"' in body
    and f'data-recovery-revision="{revision}"' in body and create_marker in body)
_, _, body = request(opener, base + '/AdminPost')
checks['receipt_is_consumed_once'] = receipt not in body
_, _, edit = request(opener, base + '/AdminPost/Edit/2006')
efields = post_fields('F31_HTTP_EDIT', 'F31 edit retained body', post_id=2006)
efields.update(__RequestVerificationToken=extract_antiforgery_token(edit),
    EditVersion=extract_hidden_value(edit, 'EditVersion'), recoveryRevision=revision)
s, u, body = request(opener, base + '/AdminPost/Edit/2006', data={**efields, 'CategoryId': ''})
checks['invalid_edit_retains_text_without_receipt'] = (
    s == 200 and '/Edit/2006' in u and 'F31 edit retained body' in body and receipt not in body)
s, u, body = request(opener, base + '/AdminPost/Edit/2006', data=efields)
checks['successful_edit_has_exact_server_key_and_revision'] = (
    s == 200 and u.endswith('/AdminPost') and receipt in body
    and 'data-recovery-key="devcore_editor_draft_edit_2006"' in body
    and f'data-recovery-revision="{revision}"' in body)
s, u, body = request(opener, base + '/AdminPost/Edit/2006', data={**efields, 'Title': 'F31_STALE_EDIT'})
_, _, index = request(opener, base + '/AdminPost')
checks['stale_edit_409_retains_text_without_receipt_or_write'] = (
    s == 409 and 'F31_STALE_EDIT' in body and receipt not in body
    and receipt not in index and 'F31_STALE_EDIT' not in index)
print(json.dumps({'checks': checks, 'count': len(checks)}, indent=2))
assert all(checks.values()), 'F31 receipt verification failed'
