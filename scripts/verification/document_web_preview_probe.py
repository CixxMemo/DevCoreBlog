#!/usr/bin/env python3
"""Preview security and non-persistence against an explicitly owned loopback fixture."""
import argparse
import hashlib
import http.client
import json
import os
import subprocess
from urllib.parse import urlparse
from http_probe_support import (cookie_opener, submit_login, request_with_headers,
    extract_antiforgery_token, is_private_admin_challenge)

p = argparse.ArgumentParser()
p.add_argument('--base-url', required=True)
p.add_argument('--database-port', required=True)
p.add_argument('--report', required=True)
a = p.parse_args()
assert urlparse(a.base_url).hostname == '127.0.0.1'
checks = {}
base = a.base_url.rstrip('/')
path = base + '/AdminDocument/Preview'

def snapshot():
    data = subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', a.database_port,
        '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c',
        'SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'])
    return hashlib.sha256(data).hexdigest()

def private(response):
    headers = {k.lower(): v for k, v in response[3].items()}
    return 'no-store' in headers.get('cache-control', '') and 'noindex' in headers.get('x-robots-tag', '')

before = snapshot()
visitor, _ = cookie_opener()
checks['anonymous_get_denied'] = is_private_admin_challenge(request_with_headers(visitor, path))
denied_post = request_with_headers(visitor, path, data={'Input.DocumentJson': '{}'})
# The private 404 error view is re-executed on POST; global antiforgery can return 400.
checks['anonymous_post_denied_without_output_or_redirect'] = denied_post[0] in (400, 404) and 'no-store' in denied_post[3].get('Cache-Control', '') and 'Location' not in denied_post[3] and 'class="document-content"' not in denied_post[2]
admin, cookies = cookie_opener()
_, _, status, url, _ = submit_login(admin, base, os.environ.get('DEVCORE_TEST_ADMIN_USERNAME', 'f17-admin'), os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url
initial = request_with_headers(admin, path)
checks['private_get_empty_form'] = initial[0] == 200 and private(initial) and 'class="document-content"' not in initial[2]
token = extract_antiforgery_token(initial[2])
assert token
empty = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [{'type': 'paragraph'}]}})

def preview(document, **extra):
    return request_with_headers(admin, path, data={'Input.DocumentJson': document, '__RequestVerificationToken': token, **extra})

checks['missing_csrf_rejected'] = request_with_headers(admin, path, data={'Input.DocumentJson': empty})[0] == 400
checks['invalid_csrf_rejected'] = preview(empty, __RequestVerificationToken='invalid')[0] == 400
success = preview(empty, Id='2003', IsPublished='true', Html='<script>attack()</script>')
checks['valid_html_private_no_receipt'] = success[0] == 200 and private(success) and 'class="document-content"' in success[2] and 'post-recovery-receipt' not in success[2]
checks['overposted_html_not_rendered'] = '<script>attack()</script>' not in success[2]
checks['strict_csp'] = "style-src-attr 'none'" in success[3].get('Content-Security-Policy', '') and 'unsafe-' not in success[3].get('Content-Security-Policy', '')
for name, document in [('missing', ''), ('invalid_json', '{'), ('raw_html_node', '{"version":1,"document":{"type":"doc","content":[{"type":"html"}]}}'),
    ('unsafe_url', '{"version":1,"document":{"type":"doc","content":[{"type":"image","attrs":{"src":"https://u:p@example.test/x"}}]}}')]:
    r = preview(document)
    checks[name + '_400_without_partial'] = r[0] == 400 and private(r) and 'class="document-content"' not in r[2]
    if name == 'invalid_json':
        checks['failed_input_retained_in_encoded_form'] = '>\n{</textarea>' in r[2]
for name, document in [('byte_limit', ' ' * (1048577 - len(empty)) + empty),
    ('node_limit', json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [{'type': 'paragraph'}] * 10000}}))]:
    r = preview(document)
    checks[name + '_413_without_partial'] = r[0] == 413 and private(r) and 'class="document-content"' not in r[2]
at = preview(' ' * (1048576 - len(empty)) + empty)
checks['max_document_byte_boundary_accepted'] = at[0] == 200
# Read the early size rejection before sending megabytes to an already closed socket.
target = urlparse(path)
connection = http.client.HTTPConnection(target.hostname, target.port, timeout=5)
connection.putrequest('POST', target.path)
for key, value in {'Content-Length': str(1048576 * 3 + 65537),
    'Content-Type': 'application/x-www-form-urlencoded', 'X-CSRF-TOKEN': token,
    'Cookie': '; '.join(c.name + '=' + c.value for c in cookies)}.items():
    connection.putheader(key, value)
connection.endheaders()
wire_response = connection.getresponse()
# ASP.NET antiforgery can translate the bounded form-read failure to 400 before the action.
checks['wire_body_limit_rejected_before_rendering'] = wire_response.status in (400, 413) and 'no-store' in (wire_response.getheader('Cache-Control') or '') and 'class="document-content"' not in wire_response.read().decode('utf-8')
connection.close()
checks['all_rows_versions_counters_unchanged'] = snapshot() == before
from pathlib import Path
Path(a.report).write_text(json.dumps({'count': len(checks), 'checks': checks, 'scope': 'Owned synthetic PostgreSQL only; no real dotenv or credentials.'}, indent=2) + '\n')
print(json.dumps({'count': len(checks), 'failed': [k for k, v in checks.items() if not v]}))
assert all(checks.values()), 'Document preview acceptance failed'
