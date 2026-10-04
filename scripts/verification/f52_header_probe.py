#!/usr/bin/env python3
"""Check the real HTML-only, static policy and cache contract on the disposable host."""
import argparse, http.client, json, os
from urllib.parse import urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers
p = argparse.ArgumentParser()
p.add_argument('--base-url', required=True)
p.add_argument('--expect-unavailable-database', action='store_true')
a = p.parse_args()
base = a.base_url.rstrip('/')
assert urlparse(base).hostname == '127.0.0.1'
visitor, _ = cookie_opener()
def security_headers(headers):
    return headers.get('X-Content-Type-Options') == 'nosniff' and headers.get('Referrer-Policy') == 'strict-origin-when-cross-origin' and headers.get('X-Frame-Options') == 'DENY'

if a.expect_unavailable_database:
    status, _, body, headers = request_with_headers(visitor, base + '/post/f01-markdown-xss')
    asset_status, _, _, asset_headers = request_with_headers(visitor, base + '/css/error-page.css')
    checks = {
        'database_failure_preserves_real_500': status == 500,
        'error_security_headers': security_headers(headers) and security_headers(asset_headers),
        'error_500_keeps_enforced_header_after_response_clear': bool(headers.get('Content-Security-Policy')) and 'Content-Security-Policy-Report-Only' not in headers,
        'error_500_remains_no_store': 'no-store' in headers.get('Cache-Control', ''),
        'error_500_has_no_stack_or_sql': 'Npgsql' not in body and 'StackTrace' not in body and 'SELECT ' not in body,
        'error_css_loads_without_database_or_inline_style': asset_status == 200 and 'Content-Security-Policy' not in asset_headers and '<style>' not in body,
    }
    print(json.dumps({'checks': checks, 'count': len(checks)}, indent=2))
    assert all(checks.values()), [key for key, value in checks.items() if not value]
    raise SystemExit(0)
admin, _ = cookie_opener()
submit_login(admin, base, os.environ.get('DEVCORE_TEST_ADMIN_USERNAME', 'f17-admin'), os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
checks = {}
for route in ['/', '/category/f01-active', '/Account/Login', '/Admin/Dashboard', '/AdminCategory/Create', '/not-found-f52', '/AdminPost/Create', '/AdminPost/Edit/2005', '/AdminPost/Edit/99999999']:
    opener = admin if route.startswith('/Admin') else visitor
    status, _, body, headers = request_with_headers(opener, base + route)
    policy = headers.get('Content-Security-Policy', '')
    checks[route + '_security_headers'] = security_headers(headers)
    checks[route + '_enforced_html'] = status in (200, 404) and bool(policy) and 'Content-Security-Policy-Report-Only' not in headers
    checks[route + '_local_scripts_no_eval'] = "script-src 'self'; script-src-attr 'none'" in policy and 'unsafe-eval' not in policy and '*' not in policy
    editor = status == 200 and (route.startswith('/AdminPost/Create') or route.startswith('/AdminPost/Edit'))
    checks[route + '_style_scope'] = ("style-src-attr 'unsafe-inline'" if editor else "style-src-attr 'none'") in policy
for route in ['/js/public-theme.js', '/css/admin-shell.css', '/api/categories', '/rss.xml', '/sitemap.xml', '/robots.txt']:
    status, _, body, headers = request_with_headers(visitor, base + route)
    checks[route + '_security_headers'] = security_headers(headers)
    checks[route + '_no_html_policy_on_non_html'] = status == 200 and 'Content-Security-Policy' not in headers
first = request_with_headers(visitor, base + '/')
second = request_with_headers(visitor, base + '/')
checks['cached_html_has_stable_policy_and_body_without_nonce'] = first[2] == second[2] and first[3].get('Content-Security-Policy') == second[3].get('Content-Security-Policy') and 'nonce-' not in second[3].get('Content-Security-Policy', '')
checks['output_cache_was_observed'] = 'Age' in second[3]
status, _, body, headers = request_with_headers(visitor, base + '/Account/Login', headers={'Host': 'evil.example'})
# The framework startup host filter rejects before the application pipeline.
checks['pre_pipeline_host_rejection_is_empty_400'] = status == 400 and body == ''
status, _, _, headers = request_with_headers(admin, base + '/Account/Logout', data={})
checks['application_csrf_400_keeps_security_headers'] = status == 400 and security_headers(headers)
connection = http.client.HTTPConnection('127.0.0.1', urlparse(base).port, timeout=5)
connection.request('HEAD', '/')
response = connection.getresponse()
checks['head_keeps_same_policy_and_security_headers'] = response.status == 200 and response.getheader('Content-Security-Policy') == second[3].get('Content-Security-Policy') and security_headers(dict(response.getheaders())) and response.read() == b''
connection.close()
print(json.dumps({'checks': checks, 'count': len(checks)}, indent=2))
assert all(checks.values()), [key for key, value in checks.items() if not value]
