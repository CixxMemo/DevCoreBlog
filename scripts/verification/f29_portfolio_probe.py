#!/usr/bin/env python3
"""Verify public feed visibility, stable URLs, bounded SQL and browser CORS policy."""
import argparse
import json
import re
import subprocess
import time
import urllib.request
import urllib.error
from pathlib import Path
from http_probe_support import admin_login_url, cookie_opener, request_with_headers, wait_until_ready


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--pg-port', required=True)
    parser.add_argument('--pg-user', required=True)
    parser.add_argument('--mode', choices=['baseline', 'acceptance'], required=True)
    parser.add_argument('--application-log')
    args = parser.parse_args()
    opener, _ = cookie_opener()
    wait_until_ready(opener, admin_login_url(args.base_url), 'Admin Sign In', 30)
    checks = {}
    def sql(query):
        return subprocess.run(['psql','-X','-h','127.0.0.1','-p',args.pg_port,'-U',args.pg_user,
            '-d','devcoreblog_f01_test','-At','-c',query],check=True,capture_output=True,text=True).stdout.strip()
    def get(headers=None):
        status, _, body, response_headers = request_with_headers(opener,
            args.base_url+'/api/public/posts/latest', headers=headers or {})
        try: parsed=json.loads(body)
        except json.JSONDecodeError: parsed=None
        return status,parsed,{key.lower():value for key,value in response_headers.items()}
    status, items, _ = get({'Host':'attacker.example.test','X-Forwarded-Host':'forwarded.example.test','X-Forwarded-Proto':'http'})
    checks['host_cannot_control_absolute_urls'] = status == 200 and isinstance(items,list) and bool(items) and all(item['url'].startswith('https://blog.example.test/post/') for item in items)
    if args.mode == 'acceptance':
        sql('''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","Excerpt","IsPublished","ThumbnailUrl","EditVersion") SELECT i, 'F29 Tie '||i, 'f29-tie-'||i, 'F29 summary', repeat('F29 body ',20000), 1001, CURRENT_TIMESTAMP, true, 0, '2026-01-01T00:00:00Z', 'F29 excerpt', true, '', 1 FROM generate_series(2901,2910) i; UPDATE "Posts" SET "PublishDate"='2020-01-01T00:00:00Z' WHERE "Id" NOT BETWEEN 2901 AND 2910 AND "PublishDate" <= CURRENT_TIMESTAMP;''')
        log=Path(args.application_log)
        before=len(log.read_text())
        status, items, headers = get({'Origin':'https://portfolio.example.test'})
        after=log.read_text()[before:]
        queries=re.findall(r'SELECT .*?(?=\n\s*\[|\n\s*info:|\Z)',after,re.S)
        feed_query=next((query for query in queries if 'FROM "Posts"' in query), '')
        selected=feed_query.split('FROM')[0]
        checks['exact_preserved_json_fields'] = status==200 and isinstance(items,list) and all(set(item)=={'id','title','slug','summary','excerpt','coverImageUrl','publishDate','url','categoryName'} for item in items)
        checks['three_posts_stable_date_and_id_order'] = status==200 and [item['id'] for item in items]==[2901,2902,2903]
        checks['sql_reads_only_feed_columns'] = bool(selected) and '"Content"' not in selected and '"CreatedDate"' not in selected and '"EditVersion"' not in selected and '"ViewCount"' not in selected and '"Name"' in selected
        checks['one_sql_with_visibility_order_and_limit'] = after.count('Executed DbCommand')==1 and 'LIMIT' in feed_query and re.search(r'ORDER BY .*"PublishDate" DESC, .*"Id"',feed_query) is not None and all(part in feed_query for part in ['"IsActive"','"IsPublished"','"PublishDate" <='])
        checks['allowed_origin_get_has_cors_header'] = headers.get('access-control-allow-origin')=='https://portfolio.example.test'
        denied_status, denied_items, denied_headers=get({'Origin':'https://unlisted.example.test'})
        checks['cors_is_browser_policy_not_auth'] = denied_status==200 and denied_items==items and 'access-control-allow-origin' not in denied_headers
        req=urllib.request.Request(args.base_url+'/api/public/posts/latest',method='OPTIONS',headers={'Origin':'https://portfolio.example.test','Access-Control-Request-Method':'GET','Access-Control-Request-Headers':'Content-Type'})
        with opener.open(req, timeout=5) as response:
            preflight={key.lower():value for key,value in response.headers.items()}
            checks['get_preflight_supported'] = response.status==204 and preflight.get('access-control-allow-origin')=='https://portfolio.example.test' and 'GET' in preflight.get('access-control-allow-methods','')
        req=urllib.request.Request(args.base_url+'/api/public/posts/latest',method='OPTIONS',headers={'Origin':'https://portfolio.example.test','Access-Control-Request-Method':'POST'})
        try:
            response = opener.open(req, timeout=5)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            checks['post_preflight_rejected'] = response.code in (204,404,405) and 'POST' not in response.headers.get('Access-Control-Allow-Methods','')
        checks['no_store_cache_policy'] = 'no-store' in headers.get('cache-control','')
        checks['payload_has_no_large_body_or_navigation'] = len(json.dumps(items))<5000 and all('content' not in item and 'category' not in item for item in items)
        sql('UPDATE "Posts" SET "IsPublished"=false WHERE "Id"=2901;')
        _, updated, _=get()
        checks['mutation_visible_without_stale_cache'] = [item['id'] for item in updated]==[2902,2903,2904]
        sql('UPDATE "Posts" SET "PublishDate"=CURRENT_TIMESTAMP+interval \'3 seconds\' WHERE "Id"=2902;')
        _, before_publish, _=get()
        time.sleep(3.1)
        _, after_publish, _=get()
        checks['scheduled_boundary_has_no_stale_cache'] = 2902 not in [item['id'] for item in before_publish] and after_publish[0]['id']==2902
        sql('UPDATE "Categories" SET "IsActive"=false WHERE "Id"=1001;')
        _, hidden, _=get()
        checks['inactive_category_hidden_immediately'] = hidden==[]
        sql('UPDATE "Categories" SET "IsActive"=true WHERE "Id"=1001; DELETE FROM "Posts" WHERE "Id" BETWEEN 2901 AND 2910;')
        statuses=[]
        for _ in range(35):
            status, _, rate_headers=get()
            statuses.append(status)
            if status==429: break
        checks['rate_limit_429_with_retry_after'] = 429 in statuses and int(rate_headers.get('retry-after','0')) > 0
    for name,passed in checks.items(): print('f29_'+name+'='+str(passed).lower(),flush=True)
    return 0 if all(checks.values()) else 1

if __name__=='__main__': raise SystemExit(main())
