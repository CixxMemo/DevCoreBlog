#!/usr/bin/env python3
"""Check durable webhook retries using synthetic HTTP requests and PostgreSQL."""
import argparse
import concurrent.futures
import json
import subprocess
import time
from http_probe_support import cookie_opener, request, wait_until_ready


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--pg-port', required=True)
    parser.add_argument('--pg-user', required=True)
    parser.add_argument('--mode', choices=['baseline', 'acceptance', 'restart'], required=True)
    args = parser.parse_args()
    checks = {}
    def sql(query):
        return subprocess.run(['psql', '-X', '-h', '127.0.0.1', '-p', args.pg_port,
            '-U', args.pg_user, '-d', 'devcoreblog_f01_test', '-At', '-c', query],
            check=True, capture_output=True, text=True).stdout.strip()
    def send(key, payload=None, secret='f17-webhook', raw=None):
        opener, _ = cookie_opener()
        headers = {'Content-Type': 'application/json', 'X-DevCore-Secret': secret}
        if key is not None:
            headers['Idempotency-Key'] = key
        deadline = time.monotonic() + 65
        while True:
            status, _, body = request(opener, args.base_url + '/api/webhooks/posts',
                raw_data=raw if raw is not None else json.dumps(payload).encode(), headers=headers)
            if status != 429 or time.monotonic() >= deadline:
                break
            time.sleep(0.5)
        try:
            body = json.loads(body)
        except json.JSONDecodeError:
            body = {}
        return status, body
    valid = {'title':'F28 Serial Retry', 'content':'F28 synthetic content', 'categoryId':1001}
    opener, _ = cookie_opener()
    wait_until_ready(opener, args.base_url + '/Account/Login', 'Admin Sign In', 30)
    if args.mode == 'restart':
        before = sql('SELECT count(*) FROM "Posts";')
        status, body = send('f28-serial', valid)
        expected_id = int(sql('SELECT "CreatedPostId" FROM "WebhookReceipts" WHERE "Key"=\'f28-serial\';'))
        checks['restart_returns_original_without_write'] = status == 200 and body.get('postId') == expected_id and sql('SELECT count(*) FROM "Posts";') == before
    else:
        before = int(sql('SELECT count(*) FROM "Posts";'))
        first = send('f28-serial', valid)
        second = send('f28-serial', valid)
        checks['serial_retry_is_same_result'] = first[0] == second[0] == 200 and first[1] == second[1]
        checks['serial_retry_creates_one_post'] = int(sql('SELECT count(*) FROM "Posts";')) == before + 1
        if args.mode == 'acceptance':
            checks['one_receipt_for_serial_retry'] = sql('SELECT count(*) FROM "WebhookReceipts" WHERE "Key"=\'f28-serial\';') == '1'
            conflict = send('f28-serial', {**valid, 'content':'changed'})
            checks['different_payload_is_409'] = conflict[0] == 409
            equivalent = send('f28-serial', {**valid, 'id':999999, 'slug':'ignored'})
            checks['unknown_fields_do_not_change_identity'] = equivalent == first
            sql('CREATE FUNCTION f28_delay_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Key" LIKE \'f28-concurrent%\' THEN PERFORM pg_sleep(0.3); END IF; RETURN NEW; END $$; CREATE TRIGGER f28_delay BEFORE INSERT ON "WebhookReceipts" FOR EACH ROW EXECUTE FUNCTION f28_delay_receipt();')
            concurrent_payload = {**valid, 'title':'F28 Concurrent Retry'}
            with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
                results = list(pool.map(lambda _: send('f28-concurrent', concurrent_payload), range(4)))
            checks['concurrent_retry_returns_one_result'] = all(result[0] == 200 and result[1] == results[0][1] for result in results)
            checks['concurrent_retry_creates_one_post'] = sql('SELECT count(*) FROM "Posts" WHERE "Title"=\'F28 Concurrent Retry\';') == '1'
            checks['concurrent_retry_creates_one_receipt'] = sql('SELECT count(*) FROM "WebhookReceipts" WHERE "Key"=\'f28-concurrent\';') == '1'
            competing = [{**valid, 'title':'F28 Race A'}, {**valid, 'title':'F28 Race B'}]
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                raced = list(pool.map(lambda payload: send('f28-concurrent-conflict', payload), competing))
            checks['concurrent_different_payload_is_one_success_one_conflict'] = sorted(result[0] for result in raced) == [200, 409] and sql('SELECT count(*) FROM "Posts" WHERE "Title" IN (\'F28 Race A\',\'F28 Race B\');') == '1'
            sql('DROP TRIGGER f28_delay ON "WebhookReceipts"; DROP FUNCTION f28_delay_receipt();')
            serial_id = first[1].get('postId')
            sql(f'UPDATE "Posts" SET "Title"=\'F28 Later Edit\', "IsPublished"=true WHERE "Id"={serial_id};')
            checks['later_edits_do_not_change_replayed_response'] = send('f28-serial', valid) == first
            concurrent_id = results[0][1].get('postId')
            sql(f'DELETE FROM "Posts" WHERE "Id"={concurrent_id};')
            checks['deleted_post_retains_receipt_and_cannot_recreate'] = send('f28-concurrent', concurrent_payload) == results[0] and sql('SELECT "PostId" IS NULL FROM "WebhookReceipts" WHERE "Key"=\'f28-concurrent\';') == 't' and sql(f'SELECT count(*) FROM "Posts" WHERE "Id"={concurrent_id};') == '0'
            bad = send('f28-invalid-then-valid', {**valid, 'categoryId':1002})
            good = send('f28-invalid-then-valid', {**valid, 'title':'F28 Valid Retry'})
            checks['validation_error_does_not_consume_key'] = bad[0] == 400 and good[0] == 200
            auth_before = sql('SELECT count(*) FROM "WebhookReceipts";')
            unauthorized = send('f28-unauthorized', valid, secret='invalid', raw=b'{')
            missing_secret = send('f28-no-secret', valid, secret='')
            checks['unauthorized_neither_parses_nor_writes_receipt'] = unauthorized[0] == missing_secret[0] == 401 and sql('SELECT count(*) FROM "WebhookReceipts";') == auth_before
            checks['invalid_key_is_400'] = send('invalid key', valid)[0] == 400 and send('x'*129, valid)[0] == 400
            # Database failure after the post insert must roll back both writes.
            sql('CREATE FUNCTION f28_fail_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Key"=\'f28-db-failure\' THEN RAISE EXCEPTION \'synthetic receipt failure\'; END IF; RETURN NEW; END $$; CREATE TRIGGER f28_failure BEFORE INSERT ON "WebhookReceipts" FOR EACH ROW EXECUTE FUNCTION f28_fail_receipt();')
            before_failure = sql('SELECT count(*) FROM "Posts";')
            failure = send('f28-db-failure', {**valid, 'title':'F28 Database Failure'})
            checks['database_error_rolls_back_post_and_receipt'] = failure[0] == 500 and sql('SELECT count(*) FROM "Posts";') == before_failure and sql('SELECT count(*) FROM "WebhookReceipts" WHERE "Key"=\'f28-db-failure\';') == '0'
            sql('DROP TRIGGER f28_failure ON "WebhookReceipts"; DROP FUNCTION f28_fail_receipt();')
            checks['database_error_key_can_retry'] = send('f28-db-failure', {**valid, 'title':'F28 Database Failure'})[0] == 200
    for name, passed in checks.items():
        print('f28_' + name + '=' + str(passed).lower(), flush=True)
    return 0 if all(checks.values()) else 1

if __name__ == '__main__':
    raise SystemExit(main())
