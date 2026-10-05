#!/usr/bin/env python3
"""Compose final security/data acceptance on an explicitly owned disposable cluster."""
import argparse
import getpass
import json
import os
from pathlib import Path
import re
import subprocess

from http_probe_support import (cookie_opener, extract_antiforgery_token,
    has_authentication_cookie, request, submit_login, wait_until_ready)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--database-port', type=int, required=True)
    parser.add_argument('--port', type=int, default=15201)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    source = args.source.resolve()
    owned = source.parent
    assert source.name == 'source' and owned.name.startswith('devcoreblog-f17.')
    assert owned.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
    assert not args.report.exists(), 'Preserve previous evidence'
    user = getpass.getuser()
    pg = ['psql', '-X', '-h', '127.0.0.1', '-p', str(args.database_port), '-U', user,
          '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1']
    actual = subprocess.check_output(pg + ['-c', 'SHOW data_directory'], text=True).strip()
    assert Path(actual).resolve() == (owned / 'postgres').resolve()
    # Inherit runtime paths only; real application credentials/config never enter the fixture.
    env = {k: v for k, v in os.environ.items() if k in
           ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL')}
    password = 'f57-isolated-password'
    hashed = subprocess.run(['dotnet', str(source / 'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'), '--stdin'],
        input=password + '\n', capture_output=True, text=True, check=True, env=env).stdout.strip()
    keys = owned / 'f57-keys'
    keys.mkdir(mode=0o700, exist_ok=True)
    base = f'http://127.0.0.1:{args.port}'
    env.update(USER=user, SITE_TIME_ZONE='Europe/Istanbul', TZ='UTC',
        ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
        SITE_URL='https://blog.example.test', PORTFOLIO_CORS_ORIGIN='https://portfolio.example.test',
        DB_CONNECTION_STRING=f'Host=127.0.0.1;Port={args.database_port};Database=devcoreblog_f01_test;Username={user}',
        ADMIN_USERNAME='f57-admin', ADMIN_PASSWORD_HASH=hashed,
        ADMIN_SESSION_VERSION='f57-stable', DATA_PROTECTION_KEYS_PATH=str(keys),
        CLOUDINARY_CLOUD_NAME='f57-cloud', CLOUDINARY_API_KEY='f57-key', CLOUDINARY_API_SECRET='f57-media',
        WEBHOOK_API_SECRET='f17-webhook', Security__WebhookRateLimit__PermitLimit='20',
        DEVCORE_TEST_ADMIN_USERNAME='f57-admin', DEVCORE_TEST_ADMIN_PASSWORD=password)
    checks, stages = {}, {}
    process = None
    log = None

    def stop():
        nonlocal process, log
        if process:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            process = None
        if log:
            log.close()
            log = None

    def start(label, overrides=None, expect_rejected=False):
        nonlocal process, log
        stop()
        log = (owned / ('f57-' + label + '.log')).open('w')
        process = subprocess.Popen([str(source / 'bin/Debug/net10.0/DevCoreBlog')], cwd=source,
            env={**env, **(overrides or {})}, stdout=log, stderr=subprocess.STDOUT)
        if expect_rejected:
            try:
                code = process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                raise AssertionError('Invalid credentials did not reject startup')
            log.flush()
            body = (owned / ('f57-' + label + '.log')).read_text()
            checks[label + '_startup_fails_closed'] = code != 0 and 'OptionsValidationException' in body and 'ADMIN_' in body
            assert checks[label + '_startup_fails_closed']
            return
        wait_until_ready(cookie_opener()[0], base + '/Account/Login', 'Admin Sign In', 30)
        assert process.poll() is None

    def probe(label, filename, arguments):
        result = subprocess.run(['python3', str(source / 'scripts/verification' / filename),
            '--base-url', base, *arguments], env=env, capture_output=True, text=True, timeout=100)
        extracted = {line.split('=')[0]: line.endswith('=true') for line in result.stdout.splitlines()
            if re.fullmatch(r'[a-zA-Z0-9_]+=(true|false)', line)}
        if not extracted:
            try:
                payload = json.loads(result.stdout[result.stdout.index('{'):])
                extracted = payload.get('checks', {})
            except ValueError:
                pass
        stages[label] = {'exit_code': result.returncode, 'checks': extracted}
        checks[label] = result.returncode == 0 and bool(extracted) and all(extracted.values())
        assert checks[label], label + ' failed; private raw output withheld'

    try:
        for label, overrides in (
            ('missing_hash', {'ADMIN_PASSWORD_HASH': '', 'ADMIN_PASSWORD': password}),
            ('missing_username', {'ADMIN_USERNAME': ''}),
            ('malformed_hash', {'ADMIN_PASSWORD_HASH': 'not-a-valid-encoded-hash'})):
            start(label, overrides, expect_rejected=True)
        start('password')
        for name, candidate in (('empty', ''), ('wrong', 'wrong')):
            admin, cookies = cookie_opener()
            _, token, status, url, body = submit_login(admin, base, 'f57-admin', candidate)
            checks[name + '_password_rejected'] = bool(token) and status == 200 and '/Account/Login' in url and 'Invalid username or password.' in body and not has_authentication_cookie(cookies)
        start('csrf')
        admin, cookies = cookie_opener()
        _, token, status, url, _ = submit_login(admin, base, 'f57-admin', password)
        checks['hashed_login_succeeds'] = bool(token) and status == 200 and '/Admin/Dashboard' in url and has_authentication_cookie(cookies)
        for endpoint in ('/Account/Logout', '/AdminPost/TogglePublish/2001', '/AdminPost/Create',
                '/AdminPost/Edit/2001', '/AdminPost/Delete/2008', '/AdminPost/UploadEditorImage',
                '/AdminPost/UploadImage', '/AdminPost/Preview', '/AdminCategory/Create',
                '/AdminCategory/Edit/1001', '/AdminCategory/Delete/1002'):
            checks['csrf_missing_' + endpoint] = request(admin, base + endpoint, data={})[0] == 400
        checks['csrf_invalid_rejected'] = request(admin, base + '/AdminPost/TogglePublish/2001', data={}, headers={'X-CSRF-TOKEN': 'invalid-fixture-token'})[0] == 400
        checks['rejected_logout_keeps_session'] = '/Admin/Dashboard' in request(admin, base + '/Admin/Dashboard')[1]
        anon, jar = cookie_opener()
        checks['login_missing_csrf_rejected'] = request(anon, base + '/Account/Login', data={'username': 'f57-admin', 'password': password})[0] == 400 and not has_authentication_cookie(jar)
        checks['anonymous_admin_redirect'] = '/Account/Login' in request(anon, base + '/AdminPost')[1]
        page = request(admin, base + '/AdminPost/Create')[2]
        valid_token = extract_antiforgery_token(page)
        checks['csrf_valid_reaches_upload_validation'] = request(admin, base + '/AdminPost/UploadEditorImage', data={}, headers={'X-CSRF-TOKEN': valid_token})[0] == 400
        request(admin, base + '/Account/Logout', data={'__RequestVerificationToken': valid_token})
        checks['valid_logout_ends_session'] = '/Account/Login' in request(admin, base + '/Admin/Dashboard')[1] and not has_authentication_cookie(cookies)
        start('rate', {'Security__LoginRateLimit__PermitLimit': '4', 'Security__LoginRateLimit__WindowSeconds': '2'})
        probe('login_rate', 'f06_login_rate_limit_probe.py', ['--permit-limit', '4', '--window-seconds', '2'])
        start('renderer')
        probe('safe_markdown', 'f26_renderer_probe.py', [])
        start('counter')
        probe('atomic_counter', 'f21_view_count_probe.py', ['--pg-port', str(args.database_port), '--pg-user', user])
        start('webhook')
        probe('webhook_durable', 'f28_webhook_probe.py', ['--mode', 'acceptance', '--pg-port', str(args.database_port), '--pg-user', user])
        start('webhook_restart')
        probe('webhook_restart', 'f28_webhook_probe.py', ['--mode', 'restart', '--pg-port', str(args.database_port), '--pg-user', user])
        start('cache')
        probe('cache_scheduled_release', 'f18_cache_probe.py', ['--pg-port', str(args.database_port), '--pg-user', user])
        start('category')
        probe('category_delete_race', 'f15_category_delete_probe.py', ['--database-port', str(args.database_port), '--database-user', user, '--database-name', 'devcoreblog_f01_test'])
        assert all(checks.values()), 'A final acceptance check failed'
    finally:
        stop()
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({'checks': checks, 'stages': stages,
            'scope': 'Owned disposable PostgreSQL and MVC. Private logs not exported. Remote CI not executed.'}, indent=2) + '\n')
    print(json.dumps({'checks': checks, 'stage_count': len(stages)}, indent=2))


if __name__ == '__main__':
    main()
