#!/usr/bin/env python3
"""Exercise the owned test adapter and the real legacy edit boundary without real application data."""
import argparse
import hashlib
import json
import os
import subprocess
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
        '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c', query]).decode().strip()
def snapshot():
    return hashlib.sha256(sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";').encode()).hexdigest()
def private(r): return 'no-store' in r[3].get('Cache-Control', '')

admin, _ = cookie_opener()
_, _, status, url, _ = submit_login(admin, base, os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url
edit = request_with_headers(admin, base + '/AdminPost/Edit/2003')
token = extract_antiforgery_token(edit[2]); assert token
expected = int(sql('SELECT "EditVersion" FROM "Posts" WHERE "Id"=2003;'))
document = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [
    {'type': 'paragraph', 'content': [{'type': 'text', 'text': 'F08 Türkçe belge 👋'}]}]}}, ensure_ascii=False)
def save(body=document, version=expected, **extra):
    return request_with_headers(admin, base + '/fixture-document/2003', data={
        'expectedEditVersion': str(version), 'documentJson': body, '__RequestVerificationToken': token, **extra})
before = snapshot()
missing = save('')
checks['adapter_rejects_missing_document_before_service'] = missing[0] == 400 and private(missing) and snapshot() == before
anonymous = request_with_headers(cookie_opener()[0], base + '/fixture-document/2003', data={'documentJson': document})
checks['adapter_requires_admin_and_antiforgery'] = anonymous[0] in (400, 404) and request_with_headers(admin,
    base + '/fixture-document/2003', data={'expectedEditVersion': str(expected), 'documentJson': document})[0] == 400
invalid = save('{')
oversized = save(' ' * (1048577 - len(document.encode('utf-8'))) + document)
checks['adapter_maps_schema_400_limit_413_without_mutation'] = invalid[0] == 400 and oversized[0] == 413 and private(invalid) and private(oversized) and snapshot() == before
protected_before = sql('SELECT to_jsonb(p) - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ - $$EditVersion$$ - $$UpdatedDate$$ FROM "Posts" p WHERE "Id"=2003;')
ok = save(DocumentPlainText='forged', ViewCount='9999', Slug='forged', IsPublished='true')
checks['adapter_commits_real_service_document_only'] = ok[0] == 200 and private(ok) and json.loads(ok[2])['editVersion'] == expected + 1 and sql('SELECT "DocumentPlainText" FROM "Posts" WHERE "Id"=2003;') == 'F08 Türkçe belge 👋'
protected_after = sql('SELECT to_jsonb(p) - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ - $$EditVersion$$ - $$UpdatedDate$$ FROM "Posts" p WHERE "Id"=2003;')
checks['overposted_derived_and_protected_fields_are_ignored'] = protected_before == protected_after
before = snapshot()
conflict = save()
checks['adapter_maps_real_stale_service_result_to_409'] = conflict[0] == 409 and private(conflict) and snapshot() == before

# This is the actual application controller, not the injected adapter.
fresh = request_with_headers(admin, base + '/AdminPost/Edit/2003')
legacy = request_with_headers(admin, base + '/AdminPost/Edit/2003', data={
    'Id': '2003', 'EditVersion': str(expected + 1), 'Title': 'F08 retained user title',
    'Content': 'F08 retained user Markdown', 'CategoryId': '1001', 'Summary': '', 'Excerpt': '',
    'IsActive': 'true', 'SaveAction': 'Save', 'PublishDate': field(fresh[2], 'PublishDate'),
    '__RequestVerificationToken': extract_antiforgery_token(fresh[2])})
checks['real_legacy_edit_returns_409_and_retains_input'] = legacy[0] == 409 and private(legacy) and 'value="F08 retained user title"' in legacy[2] and 'F08 retained user Markdown' in legacy[2] and 'JSON belge' in legacy[2]
checks['real_legacy_rejection_preserves_every_database_field'] = snapshot() == before
checks['real_409_preserves_enforcing_csp'] = "script-src 'self'" in legacy[3].get('Content-Security-Policy', '') and "unsafe-eval" not in legacy[3].get('Content-Security-Policy', '')
Path(a.report).write_text(json.dumps({'count': len(checks), 'checks': checks,
    'scope': 'Owned PostgreSQL and injected test adapter for future HTTP translation; legacy Edit is the real controller. No production document-save endpoint added.'}, indent=2) + '\n')
print(json.dumps({'count': len(checks), 'failed': [k for k, v in checks.items() if not v]}))
assert all(checks.values())
