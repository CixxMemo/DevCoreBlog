#!/usr/bin/env python3
"""AY02 acceptance on real PostgreSQL and MVC: the unmodified build keeps shelved email paths closed with every mail setting on.
A dormant copy first creates genuine unfinished work and proves that the listener would see a provider call.
Every provider call stops at a local listener, so nothing leaves this machine and no real address is used."""
import argparse
import getpass
import html
import http.cookiejar
import json
import os
from html.parser import HTMLParser
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

from dormant_email_paths import CLOSED, OPENED, switch_file

parser = argparse.ArgumentParser()
parser.add_argument('--closed', type=Path, required=True, help='Unmodified source copy; built here.')
parser.add_argument('--open', type=Path, required=True, help='Already built dormant copy that also holds the test tools.')
parser.add_argument('--report', type=Path, required=True)
parser.add_argument('--port', default='55541')
args = parser.parse_args()

closed_root = args.closed.resolve()
open_root = args.open.resolve()
APP_PORT = 15241
PROXY_PORT = 15542
BASE = f'http://127.0.0.1:{APP_PORT}'
NEWSLETTER_ID = 4702
LEGACY_ID = 4703
ACTIVE_ADDRESS = 'suspension-active@example.test'
PASSWORD = 'suspension-synthetic-password'
inherited = ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL',
             'NPM_CONFIG_USERCONFIG', 'NPM_CONFIG_GLOBALCONFIG', 'NPM_CONFIG_REGISTRY')
env = {key: value for key, value in os.environ.items() if key in inherited}
env.update(PGPASSFILE='/dev/null', PGCONNECT_TIMEOUT='5')
checks = {}


def run(command, extra=None, cwd=None, stdin=None, required=True, timeout=180):
    result = subprocess.run(command, cwd=cwd or closed_root, env={**env, **(extra or {})}, input=stdin,
                            capture_output=True, text=True, timeout=timeout)
    if required and result.returncode:
        # The fixture is synthetic: a short tail names the failing step without any real credential to leak.
        step = ' '.join(Path(part).name for part in command[:3])
        tail = (result.stdout + result.stderr).strip().splitlines()[-4:]
        raise RuntimeError('Owned suspension command failed: ' + step + ' exit=' + str(result.returncode) + ' | ' + ' / '.join(line[:200] for line in tail))
    return result


def check(name, passed):
    checks[name] = bool(passed)
    print(name + '=' + str(bool(passed)).lower(), flush=True)
    if not passed:
        raise RuntimeError('Acceptance failed: ' + name)


class Fields(HTMLParser):
    def __init__(self, body):
        super().__init__()
        self.values = {}
        self.feed(body)

    def handle_starttag(self, tag, attrs):
        data = dict(attrs)
        if tag == 'input' and 'name' in data:
            self.values[data['name']] = data.get('value', '') or ''


class ProviderListener:
    """Stands in for the network: records each provider connection and never answers it."""

    def __init__(self):
        self.calls = 0
        self.held = []
        self.server = socket.socket()
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind(('127.0.0.1', PROXY_PORT))
        self.server.listen(16)
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                connection, _ = self.server.accept()
            except OSError:
                return
            self.calls += 1
            self.held.append(connection)

    def reset(self):
        for connection in self.held:
            connection.close()
        self.held.clear()
        self.calls = 0

    def close(self):
        self.reset()
        self.server.close()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *unused):
        return None


class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(self.jar))

    def request(self, path, data=None, method=None, raw=None, headers=None):
        payload = raw if raw is not None else urllib.parse.urlencode(data).encode() if data is not None else None
        request = urllib.request.Request(BASE + path, data=payload, method=method, headers=headers or {})
        try:
            with self.opener.open(request, timeout=20) as response:
                return response.status, {k.lower(): v for k, v in response.headers.items()}, response.read().decode()
        except urllib.error.HTTPError as error:
            return error.code, {k.lower(): v for k, v in error.headers.items()}, error.read().decode()

    def token(self, path):
        status, _, body = self.request(path)
        value = Fields(body).values.get('__RequestVerificationToken')
        if status != 200 or not value:
            raise RuntimeError('Antiforgery token unavailable: ' + path + ' status=' + str(status))
        return value

    def login(self):
        values = Fields(self.request('/fixture-admin/login')[2]).values
        form = {**values, 'username': 'fixture-admin', 'password': PASSWORD}
        if self.request('/fixture-admin/login', form)[0] != 302:
            raise RuntimeError('Owned admin sign-in failed')


for port in (int(args.port), APP_PORT, PROXY_PORT):
    with socket.socket() as probe:
        if probe.connect_ex(('127.0.0.1', port)) == 0:
            raise SystemExit('Owned suspension port occupied')

listener = None
try:
    if switch_file(closed_root).read_text().count(CLOSED) != 1 or switch_file(open_root).read_text().count(OPENED) != 1:
        raise RuntimeError('Closed and dormant copies are not what this stage expects')
    # The tool requires this prefix, a reader_ database and the owned data directory.
    with tempfile.TemporaryDirectory(prefix='devcoreblog-reader-suspension-', dir='/tmp') as owned:
        fixture = Path(owned)
        (fixture / 'socket').mkdir()
        keys = fixture / 'keys'
        keys.mkdir(mode=0o700)
        env['READER_TEST_ROOT'] = owned
        database = 'reader_suspension'
        connection = f'Host=127.0.0.1;Port={args.port};Database={database};Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        process = None
        pg_started = False
        web_log = (fixture / 'web.log').open('w')

        def sql(query):
            return run(['psql', '-X', '-h', '127.0.0.1', '-p', args.port, '-d', database, '-At', '-v', 'ON_ERROR_STOP=1', '-c', query]).stdout.strip()

        def snapshot():
            tables = sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY 1").splitlines()
            ignored = ('Posts', '__EFMigrationsHistory')
            return {table: sql(f"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'|' ORDER BY to_jsonb(t)::text),'')) FROM \"{table}\" t")
                    for table in tables if table not in ignored}

        def start(root):
            global process
            process = subprocess.Popen(['dotnet', str(root / 'bin/Debug/net10.0/DevCoreBlog.dll'), '--contentRoot', str(root), '--urls', BASE],
                                       cwd=root, env=settings, stdout=web_log, stderr=subprocess.STDOUT)
            probe_client = Client()
            for _ in range(200):
                if process.poll() is not None:
                    raise RuntimeError('Owned suspension app exited')
                try:
                    if probe_client.request('/fixture-admin/login')[0] == 200:
                        return
                except OSError:
                    pass
                time.sleep(.1)
            raise RuntimeError('Owned suspension readiness failed')

        def kill():
            global process
            if process is not None and process.poll() is None:
                process.send_signal(signal.SIGKILL)
                process.wait(timeout=10)
            process = None

        def wait_until(condition, seconds):
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                if condition():
                    return True
                time.sleep(.25)
            return condition()

        try:
            shutil.copyfile(open_root / 'NuGet.Config', closed_root / 'NuGet.Config')
            run(['dotnet', 'restore', 'DevCoreBlog.csproj', '--configfile', 'NuGet.Config', '-p:NuGetAudit=false'], timeout=240)
            run(['dotnet', 'build', '--no-restore', '--disable-build-servers', '-m:1', '-nodeReuse:false', '-p:NuGetAudit=false', 'DevCoreBlog.csproj'], timeout=300)

            run(['initdb', '-D', owned + '/pg', '--auth=trust', '--encoding=UTF8', '--locale=en_US.UTF-8'])
            run(['pg_ctl', '-D', owned + '/pg', '-l', owned + '/pg.log', '-o', f'-h 127.0.0.1 -p {args.port} -k {owned}/socket', '-w', 'start'])
            pg_started = True
            run(['createdb', '-h', '127.0.0.1', '-p', args.port, database])
            run(['dotnet', 'ef', 'database', 'update', '--no-build', '--project', 'DevCoreBlog.csproj', '--context', 'ApplicationDbContext'], {'DB_CONNECTION_STRING': connection})

            document = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Sentetik içerik.'}]}]}})
            sql("""INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (4701,'Sentetik alan','suspension-fixture','2026-01-01Z',true,1)""")
            sql(f"""INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes")
                VALUES ({NEWSLETTER_ID},'Sentetik bülten','suspension-newsletter','Kısa özet.','','Legacy',4701,'2026-01-01Z',true,true,'2026-01-01Z','',0,1,4,1,1,'{document}','Sentetik içerik.',2,1)""")
            sql(f"""INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope")
                VALUES ({LEGACY_ID},'Sentetik yazı','suspension-post','Kısa özet.','','Sentetik gövde.',4701,'2026-01-01Z',true,true,'2026-01-01Z','',0,1,2,0)""")
            sql(f"""INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version","PeriodId")
                VALUES (gen_random_uuid(),'{ACTIVE_ADDRESS}','{ACTIVE_ADDRESS}',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid())""")

            tool = ['dotnet', str(open_root / 'tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll')]
            # The verification seed dispatches the oldest queued job itself, so it must run before anything else queues.
            verification_file = fixture / 'verification.json'
            run(tool + ['--verification-seed'], {'DB_CONNECTION_STRING': connection, 'OUTBOX_TEST_KEYS': str(keys), 'VERIFICATION_TEST_PROOF_FILE': str(verification_file)}, cwd=open_root)
            verification = json.loads(verification_file.read_text())
            reader_file = fixture / 'reader.json'
            run(tool + ['--reader-seed'], {'DB_CONNECTION_STRING': connection, 'READER_TEST_PROOF_FILE': str(reader_file)}, cwd=open_root)
            reader = json.loads(reader_file.read_text())
            hashed = run(['dotnet', str(open_root / 'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'), '--stdin'], cwd=open_root, stdin=PASSWORD + '\n').stdout.strip()

            # Every mail setting is deliberately on: only the code boundary can keep the closed build shut.
            settings = {**env, 'DB_CONNECTION_STRING': connection, 'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': BASE,
                        'SITE_URL': 'https://aether.example.test', 'SITE_TIME_ZONE': 'Europe/Istanbul', 'ADMIN_USERNAME': 'fixture-admin',
                        'ADMIN_PASSWORD_HASH': hashed, 'ADMIN_LOGIN_PATH': '/fixture-admin/login', 'DATA_PROTECTION_KEYS_PATH': str(keys),
                        'SUBSCRIPTIONS_ENABLED': 'true', 'READER_LOGIN_ENABLED': 'true', 'TRANSACTIONAL_EMAIL_WORKER_ENABLED': 'true',
                        'NEWSLETTER_WORKER_ENABLED': 'true', 'NEWSLETTER_DELIVERY_READY': 'true', 'TEST_EMAIL_ENABLED': 'true',
                        'TEST_EMAIL_RECIPIENT': 'suspension-test@example.test', 'RESEND_WEBHOOK_ENABLED': 'true',
                        'RESEND_WEBHOOK_SECRET': 'whsec_c3ludGhldGljLXN1c3BlbnNpb24tc2VjcmV0',
                        'RESEND_WEBHOOK_ALLOWED_SENDERS': 'sender@example.test', 'RESEND_API_KEY': 're_synthetic',
                        'RESEND_FROM_EMAIL': 'sender@example.test', 'RESEND_FROM_NAME': 'Synthetic',
                        'HTTPS_PROXY': f'http://127.0.0.1:{PROXY_PORT}'}
            listener = ProviderListener()

            # Positive control: the dormant build really creates work and really reaches the listener.
            start(open_root)
            admin = Client()
            admin.login()
            compose = admin.request(f'/AdminCampaign/Compose?postId={NEWSLETTER_ID}')
            prepare_form = {**Fields(compose[2]).values, 'Decision': 'prepare'}
            prepared = admin.request('/AdminCampaign/Prepare', prepare_form)
            details_path = prepared[1].get('location', '')
            check('control_campaign_prepared_on_dormant_build', compose[0] == 200 and prepared[0] == 302 and 'Details' in details_path)
            details = admin.request(details_path)
            start_form = {key: value for key, value in Fields(details[2]).values.items() if key in ('Id', 'Version', '__RequestVerificationToken')}
            started = admin.request('/AdminCampaign/Start', {**start_form, 'Decision': 'start', 'Confirm': 'true'})
            check('control_campaign_started_on_dormant_build', started[0] in (200, 302))
            visitor = Client()
            registration_token = visitor.token('/abone-ol')
            for address in ('suspension-new-1@example.test', 'suspension-new-2@example.test'):
                visitor.request('/abone-ol', {'Email': address, '__RequestVerificationToken': registration_token})
            count = lambda query: int(sql('SELECT count(*) FROM ' + query))
            check('control_listener_sees_provider_calls', wait_until(lambda: listener.calls >= 1, 30))
            check('control_campaign_attempt_reached_provider_boundary', wait_until(lambda: count('"CampaignDeliveryAttempts"') >= 1, 30))
            check('control_transactional_attempt_reached_provider_boundary', wait_until(lambda: count('"TransactionalEmailAttempts"') >= 1, 30))
            check('control_unfinished_transactional_work_exists', count('"TransactionalEmailJobs" WHERE "State"=0') >= 1)
            check('control_unfinished_campaign_work_exists', count('"CampaignDeliveryJobs" WHERE "State" IN (0,1,2)') >= 1)
            # A crash leaves in-flight and pending rows exactly as a real restart would find them.
            kill()
            listener.reset()
            before = snapshot()

            start(closed_root)
            time.sleep(8)  # Both workers poll far more often than this when they are running.
            check('restart_makes_no_provider_call', listener.calls == 0)
            check('restart_leaves_unfinished_work_untouched', snapshot() == before)

            anonymous = Client()
            token = anonymous.token('/fixture-admin/login')
            closed_paths = ('/abone-ol', '/sign-in', '/abonelik/dogrula', '/abonelik/giris')
            for path in closed_paths:
                status, headers, body = anonymous.request(path)
                text = html.unescape(body)
                check('closed_get' + path, status == 410 and 'Bülten ve abonelik kapatıldı' in text and '<form' not in body and 'type="email"' not in body)
                check('closed_private_response' + path, 'no-store' in headers.get('cache-control', '') and 'noindex' in body and bool(headers.get('content-security-policy')))
                head = anonymous.request(path, method='HEAD')
                check('closed_head' + path, head[0] == 410 and head[2] == '')
            attempts = (('/abone-ol', {'Email': 'suspension-closed@example.test'}), ('/sign-in', {'Email': reader['email']}),
                        ('/abonelik/dogrula', {'Token': verification['token']}), ('/abonelik/giris', {'Token': reader['token']}))
            for path, form in attempts:
                check('closed_post_needs_antiforgery' + path, anonymous.request(path, form)[0] == 400)
                check('closed_post_stores_nothing' + path, anonymous.request(path, {**form, '__RequestVerificationToken': token})[0] == 410 and snapshot() == before)
            check('real_proofs_activate_or_sign_in_nobody', not any(cookie.name == 'AetherNotes.Reader' for cookie in anonymous.jar))

            admin = Client()
            admin.login()
            admin_token = admin.token('/AdminPost/Create')
            for path in ('/AdminTestEmail', f'/AdminCampaign/Compose?postId={NEWSLETTER_ID}'):
                check('admin_closed_get' + path.split('?')[0], admin.request(path)[0] == 410)
            for path, form in (('/AdminTestEmail/Prepare', {'Input.Subject': 'Sentetik', 'Input.RequestId': 'ignored'}),
                               ('/AdminTestEmail/Send', {'Input.Subject': 'Sentetik', 'Input.RequestId': 'ignored'}),
                               ('/AdminCampaign/Prepare', {key: value for key, value in prepare_form.items() if key != '__RequestVerificationToken'}),
                               ('/AdminCampaign/Start', {'Id': start_form['Id'], 'Version': start_form['Version'], 'Decision': 'start', 'Confirm': 'true'}),
                               ('/AdminCampaign/Retry', {'Id': start_form['Id'], 'Version': start_form['Version'], 'Decision': 'retry', 'Confirm': 'true'})):
                check('admin_closed_post_stores_nothing' + path, admin.request(path, {**form, '__RequestVerificationToken': admin_token})[0] == 410 and snapshot() == before)
            check('history_stays_readable', all(admin.request(path)[0] == 200 for path in ('/AdminCampaign', details_path, '/AdminSubscriber')))
            webhook = anonymous.request('/api/webhooks/resend', raw=b'{}', headers={'Content-Type': 'application/json', 'svix-id': 'msg_x', 'svix-timestamp': str(int(time.time())), 'svix-signature': 'v1,x'})
            check('provider_webhook_stays_closed', webhook[0] == 503 and snapshot() == before)
            check('no_provider_call_after_all_requests', listener.calls == 0)

            traces = ('href="/abone-ol"', 'href="/sign-in"', 'registration-form', 'newsletter-demo', 'reader-sign-in-link', 'Bültene abone')
            for path in ('/', '/?kind=news', '/bulten', '/about', '/contact', '/post/suspension-post', '/category/suspension-fixture'):
                status, _, body = anonymous.request(path)
                check('no_subscription_trace' + path, status == 200 and not any(trace in html.unescape(body) for trace in traces))
            home = html.unescape(anonymous.request('/')[2])
            check('home_has_no_newsletter_promise', 'href="/bulten"' not in home and 'Bülten' not in home and 'e-posta' not in home.lower() and 'abone' not in home.lower())
            dashboard = html.unescape(admin.request('/Admin/Dashboard')[2])
            check('admin_menu_has_no_send_or_subscriber_entry', all(label not in dashboard for label in ('Gönderimler', 'Aboneler', 'Yayın ayarları')))
            check('newsletter_list_offers_no_send', 'AdminCampaign' not in admin.request('/AdminNewsletter')[2])

            exit_page = anonymous.request('/abonelik/cik')
            exit_token = Fields(exit_page[2]).values.get('__RequestVerificationToken')
            check('exit_page_still_works', exit_page[0] == 200 and bool(exit_token) and 'kapatıldı' not in html.unescape(exit_page[2]))
            status_of = lambda: sql(f"SELECT \"Status\" FROM \"Subscribers\" WHERE \"AddressKey\"='{reader['email']}'")
            check('exit_subject_active_before', status_of() == '1')
            exited = anonymous.request('/abonelik/cik', {'Token': reader['exitToken'], '__RequestVerificationToken': exit_token})
            check('real_exit_proof_still_unsubscribes', exited[0] == 200 and status_of() == '2')
            # The HTTPS answer is covered behind real Nginx by campaign-delivery; plain HTTP stays refused here.
            check('one_click_boundary_still_refuses_plain_http', anonymous.request('/abonelik/tek-tik')[0] == 400)

            edit = admin.request(f'/AdminPost/Edit/{LEGACY_ID}')
            values = Fields(edit[2]).values
            saved = admin.request(f'/AdminPost/Edit/{LEGACY_ID}', {'Id': str(LEGACY_ID), 'Title': 'Sentetik yazı kaydedildi', 'Content': 'Sentetik gövde kaydedildi.', 'CategoryId': '4701',
                                  'Summary': 'Kısa özet.', 'Excerpt': '', 'IsPublished': 'true', 'IsActive': 'true', 'SaveAction': 'Save', 'PublishDate': values.get('PublishDate', ''),
                                  'EditVersion': values.get('EditVersion', ''), '__RequestVerificationToken': values.get('__RequestVerificationToken', '')})
            check('admin_can_save_a_post', edit[0] == 200 and saved[0] == 302 and sql(f'SELECT "Title" FROM "Posts" WHERE "Id"={LEGACY_ID}') == 'Sentetik yazı kaydedildi')
            check('saved_post_stays_public', 'Sentetik yazı kaydedildi' in html.unescape(anonymous.request('/post/suspension-post')[2]))
            created = admin.request('/AdminWriting/Create')
            check('document_editor_still_opens', created[0] == 200)
            check('no_provider_call_during_closed_run', listener.calls == 0)

            kill()
            web_log.flush()
            log = (fixture / 'web.log').read_text()
            check('logs_hold_no_address_key_or_proof', all(secret not in log for secret in (ACTIVE_ADDRESS, reader['email'], 'suspension-new-1@example.test', 're_synthetic', reader['token'], reader['exitToken'], verification['token'])))
        finally:
            kill()
            web_log.close()
            if pg_started:
                run(['pg_ctl', '-D', owned + '/pg', '-m', 'fast', '-w', 'stop'], required=False)
    check('owned_cluster_and_keys_removed', not fixture.exists())
except Exception as error:
    checks['fixture_completed'] = False
    print(str(error) if isinstance(error, RuntimeError) else type(error).__name__, flush=True)
finally:
    if listener is not None:
        listener.close()
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps({'passed': bool(checks) and all(checks.values()), 'count': len(checks), 'checks': checks,
                                       'scope': 'Real PostgreSQL and MVC. The unmodified build ran with every mail setting on; a local listener stood in for the provider. No real recipient.'}, indent=2) + '\n')
raise SystemExit(0 if checks and all(checks.values()) else 1)
