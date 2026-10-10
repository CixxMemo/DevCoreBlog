#!/usr/bin/env python3
"""Verify inventory correctness and read-only/privacy failures on an owned disposable cluster."""
import argparse
import getpass
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--report-dir', type=Path, required=True)
    parser.add_argument('--postgres-port', type=int, default=55473)
    args = parser.parse_args()
    assert 1024 < args.postgres_port < 65535
    assert not args.report_dir.exists(), 'Preserve previous evidence'
    args.report_dir.mkdir(parents=True)
    root = Path(__file__).resolve().parents[2]
    tool = root / 'tools/DevCoreBlog.TransitionInventoryTool/bin/Debug/net10.0/DevCoreBlog.TransitionInventoryTool.dll'
    assert tool.is_file(), 'Build the inventory tool first'
    env = {k: v for k, v in os.environ.items() if k in
        ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL')}
    checks = {}
    completed = False
    for name in ('initdb', 'pg_ctl', 'createdb', 'psql', 'pg_isready', 'dotnet'):
        assert shutil.which(name, path=env.get('PATH')), name

    def run(command, extra=None, check=True):
        return subprocess.run(command, env={**env, **(extra or {})}, capture_output=True,
            text=True, timeout=45, check=check)

    assert run(['pg_isready', '-h', '127.0.0.1', '-p', str(args.postgres_port)], check=False).returncode != 0
    with tempfile.TemporaryDirectory(prefix='devcoreblog-inventory-', dir='/tmp') as owned:
        owned = Path(owned)
        data, socket = owned / 'postgres', owned / 'socket'
        socket.mkdir()
        started = False
        user = getpass.getuser()
        sql_args = ['psql', '-X', '-h', '127.0.0.1', '-p', str(args.postgres_port), '-U', user,
            '-d', 'inventory_synthetic', '-At', '-v', 'ON_ERROR_STOP=1', '-c']
        config = f'Host=127.0.0.1;Port={args.postgres_port};Database=inventory_synthetic;Username={user};Password=F02_CREDENTIAL_CANARY'

        def sql(text):
            return run(sql_args + [text]).stdout.strip()

        def inventory(label, connection=config):
            proc = run(['dotnet', str(tool)], {'TRANSITION_INVENTORY_CONNECTION': connection}, check=False)
            assert not proc.stderr and 'F02_CREDENTIAL_CANARY' not in proc.stdout
            result = json.loads(proc.stdout)
            assert 'F02_BODY_CANARY' not in proc.stdout and 'F02_URL_CANARY' not in proc.stdout
            (args.report_dir / (label + '.json')).write_text(json.dumps(result, indent=2) + '\n')
            return proc.returncode, result

        try:
            run(['initdb', '-D', str(data), '--no-locale', '--encoding=UTF8', '--auth=trust'])
            run(['pg_ctl', '-D', str(data), '-l', str(owned / 'postgres.log'), '-o',
                f"-p {args.postgres_port} -h 127.0.0.1 -k {socket} -c log_statement=all -c log_line_prefix=%a:", '-w', 'start'])
            started = True
            run(['createdb', '-h', '127.0.0.1', '-p', str(args.postgres_port), '-U', user, 'inventory_synthetic'])
            sql('''CREATE TABLE "Categories" ("Id" int PRIMARY KEY,"Slug" text,"IsActive" boolean);
                CREATE TABLE "Posts" ("Id" int PRIMARY KEY,"Title" text,"Content" text,"Summary" text,
                "Excerpt" text,"Slug" text,"CategoryId" int,"IsActive" boolean,"IsPublished" boolean,
                "PublishDate" timestamptz,"ThumbnailUrl" text,"ThumbnailPublicId" text,
                "ThumbnailWidth" int,"ThumbnailHeight" int);
                CREATE UNIQUE INDEX ON "Posts" ("Slug");
                CREATE UNIQUE INDEX ON "Categories" ("Slug");
                CREATE TABLE "WebhookReceipts" ("CreatedAt" timestamptz,"PostId" int);
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text);''')
            code, empty = inventory('empty')
            checks['empty_database_is_verified_zero_not_connection_failure'] = code == 0 and empty['sections']['totals']['posts'] == 0
            checks['real_server_readonly_repeatable_snapshot'] = empty['sections']['environment']['transactionReadOnly'] == 'on' and empty['sections']['environment']['isolation'] == 'repeatable read'
            checks['snapshot_timestamp_is_explicit_utc'] = empty['sections']['environment']['snapshotUtc'].endswith('+00:00')
            checks['valid_unique_slug_indexes_are_discovered'] = empty['sections']['schema']['postSlugUniqueValidUnfiltered'] and empty['sections']['schema']['categorySlugUniqueValidUnfiltered']
            denied = run(sql_args + ['INSERT INTO "Categories" VALUES (99,\'blocked\',true)'],
                {'PGOPTIONS': '-c default_transaction_read_only=on'}, check=False)
            checks['postgres_rejects_write_under_same_readonly_policy'] = denied.returncode != 0 and 'read-only' in denied.stderr and sql('SELECT count(*) FROM "Categories"') == '0'
            sql('''INSERT INTO "Categories" VALUES (1,'active',true),(2,'inactive',false);
                INSERT INTO "Posts" ("Id","Title","Content","Summary","Excerpt","Slug","CategoryId","IsActive","IsPublished","PublishDate","ThumbnailUrl")
                VALUES (1,'F02_BODY_CANARY','![image](https://F02_URL_CANARY/image.png)','','','one',1,true,true,now()-interval '1 day','https://F02_URL_CANARY/cover.png'),
                (2,'draft','text','','','two',1,true,false,now(),''),
                (3,'scheduled','text','','','three',1,true,true,now()+interval '1 day',''),
                (4,'inactive','text','','','four',1,false,true,now(),''),
                (5,'category inactive','<img src="F02_URL_CANARY">','','','five',2,true,true,now(),'');
                INSERT INTO "WebhookReceipts" VALUES (now(),1),(now(),NULL);
                INSERT INTO "__EFMigrationsHistory" VALUES ('20261004183254_CoverMetadata');''')
            before = sql('SELECT md5(string_agg(row_to_json(p)::text,\'\' ORDER BY "Id")) FROM "Posts" p')
            code, seeded = inventory('seeded')
            posts = seeded['sections']['posts']
            checks['draft_scheduled_public_inactive_are_distinct'] = code == 0 and [posts[n] for n in ('draft', 'scheduled', 'publiclyVisible', 'inactiveByPostOrCategory')] == [1, 1, 1, 2]
            checks['media_candidates_count_without_returning_urls_or_bodies'] = posts['postsWithMarkdownImageCandidate'] == posts['postsWithHtmlImageCandidate'] == posts['postsWithCoverUrl'] == 1
            checks['receipts_retained_after_deletion_are_counted'] = seeded['sections']['webhookReceipts']['total'] == 2 and seeded['sections']['webhookReceipts']['retainedAfterPostDeletion'] == 1
            checks['inventory_does_not_change_existing_content'] = before == sql('SELECT md5(string_agg(row_to_json(p)::text,\'\' ORDER BY "Id")) FROM "Posts" p')
            checks['legacy_access_metadata_is_explicitly_unknown'] = posts['accessContract'] == 'legacy_schema_without_access_metadata' and posts['subscriberBodies'] is None
            sql('ALTER TABLE "Posts" ADD COLUMN "ContentKind" int NOT NULL DEFAULT 0, ADD COLUMN "AccessScope" int NOT NULL DEFAULT 0; UPDATE "Posts" SET "ContentKind"=4,"AccessScope"=1 WHERE "Id"=1')
            code, private = inventory('explicit-private-access')
            checks['new_access_schema_excludes_published_private_body'] = code == 0 and private['sections']['posts']['publiclyVisible'] == 0 and private['sections']['posts']['subscriberBodies'] == 1
            sql('UPDATE "Posts" SET "AccessScope"=0 WHERE "Id"=1')
            code, invalid_access = inventory('invalid-access')
            checks['corrupt_public_newsletter_is_counted_but_not_public'] = code == 0 and invalid_access['sections']['posts']['publiclyVisible'] == 0 and invalid_access['sections']['posts']['invalidAccessMetadata'] == 1
            sql('ALTER TABLE "Posts" DROP COLUMN "AccessScope"')
            code, partial = inventory('partial-access-schema')
            checks['partial_access_schema_is_unverified_without_fake_public_count'] = code == 1 and 'posts' not in partial['sections'] and 'totals' not in partial['sections']
            sql('ALTER TABLE "Posts" DROP COLUMN "ContentKind"')
            sql('DROP INDEX "Posts_Slug_idx"; UPDATE "Posts" SET "Slug"=\'one\' WHERE "Id"=2; ALTER TABLE "Posts" DROP COLUMN "ThumbnailPublicId"')
            code, legacy = inventory('duplicates-legacy')
            checks['actual_duplicate_slug_and_missing_index_are_visible'] = code == 0 and legacy['sections']['posts']['duplicateNonemptySlugGroups'] == 1 and not legacy['sections']['schema']['postSlugUniqueValidUnfiltered']
            checks['absent_metadata_is_unknown_not_fake_zero'] = legacy['sections']['posts']['postsWithCoverPublicId'] is None
            sql('ALTER TABLE "Posts" RENAME COLUMN "Content" TO "UnknownContent"')
            code, incompatible = inventory('incompatible-schema')
            checks['incompatible_schema_preserves_unknown_counts'] = code == 1 and incompatible['status'] == 'unverified' and 'totals' not in incompatible['sections']
            sql('ALTER TABLE "Posts" RENAME COLUMN "UnknownContent" TO "Content"; TRUNCATE "Posts"; INSERT INTO "Posts" ("Id","Content") SELECT n,\'bounded\' FROM generate_series(1,50001) n')
            code, too_many = inventory('row-limit')
            checks['oversized_inventory_stops_before_content_analysis'] = code == 1 and too_many['sections']['totals']['posts'] == 50001 and 'posts' not in too_many['sections']
            sql('TRUNCATE "Posts"; INSERT INTO "Posts" ("Id","Content") VALUES (1,\'bounded\'); ALTER TABLE "Posts" RENAME TO "InventorySlowSource"; CREATE VIEW "Posts" AS SELECT p.* FROM "InventorySlowSource" p CROSS JOIN LATERAL pg_sleep(20) delay')
            started_at = time.monotonic()
            code, slow = inventory('slow-query')
            checks['slow_query_is_bounded_and_never_reports_zero'] = code == 1 and time.monotonic() - started_at < 15 and 'totals' not in slow['sections']
            code, failed = inventory('connection-failed', 'Host=127.0.0.1;Port=1;Database=absent;Password=F02_CREDENTIAL_CANARY')
            checks['connection_failure_is_not_empty_database_or_secret_exception'] = code == 1 and failed['status'] == 'unverified' and failed['sections'] == {}
            code, invalid = inventory('invalid-config', 'F02_CREDENTIAL_CANARY')
            checks['invalid_configuration_is_safe_nonzero'] = code == 1 and invalid['sections'] == {}
            log = (owned / 'postgres.log').read_text()
            statements = [line for line in log.splitlines() if line.startswith('DevCoreBlog.ReadOnlyInventory:') and ('statement:' in line or 'execute ' in line)]
            checks['tool_executed_no_insert_update_delete_ddl'] = bool(statements) and not any(
                marker in line.upper() for line in statements for marker in ('INSERT INTO', 'UPDATE "', 'DELETE FROM', 'CREATE TABLE', 'ALTER TABLE', 'DROP TABLE'))
            assert all(checks.values()), [name for name, value in checks.items() if not value]
            completed = True
        finally:
            if started:
                run(['pg_ctl', '-D', str(data), '-m', 'fast', '-w', 'stop'], check=False)
            (args.report_dir / 'summary.json').write_text(json.dumps({'checks': checks,
                'passed': completed and bool(checks) and all(checks.values()), 'scope': 'Owned synthetic PostgreSQL only; no configured real DB or dotenv loaded.'}, indent=2) + '\n')
    print(json.dumps({'checks': checks, 'passed': all(checks.values())}, indent=2))


if __name__ == '__main__':
    main()
