#!/usr/bin/env python3
"""Compare full MVC responses on the owned F48 fixture, excluding login from timing."""
import argparse, json, os, re, statistics, time
from pathlib import Path
from http_probe_support import cookie_opener, request, submit_login

parser = argparse.ArgumentParser()
parser.add_argument('--fixture-root', required=True)
parser.add_argument('--base-url', default='http://127.0.0.1:15181', choices=['http://127.0.0.1:15181','http://127.0.0.1:15182'])
parser.add_argument('--log-name', default='application.log', choices=['application.log','application-after.log'])
args = parser.parse_args()
root = Path(args.fixture_root).resolve()
assert root.parent == Path('/tmp').resolve() and root.name.startswith('devcoreblog-f17.')
assert (root/'postgres/postmaster.pid').read_text().splitlines()[3] == '55449'
base = args.base_url
admin, _ = cookie_opener()
anon, _ = cookie_opener()
assert submit_login(admin, base, os.environ['DEVCORE_TEST_ADMIN_USERNAME'],
    os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2] == 200
results = {}
log = root/args.log_name
for name, path in [('home','/?pageSize=9'), ('detail','/post/f48-post-1'),
    ('search','/ara?query=needle'), ('admin','/AdminPost')]:
    times = []
    for iteration in range(18):
        offset = log.stat().st_size
        start = time.perf_counter()
        status, _, body = request(admin if name == 'admin' else anon, base+path)
        elapsed = (time.perf_counter()-start)*1000
        assert status == 200, (name, status)
        if iteration >= 3: times.append(elapsed)
    with log.open() as handle:
        handle.seek(offset)
        commands = handle.read()
    results[name] = {'medianMs': statistics.median(times), 'samples': times,
        'queryCount': commands.count('Executed DbCommand'), 'responseBytes': len(body.encode()),
        'cardRows': len(re.findall(r'class="post-card ', body)),
        'adminRows': len(re.findall(r'id="post-row-', body)), 'sql': commands}
print(json.dumps(results, indent=2))
