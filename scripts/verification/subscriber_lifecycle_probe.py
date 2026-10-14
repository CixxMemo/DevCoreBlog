#!/usr/bin/env python3
"""Owned PostgreSQL migration/race acceptance; never loads application dotenv or real subscriber data."""
import argparse, getpass, json, os, subprocess, tempfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--report', type=Path, required=True)
parser.add_argument('--port', default='55520')
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
checks = {}
env = {key: value for key, value in os.environ.items()
       if key in ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL')}
env.update(PGPASSFILE='/dev/null', PGCONNECT_TIMEOUT='5')
previous = '20261012095050_ScopedWebhookReceipts'
current = '20261012170132_SubscriberLifecycle'

def run(command, extra=None, required=True, timeout=60):
    result = subprocess.run(command, cwd=root, env={**env, **(extra or {})},
                            capture_output=True, text=True, timeout=timeout)
    if required and result.returncode:
        # Provider/SQL exception bodies are not exported as diagnostics.
        raise RuntimeError('Fixture command failed: ' + command[0] + ' exit=' + str(result.returncode))
    return result

def check(name, condition):
    checks[name] = bool(condition)
    print('subscriber_' + name + '=' + str(bool(condition)).lower(), flush=True)

if run(['pg_isready', '-h', '127.0.0.1', '-p', args.port], required=False).returncode == 0:
    raise SystemExit('Owned PostgreSQL port is occupied.')

try:
    with tempfile.TemporaryDirectory(prefix='devcoreblog-subscriber-', dir='/tmp') as owned:
        fixture = Path(owned)
        (fixture / 'socket').mkdir()
        started = False
        def connection(database):
            return f'Host=127.0.0.1;Port={args.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        def sql(database, query, required=True):
            return run(['psql', '-X', '-h', '127.0.0.1', '-p', args.port, '-d', database,
                        '-At', '-v', 'ON_ERROR_STOP=1', '-c', query], required=required).stdout.strip()
        def migrate(database, target=None, required=True):
            return run(['dotnet', 'ef', 'database', 'update', *([target] if target else []), '--no-build',
                        '--project', 'DevCoreBlog.csproj', '--startup-project', 'DevCoreBlog.csproj',
                        '--context', 'ApplicationDbContext', '--no-color'],
                       {'DB_CONNECTION_STRING': connection(database)}, required=required)
        def snapshot(database):
            return '\n'.join(sql(database, f'SELECT to_jsonb(t)::text FROM "{table}" t ORDER BY 1')
                             for table in ('Posts', 'Categories', 'WebhookReceipts'))
        try:
            run(['initdb', '-D', str(fixture / 'pg'), '--auth=trust', '--encoding=UTF8', '--locale=en_US.UTF-8'])
            run(['pg_ctl', '-D', str(fixture / 'pg'), '-l', str(fixture / 'pg.log'), '-o',
                 f'-h 127.0.0.1 -p {args.port} -k {fixture}/socket', '-w', 'start'])
            started = True
            version = sql('postgres', 'SHOW server_version')
            for database in ('subscriber_empty', 'subscriber_prior'):
                run(['createdb', '-h', '127.0.0.1', '-p', args.port, database])
            migrate('subscriber_prior', previous)
            check('baseline_previous_schema_has_no_subscribers', sql('subscriber_prior', "SELECT to_regclass('\"Subscribers\"') IS NULL") == 't')
            sql('subscriber_prior', '''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion")
                VALUES (1,'Synthetic','synthetic','2020-01-01Z',true,1);
                INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion")
                VALUES (1,'Synthetic','synthetic','Summary','Excerpt','Türkçe preserved content',1,'2020-01-01Z',true,false,'2030-01-01Z','',7,1);
                INSERT INTO "WebhookReceipts" ("ContractVersion","Key","PayloadHash","PostId","CreatedPostId","Title","Slug","CreatedAt","IsPublished","PublishDate")
                VALUES (1,'fixture-receipt',repeat('a',64),1,1,'Synthetic','synthetic','2020-01-01Z',false,'2030-01-01Z');''')
            before = snapshot('subscriber_prior')
            history = sql('subscriber_prior', 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
            migrate('subscriber_empty', current)
            check('empty_schema_migration_succeeds', sql('subscriber_empty', 'SELECT count(*) FROM "Subscribers"') == '0')
            migrate('subscriber_empty', previous)
            check('empty_down_is_safe', sql('subscriber_empty', "SELECT to_regclass('\"Subscribers\"') IS NULL") == 't')
            migrate('subscriber_empty', current)
            migrate('subscriber_prior', current)
            check('prior_schema_upgrade_preserves_all_old_rows', before == snapshot('subscriber_prior'))
            check('migration_history_is_additive', sql('subscriber_prior', 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines() == history + [current])
            for database in ('subscriber_empty', 'subscriber_prior'):
                migrate(database, '20261014124542_SubscriberUnsubscriptionPeriod')
                result = run(['dotnet', str(root / 'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll'), '--subscribers'],
                             {'DB_CONNECTION_STRING': connection(database)}, required=False)
                try:
                    report = json.loads(result.stdout)
                    valid = result.returncode == 0 and report['passed'] and report['count'] == len(report['checks']) and all(report['checks'].values())
                except (ValueError, KeyError, TypeError):
                    valid = False
                    report = {'passed': False, 'exit_code': result.returncode, 'diagnostic': 'Tool did not return a complete safe report'}
                args.report.parent.mkdir(parents=True, exist_ok=True)
                (args.report.parent / (database + '.json')).write_text(json.dumps(report, indent=2) + '\n')
                check(database + '_domain_and_races', valid)
            runtime_history = sql('subscriber_prior', 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines()
            count = sql('subscriber_prior', 'SELECT count(*) FROM "Subscribers"')
            result = migrate('subscriber_prior', previous, required=False)
            check('nonempty_down_rejected_and_data_retained', result.returncode != 0
                  and sql('subscriber_prior', 'SELECT count(*) FROM "Subscribers"') == count
                  and sql('subscriber_prior', 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1').splitlines() == runtime_history)
            check('lifecycle_tests_do_not_touch_old_data', before == snapshot('subscriber_prior'))
            result = run(['dotnet', 'ef', 'migrations', 'has-pending-model-changes', '--no-build', '--project', 'DevCoreBlog.csproj',
                          '--startup-project', 'DevCoreBlog.csproj', '--context', 'ApplicationDbContext'], {'DB_CONNECTION_STRING': connection('subscriber_prior')})
            check('model_snapshot_matches', result.returncode == 0)
        finally:
            if started:
                run(['pg_ctl', '-D', str(fixture / 'pg'), '-m', 'fast', '-w', 'stop'], required=False)
        check('owned_cluster_stopped', run(['pg_isready', '-h', '127.0.0.1', '-p', args.port], required=False).returncode != 0)
except Exception as error:
    checks['fixture_completed'] = False
    print('Subscriber fixture failed: ' + (str(error) if isinstance(error,RuntimeError) else type(error).__name__), flush=True)
    version = locals().get('version', 'unverified')
finally:
    args.report.parent.mkdir(parents=True, exist_ok=True)
    report = {'passed': bool(checks) and all(checks.values()), 'count': len(checks), 'checks': checks,
              'postgres_version': version, 'scope': 'Owned real PostgreSQL, synthetic subscribers; no provider or production calls'}
    args.report.write_text(json.dumps(report, indent=2) + '\n')
raise SystemExit(0 if report['passed'] else 1)
