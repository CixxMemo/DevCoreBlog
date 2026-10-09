#!/usr/bin/env python3
"""Verify configured admin routes and privacy only on the owned disposable MVC fixture."""
import argparse
import getpass
import http.client
import json
import os
from pathlib import Path
import subprocess
from urllib.parse import urlsplit

from http_probe_support import (admin_login_path, admin_login_url, cookie_opener,
    extract_antiforgery_token, has_authentication_cookie, is_private_admin_challenge,
    request_with_headers, submit_login)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--database-port', type=int, required=True)
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    source = args.source.resolve()
    assert source.name == 'source' and source.parent.name.startswith('devcoreblog-f17.')
    assert source.parent.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
    assert (source / '.env').read_text() == ''
    base = args.base_url.rstrip('/')
    assert urlsplit(base).hostname == '127.0.0.1'
    assert admin_login_path() != '/Account/Login', 'Exercise a genuinely configured route'
    assert not args.report.exists(), 'Preserve prior evidence'
    checks = {}
    visitor, jar = cookie_opener()
    username, password = os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD']
    try:
        response = request_with_headers(visitor, admin_login_url(base))
        checks['configured_get_has_form_csrf_and_no_store'] = (response[0] == 200
            and 'action="' + admin_login_path() + '"' in response[2]
            and extract_antiforgery_token(response[2]) is not None
            and 'no-store' in response[3].get('Cache-Control', ''))
        connection = http.client.HTTPConnection('127.0.0.1', urlsplit(base).port, timeout=5)
        connection.request('HEAD', admin_login_path())
        head = connection.getresponse()
        checks['configured_head_is_empty_private_200'] = (head.status == 200
            and 'no-store' in head.getheader('Cache-Control', '') and head.read() == b'')
        connection.close()
        for path in ('/Account/Login', '/Account/Login/', '/account/login'):
            result = request_with_headers(visitor, base + path)
            checks['legacy_' + path + '_does_not_expose_login'] = (result[0] == 404
                and 'Admin Sign In' not in result[2] and 'Location' not in result[3])
        for path in ('/Admin', '/Admin/Dashboard', '/AdminPost', '/AdminCategory'):
            checks[path + '_denial_is_private_404'] = is_private_admin_challenge(
                request_with_headers(visitor, base + path))
        for path in ('/', '/sign-in', '/about', '/contact', '/robots.txt', '/sitemap.xml'):
            result = request_with_headers(visitor, base + path)
            checks[path + '_does_not_disclose_configured_admin'] = (result[0] == 200
                and admin_login_path() not in result[2]
                and all(admin_login_path() not in value for value in result[3].values()))
        for suffix, data in (('missing', {}), ('invalid', {'__RequestVerificationToken': 'invalid-fixture-token'})):
            result = request_with_headers(visitor, admin_login_url(base), data={
                'username': username, 'password': password, **data})
            checks['login_csrf_' + suffix + '_rejected'] = result[0] == 400 and not has_authentication_cookie(jar)
        admin, cookies = cookie_opener()
        _, token, status, url, body = submit_login(admin, base, username, 'wrong-fixture-password')
        checks['wrong_password_has_generic_error_without_session'] = (token is not None
            and status == 200 and url == admin_login_url(base)
            and 'Invalid username or password.' in body and not has_authentication_cookie(cookies))
        _, token, status, url, _ = submit_login(admin, base, username, password)
        checks['configured_post_creates_admin_session'] = (token is not None and status == 200
            and url == base + '/Admin/Dashboard' and has_authentication_cookie(cookies))
        result = request_with_headers(admin, base + '/AdminPost/Create')
        token = extract_antiforgery_token(result[2])
        checks['admin_access_is_private'] = result[0] == 200 and 'no-store' in result[3].get('Cache-Control', '')
        checks['logout_missing_csrf_preserves_session'] = (request_with_headers(admin,
            base + '/Account/Logout', data={})[0] == 400
            and request_with_headers(admin, base + '/Admin/Dashboard')[0] == 200)
        request_with_headers(admin, base + '/Account/Logout', data={'__RequestVerificationToken': token or ''})
        checks['logout_revokes_access_without_disclosing_login'] = (not has_authentication_cookie(cookies)
            and is_private_admin_challenge(request_with_headers(admin, base + '/AdminPost')))

        # No dotenv or real process settings enter negative startup probes.
        env = {key: value for key, value in os.environ.items() if key in
            ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL')}
        env.update(ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development',
            SITE_URL='https://blog.example.test',
            DB_CONNECTION_STRING=f'Host=127.0.0.1;Port={args.database_port};Database=devcoreblog_f01_test;Username={getpass.getuser()}',
            DATA_PROTECTION_KEYS_PATH=str(source.parent / 'keys'))
        for label, path in (('missing', None), ('empty', ''), ('whitespace', ' '),
                ('external', 'https://invalid-path-canary.example'), ('reserved', '/sign-in'),
                ('query', '/invalid-path-canary?x=1'), ('deep', '/a/b/c'), ('oversize', '/' + 'a' * 160)):
            settings = dict(env)
            if path is not None:
                settings['ADMIN_LOGIN_PATH'] = path
            process = subprocess.run([str(source / 'bin/Debug/net10.0/DevCoreBlog')], cwd=source,
                env=settings, capture_output=True, text=True, timeout=10)
            raw = process.stdout + process.stderr
            checks[label + '_login_config_fails_closed'] = process.returncode != 0 and 'ADMIN_LOGIN_PATH' in raw
            checks[label + '_error_does_not_print_input'] = 'invalid-path-canary' not in raw
        assert all(checks.values()), [name for name, passed in checks.items() if not passed]
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({'checks': checks, 'count': len(checks),
            'scope': 'Owned MVC and disposable PostgreSQL; only synthetic settings. No real admin path, credentials or cookies exported.'}, indent=2) + '\n')
    print(json.dumps({'checks': checks, 'count': len(checks)}, indent=2))


if __name__ == '__main__':
    main()
