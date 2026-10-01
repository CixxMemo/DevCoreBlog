#!/usr/bin/env python3
"""Verify rendered asset URLs and bytes against an isolated application fixture."""
import argparse
import json
import os
import re
from urllib.parse import urljoin
from http_probe_support import cookie_opener, submit_login, request

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', required=True)
args = parser.parse_args()
base = args.base_url.rstrip('/')
admin, _ = cookie_opener()
visitor, _ = cookie_opener()
submit_login(admin, base, os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
checks = {}
assets = set()
for path, opener in [('/', visitor), ('/kategori/f01-active', visitor),
                     ('/ara?query=F01', visitor), ('/yazi/f01-markdown-xss', visitor),
                     ('/Account/Login', visitor), ('/Admin/Dashboard', admin),
                     ('/AdminPost', admin), ('/AdminPost/Create', admin),
                     ('/AdminPost/Edit/2005', admin)]:
    status, _, html = request(opener, base + path)
    checks[path + '_compiled_assets'] = (status == 200 and '/generated/tailwind-' in html
        and 'cdn.tailwindcss.com' not in html and 'editor/latest/' not in html)
    for url in re.findall(r'(?:src|href)="([^"]+)"', html):
        if url.startswith('/generated/'):
            assets.add(url)
            checks['cache_busting_' + url.split('?')[0]] = '?v=' in url
    if '/generated/prism/' in html:
        checks['toolbar_precedes_language'] = html.index('prism-toolbar.min.js') < html.index('prism-show-language.min.js')
for url in sorted(assets):
    status, _, body = request(visitor, urljoin(base, url.replace('&amp;', '&')))
    checks['http_asset_' + url.split('?')[0]] = status == 200 and len(body) > 100
print(json.dumps({'checks': checks, 'unique_rendered_assets': len(assets)}, indent=2))
assert all(checks.values()), [name for name, passed in checks.items() if not passed]
