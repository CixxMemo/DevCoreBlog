#!/usr/bin/env python3
"""Verify private bodies, metadata, counters and warmed HTTP caches on the owned fixture."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import getpass
import json
import os
from pathlib import Path
import subprocess
import urllib.error
import urllib.request

from http_probe_support import (cookie_opener, extract_antiforgery_token, extract_hidden_value,
    request, request_with_headers, submit_login)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--database-port', type=int, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    source = args.source.resolve()
    assert source.name == 'source' and source.parent.name.startswith('devcoreblog-f17.')
    assert source.parent.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
    assert args.base_url.startswith('http://127.0.0.1:') and 1024 < args.database_port < 65535
    base = args.base_url.rstrip('/')
    checks = {}

    def sql(statement):
        return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', str(args.database_port),
            '-U', getpass.getuser(), '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1',
            '-c', statement], text=True, timeout=10).strip()

    visitor, _ = cookie_opener()
    admin, _ = cookie_opener()
    _, token, login_status, _, _ = submit_login(admin, base,
        os.environ['DEVCORE_TEST_ADMIN_USERNAME'], os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
    checks['authorized_fixture_login'] = login_status == 200 and token is not None
    slugs = ['f04-private-newsletter', 'f04-private-article', 'f04-private-scheduled',
        'f04-private-draft', 'f04-private-inactive']
    forbidden = [f'F04_PRIVATE_{part}_{id}' for id in range(4001, 4006)
        for part in ('TITLE', 'SUMMARY', 'BODY', 'EXCERPT')]
    forbidden += slugs + [f'f04-private-cover-{id}' for id in range(4001, 4006)]
    surfaces = ['/', '/?page=1&pageSize=27', '/kategori/f01-active', '/ara?query=F04',
        '/ara?query=F04_PRIVATE_BODY&category=f01-active', '/post/f01-visible',
        '/api/public/posts/latest', '/sitemap.xml', '/rss.xml']
    try:
        for index, path in enumerate(surfaces):
            response = request_with_headers(visitor, base + path)
            checks[f'public_surface_{index}_no_private_title_summary_body_url_or_meta'] = (
                response[0] == 200 and all(marker not in response[2] for marker in forbidden))
        checks['positive_public_control_in_home_search_portfolio_and_sitemap'] = all(
            'F04_PUBLIC_CONTROL' in request(visitor, base + path)[2]
            for path in ('/', '/ara?query=F04', '/api/public/posts/latest')) and (
                'f04-public-control' in request(visitor, base + '/sitemap.xml')[2])
        before = sql('SELECT string_agg("ViewCount"::text,\',\' ORDER BY "Id") FROM "Posts" WHERE "Id" BETWEEN 4001 AND 4005')
        for slug in slugs:
            for label, opener in (('anonymous', visitor), ('admin', admin)):
                response = request_with_headers(opener, base + '/post/' + slug)
                checks[f'{label}_{slug}_get_private_404_no_store_no_metadata'] = (
                    response[0] == 404 and 'no-store' in response[3].get('Cache-Control', '')
                    and all(marker not in response[2] for marker in forbidden))
                head = urllib.request.Request(base + '/post/' + slug, method='HEAD')
                try:
                    with opener.open(head, timeout=5) as result:
                        status, body = result.status, result.read()
                except urllib.error.HTTPError as error:
                    status, body = error.code, error.read()
                checks[f'{label}_{slug}_head_private_404_empty'] = status == 404 and body == b''
        checks['private_get_head_admin_requests_do_not_increment_counter'] = before == sql(
            'SELECT string_agg("ViewCount"::text,\',\' ORDER BY "Id") FROM "Posts" WHERE "Id" BETWEEN 4001 AND 4005')
        edit = request(admin, base + '/AdminPost/Edit/4001')
        checks['authorized_admin_editor_can_read_private_body'] = edit[0] == 200 and 'F04_PRIVATE_BODY_4001' in edit[2]
        form = {'Id': '4001', 'EditVersion': extract_hidden_value(edit[2], 'EditVersion') or '',
            'Title': 'F04_PRIVATE_TITLE_4001', 'Content': 'F04_PRIVATE_BODY_4001',
            'Summary': 'F04_PRIVATE_SUMMARY_4001', 'Excerpt': 'F04_PRIVATE_EXCERPT_4001',
            'CategoryId': '1001', 'IsActive': 'true', 'SaveAction': 'Save',
            'PublishDate': '2026-10-01T12:00:00', 'AccessScope': '0', 'ContentKind': '1',
            '__RequestVerificationToken': extract_antiforgery_token(edit[2]) or ''}
        saved = request(admin, base + '/AdminPost/Edit/4001', data=form)
        checks['legacy_admin_save_ignores_posted_classification_and_preserves_private_access'] = (
            saved[0] == 200 and '/AdminPost' in saved[1] and sql(
                'SELECT "ContentKind"::text || \',\' || "AccessScope"::text FROM "Posts" WHERE "Id"=4001') == '4,1'
            and request(visitor, base + '/post/f04-private-newsletter')[0] == 404)

        warm = [request(visitor, base + path) for path in ('/', '/kategori/f01-active')]
        sql('UPDATE "Posts" SET "Title"=\'F04_CACHE_DB_ONLY_CANARY\' WHERE "Id"=4010')
        checks['cache_was_warmed_and_reused_before_access_change'] = all(
            'F04_PUBLIC_CONTROL' in body and 'F04_CACHE_DB_ONLY_CANARY' not in body
            for _, _, body in [request(visitor, base + path) for path in ('/', '/kategori/f01-active')]) and all(
                'F04_PUBLIC_CONTROL' in result[2] for result in warm)
        denied = request(visitor, base + '/fixture-access/hide-control', data={})
        no_csrf = request(admin, base + '/fixture-access/hide-control', data={})
        checks['fixture_mutation_retains_auth_and_antiforgery'] = denied[0] in (400, 404) and no_csrf[0] == 400
        control_form = request(admin, base + '/AdminPost/Edit/4010')
        hidden = request(admin, base + '/fixture-access/hide-control', data={
            '__RequestVerificationToken': extract_antiforgery_token(control_form[2]) or ''})
        checks['actual_service_access_change_committed'] = hidden[0] == 200 and sql(
            'SELECT "AccessScope" FROM "Posts" WHERE "Id"=4010') == '1'
        def fresh_read(path):
            opener, _ = cookie_opener()
            return request(opener, base + path)
        with ThreadPoolExecutor(max_workers=6) as pool:
            responses = list(pool.map(fresh_read, ['/', '/kategori/f01-active'] * 6))
        checks['access_change_evicts_warmed_lists_and_parallel_reads'] = all(
            result[0] == 200 and 'F04_PUBLIC_CONTROL' not in result[2] and 'F04_CACHE_DB_ONLY_CANARY' not in result[2]
            for result in responses)
        checks['formerly_public_detail_and_crawler_documents_now_hide_body'] = (
            request(visitor, base + '/post/f04-public-control')[0] == 404 and all(
                'f04-public-control' not in request(visitor, base + path)[2]
                for path in ('/sitemap.xml', '/rss.xml', '/api/public/posts/latest')))
        create = request(admin, base + '/AdminPost/Create')
        created = request(admin, base + '/AdminPost/Create', data={
            'Title': 'F04_LEGACY_CREATE', 'Content': 'F04 legacy create body',
            'CategoryId': '1001', 'IsActive': 'true', 'SaveAction': 'SaveDraft',
            'PublishDate': '2026-10-01T12:00:00', 'ContentKind': '4', 'AccessScope': '1',
            '__RequestVerificationToken': extract_antiforgery_token(create[2]) or ''})
        checks['legacy_admin_create_retains_server_owned_default_metadata'] = created[0] == 200 and sql(
            'SELECT "ContentKind"::text || \',\' || "AccessScope"::text FROM "Posts" WHERE "Title"=\'F04_LEGACY_CREATE\'') == '0,0'
        webhook = request(visitor, base + '/api/webhooks/posts', raw_data=json.dumps({
            'title': 'F04_LEGACY_WEBHOOK', 'content': 'F04 legacy webhook body',
            'categoryId': 1001, 'isPublished': True, 'contentKind': 4, 'accessScope': 1}).encode(),
            headers={'Content-Type': 'application/json', 'X-DevCore-Secret': 'f17-webhook',
                'Idempotency-Key': 'f04-legacy-create'})
        checks['legacy_webhook_retains_default_metadata_and_publication_policy'] = webhook[0] == 200 and sql(
            'SELECT "ContentKind"::text || \',\' || "AccessScope"::text || \',\' || "IsPublished"::text FROM "Posts" WHERE "Title"=\'F04_LEGACY_WEBHOOK\'') == '0,0,false'
        assert all(checks.values()), [name for name, passed in checks.items() if not passed]
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({'checks': checks, 'passed': bool(checks) and all(checks.values()),
            'scope': 'Owned synthetic PostgreSQL and disposable HTTP host only; no real dotenv/provider.'}, indent=2) + '\n')
    print('f04_http_private_access_and_cache_matrix=true')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
