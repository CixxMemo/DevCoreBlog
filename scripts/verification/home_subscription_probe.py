#!/usr/bin/env python3
"""F46 real isolated PostgreSQL/MVC home, current reader identity and admission boundary.
Only the scheduler and transport are controlled in the owned source copy; no external email.
"""
import argparse
import getpass
import html
import http.cookiejar
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from test_email_fixture_host import build_fixture_host
from test_email_http_fixture import Fields
from html.parser import HTMLParser
from rss_removal_http_probe import verify as verify_rss_removal

class SettingsForm(HTMLParser):
 def __init__(self,body):super().__init__();self.values={};self.active=False;self.textarea=None;self.feed(body)
 def handle_starttag(self,tag,attrs):
  d=dict(attrs)
  if tag=='form':self.active=d.get('action','').endswith('/AdminPublication/Save')
  if self.active and tag=='input' and 'name' in d and 'disabled' not in d and (d.get('type')!='checkbox' or 'checked' in d):self.values[d['name']]=d.get('value','')
  if self.active and tag=='textarea':self.textarea=d.get('name');self.values[self.textarea]=''
 def handle_data(self,data):
  if self.active and self.textarea:self.values[self.textarea]+=data
 def handle_endtag(self,tag):
  if tag=='textarea':self.textarea=None
  if tag=='form':self.active=False


p = argparse.ArgumentParser()
p.add_argument('--report', type=Path, required=True)
p.add_argument('--browser-module', type=Path)
p.add_argument('--browser-executable', type=Path)
p.add_argument('--browser-script', type=Path, help='Optional trusted local Chrome acceptance script; the default keeps F46 coverage.')
a = p.parse_args()
root = Path(__file__).resolve().parents[2]
env = {k: v for k, v in os.environ.items() if k in ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'LANG', 'LC_ALL')}
env.update(PGPASSFILE='/dev/null', PGCONNECT_TIMEOUT='5')
checks = {}
a.report.parent.mkdir(parents=True, exist_ok=True)
base = 'http://127.0.0.1:15239'
pg_port = '55539'

def run(command, extra=None, input=None, required=True, timeout=150):
    r = subprocess.run(command, cwd=root, env={**env, **(extra or {})}, input=input, capture_output=True, text=True, timeout=timeout)
    if required and r.returncode:
        raise RuntimeError('Owned home command failed: ' + command[0] + ' exit=' + str(r.returncode))
    return r

def check(name, value):
    checks[name] = bool(value)
    print(name + '=' + str(bool(value)).lower(), flush=True)
    if not value:
        raise RuntimeError('Acceptance failed: ' + name)

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None

def client():
    return urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

for port in (int(pg_port), 15239):
    with socket.socket() as s:
        if s.connect_ex(('127.0.0.1', port)) == 0:
            raise SystemExit('Owned home port occupied')
try:
    with tempfile.TemporaryDirectory(prefix='devcoreblog-home-', dir='/tmp') as owned:
        f = Path(owned)
        (f / 'socket').mkdir()
        (f / 'keys').mkdir(mode=0o700)
        proc = None
        pg_started = False
        stream = (f / 'web.log').open('w')
        connection = f'Host=127.0.0.1;Port={pg_port};Database=home_test;Username={getpass.getuser()};Timeout=5;Command Timeout=10;Include Error Detail=false'
        def sql(q, required=True):
            return run(['psql', '-X', '-h', '127.0.0.1', '-p', pg_port, '-d', 'home_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c', q], required=required).stdout.strip()
        def snapshot():
            return '\n'.join(sql('SELECT to_jsonb(t)::text FROM "' + table + '" t ORDER BY 1') for table in ('PublicationSettings', 'Subscribers', 'ReaderSessions', 'TransactionalEmailJobs', 'Posts', 'NewsletterCampaigns'))
        def stop():
            if proc is not None and proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait(timeout=10)
        try:
            run(['initdb', '-D', owned + '/pg', '--auth=trust', '--encoding=UTF8', '--locale=en_US.UTF-8'])
            run(['pg_ctl', '-D', owned + '/pg', '-l', owned + '/pg.log', '-o', f'-h 127.0.0.1 -p {pg_port} -k {owned}/socket', '-w', 'start'])
            pg_started = True
            run(['createdb', '-h', '127.0.0.1', '-p', pg_port, 'home_test'])
            run(['dotnet', 'ef', 'database', 'update', '--no-build', '--project', 'DevCoreBlog.csproj', '--context', 'ApplicationDbContext'], {'DB_CONNECTION_STRING': connection})
            check('real_pg16_and_current_schema', sql('SHOW server_version').startswith('16.') and sql('SELECT count(*) FROM "PublicationSettings"') == '0')
            # Actual public cards and a private canary exercise the existing projection boundaries.
            sql("""INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES (4601,'Deney alanı','home-fixture','2026-01-01Z',true,1)""")
            document = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': 'Deney sonucunu birlikte değerlendiriyoruz.'}]}]}})
            for kind in range(1, 5):
                title = ('PRIVATE_HOME_TITLE' if kind == 4 else ['AI gelişmesi', 'Bir deneyin sonucu', 'Teknoloji üzerine düşünceler'][kind - 1])
                sql('INSERT INTO "Posts" ("Id","Title","Slug","Summary","Excerpt","Content","CategoryId","CreatedDate","IsActive","IsPublished","PublishDate","ThumbnailUrl","ViewCount","EditVersion","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes") VALUES (' + str(4600 + kind) + ",'" + title + "','home-" + str(kind) + "','Kısa bir deney özeti.','','Legacy',4601,'2026-01-01Z',true,true,'2026-01-01Z','',0,1," + str(kind) + ',' + str(int(kind == 4)) + ",1,'" + document + "','Deney sonucunu birlikte değerlendiriyoruz.',4,1)")
            session = str(uuid.uuid4())
            def patch(source):
                # Admission availability remains real. No background scheduler races the HTTP/DB assertions.
                program = source / 'Program.cs'
                text = program.read_text()
                needle = 'builder.Services.AddHostedService<DevCoreBlog.Infrastructure.TransactionalEmailWorker>();'
                assert text.count(needle) == 1
                program.write_text(text.replace(needle, 'builder.Services.AddSingleton<DevCoreBlog.Fixture.HomeSchedulerSuppressed>();'))
                (source / 'F46Scheduler.cs').write_text('namespace DevCoreBlog.Fixture; internal sealed class HomeSchedulerSuppressed { }')
                (source / 'Controllers/F46ReaderController.cs').write_text('''using System.Security.Claims;using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Authentication;using DevCoreBlog.Security;
public sealed class F46ReaderController:Controller{[HttpGet("/fixture-home/reader")] public async Task<IActionResult> Index(Guid? session){var id=session?.ToString()??Environment.GetEnvironmentVariable("F46_READER_SESSION")??throw new InvalidOperationException();var p=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ReaderCookieAuthenticationEvents.SessionClaim,id.Replace("-",""))},ReaderCookieAuthenticationEvents.Scheme));await HttpContext.SignInAsync(ReaderCookieAuthenticationEvents.Scheme,p,new AuthenticationProperties{IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1),IsPersistent=true,AllowRefresh=false});return NoContent();}}''')
                # Capture only property names in the actually executed public projection, never parameters/values.
                (source / 'F46Projection.cs').write_text('''using System.Data.Common;using Microsoft.EntityFrameworkCore.Diagnostics;
namespace DevCoreBlog.Fixture;
internal sealed class HomeProjectionInterceptor:DbCommandInterceptor{
 public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default){if(command.CommandText.Contains("FROM \\\"PublicationSettings\\\"")){var valid=command.CommandText.TrimStart().StartsWith("SELECT p.\\\"Name\\\", p.\\\"Cadence\\\"")&&!command.CommandText.Contains("Sender");File.AppendAllText((Environment.GetEnvironmentVariable("F46_PROJECTION")??throw new InvalidOperationException()),valid?"narrow\\n":"other\\n");}return base.ReaderExecutingAsync(command,data,result,cancellationToken);}}
''')
                text = program.read_text()
                marker = 'var app = builder.Build();'
                program.write_text(text.replace(marker, 'builder.Services.AddScoped<DevCoreBlog.Fixture.HomeProjectionInterceptor>();\nbuilder.Services.AddDbContext<DevCoreBlog.Data.ApplicationDbContext>((services, options) => options.AddInterceptors(services.GetRequiredService<DevCoreBlog.Fixture.HomeProjectionInterceptor>()));\n' + marker))
            source, packet, control, clock, transport_gate = build_fixture_host(root, f, env, patch)
            hashed = run(['dotnet', str(root / 'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'), '--stdin'], input='f46-synthetic-password\n').stdout.strip()
            settings = {**env, 'DB_CONNECTION_STRING': connection, 'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': base, 'SITE_URL': 'https://aether.example.test', 'SITE_TIME_ZONE': 'Europe/Istanbul', 'ADMIN_USERNAME': 'fixture-admin', 'ADMIN_PASSWORD_HASH': hashed, 'ADMIN_LOGIN_PATH': '/fixture-admin/login', 'DATA_PROTECTION_KEYS_PATH': str(f / 'keys'), 'NEWSLETTER_WORKER_ENABLED': 'false', 'NEWSLETTER_DELIVERY_READY': 'false', 'TRANSACTIONAL_EMAIL_WORKER_ENABLED': 'true', 'READER_LOGIN_ENABLED': 'false', 'SUBSCRIPTIONS_ENABLED': 'true', 'TEST_EMAIL_ENABLED': 'false', 'RESEND_WEBHOOK_ENABLED': 'false', 'RESEND_API_KEY': 're_synthetic', 'RESEND_FROM_EMAIL': 'sender@example.test', 'RESEND_FROM_NAME': 'Synthetic', 'F46_READER_SESSION': session, 'F46_PROJECTION': str(f / 'projection.txt'), 'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command': 'Trace', 'Logging__LogLevel__Microsoft.AspNetCore.Mvc.ModelBinding': 'Trace'}
            anon, admin, reader = client(), client(), client()
            def request(path='/', data=None, method=None, opener=None, headers=None):
                req = urllib.request.Request(base + path, data=urllib.parse.urlencode(data).encode() if data is not None else None, method=method, headers=headers or {})
                try:
                    with (opener or anon).open(req, timeout=20) as r:
                        return r.status, {k.lower(): v for k, v in r.headers.items()}, r.read().decode()
                except urllib.error.HTTPError as e:
                    return e.code, {k.lower(): v for k, v in e.headers.items()}, e.read().decode()
            def start(extra=None):
                global proc
                stop()
                proc = subprocess.Popen(['dotnet', str(source / 'bin/Debug/net10.0/DevCoreBlog.dll'), '--contentRoot', str(source), '--urls', base], cwd=source, env={**settings, **(extra or {})}, stdout=stream, stderr=subprocess.STDOUT)
                for _ in range(150):
                    if proc.poll() is not None:
                        raise RuntimeError('Owned home app exited')
                    try:
                        if request('/fixture-admin/login')[0] == 200:
                            return
                    except OSError:
                        pass
                    time.sleep(.1)
                raise RuntimeError('Owned home readiness failed')
            def submit(address, token=None):
                anti = token or Fields(request()[2]).values['__RequestVerificationToken']
                return request('/abone-ol', {'Email': address, '__RequestVerificationToken': anti})
            start()
            before = snapshot()
            home = request()
            check('approved_hero_defaults_and_real_form_before_first_settings', home[0] == 200 and 'Yapay zekâyı deneyerek öğren' in html.unescape(home[2]) and 'id="publication-name">Aether Notes' in home[2] and 'id="registration-form"' in home[2] and sql('SELECT count(*) FROM "PublicationSettings"') == '0')
            check('real_public_cards_without_private_body_title_or_slug', all(title in html.unescape(home[2]) for title in ('AI gelişmesi', 'Bir deneyin sonucu', 'Teknoloji üzerine düşünceler')) and 'PRIVATE_HOME_TITLE' not in home[2] and 'home-4' not in home[2])
            check('actual_home_select_only_name_and_cadence', (f / 'projection.txt').read_text().splitlines() == ['narrow'])
            variants = [request(), request(method='HEAD'), request('/Home/Index'), request('/home/index/'), request('/?page=0'), request('/?page=2')]
            check('home_alias_head_invalid_missing_no_shared_cache', all('no-store' in r[1].get('cache-control', '') and 'age' not in r[1] for r in variants) and variants[1][2] == '' and variants[-2][0] == 400 and variants[-1][0] == 404)
            check('get_head_do_not_mutate_or_send', snapshot() == before and not packet.exists())
            check('three_public_discovery_kinds_and_teaser_link', all('kind=' + k in home[2] for k in ('news', 'experience', 'philosophy')) and 'href="/bulten"' in home[2] and all(request('/?kind=' + k)[0] == 200 for k in ('news', 'experience', 'philosophy')))
            check('home_post_does_not_bypass_registration_boundary', request('/', {'Email': 'ignored@example.test'})[0] in (400, 405) and snapshot() == before)
            check('registration_requires_antiforgery', request('/abone-ol', {'Email': 'csrf@example.test'})[0] == 400 and snapshot() == before)
            invalid = submit('bad address')
            check('invalid_address_real_400_without_false_success', invalid[0] == 400 and 'aria-invalid="true"' in invalid[2] and 'role="status"' not in invalid[2] and snapshot() == before)
            accepted = submit('home-new@example.test')
            check('home_form_creates_pending_with_encrypted_verification_outbox', accepted[0] == 200 and 'Adresiniz başvuruya uygunsa' in html.unescape(accepted[2]) and sql('SELECT "Status" FROM "Subscribers" WHERE "AddressKey"=\'home-new@example.test\'') == '0' and sql('SELECT count(*) FROM "TransactionalEmailJobs"') == '1' and not packet.exists())
            check('repeat_request_generic_and_idempotent', submit('home-new@example.test')[0] == 200 and sql('SELECT count(*) FROM "TransactionalEmailJobs"') == '1' and 'reader-archive-cta' not in request()[2])
            values = Fields(request('/fixture-admin/login', opener=admin)[2]).values
            check('real_admin_login', request('/fixture-admin/login', {**values, 'username': 'fixture-admin', 'password': 'f46-synthetic-password'}, opener=admin)[0] == 302)
            check('admin_cookie_never_means_reader', 'registration-form' in request(opener=admin)[2] and 'reader-archive-cta' not in request(opener=admin)[2])
            values = SettingsForm(request('/AdminPublication', opener=admin)[2]).values
            saved = request('/AdminPublication/Save', {**values, 'Name': 'Aether & Yeni', 'Cadence': 'Deney hazır olduğunda paylaşılır.'}, opener=admin)
            fresh = request()
            check('admin_save_updates_public_copy_immediately_encoded_without_sender', saved[0] == 302 and 'Aether &amp; Yeni' in fresh[2] and 'Deney hazır olduğunda paylaşılır.' in html.unescape(fresh[2]) and all(s not in fresh[2] for s in ('sender@example.test', 're_synthetic', 'SenderEmail', 'SenderName', 'Version', 'ConfiguredSender')))
            sql('''INSERT INTO "Subscribers" ("Id","EmailAddress","AddressKey","Status","Suppression","CreatedUtc","UpdatedUtc","VerifiedUtc","Version","PeriodId") VALUES (gen_random_uuid(),'home-reader@example.test','home-reader@example.test',1,0,'2026-01-01Z','2026-01-01Z','2026-01-01Z',2,gen_random_uuid())''')
            sql(f'''INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc") SELECT '{session}',"Id","Version","PeriodId",now(),now()+interval '7 days' FROM "Subscribers" WHERE "AddressKey"='home-reader@example.test' ''')
            request('/fixture-home/reader', opener=reader)
            before = snapshot()
            member = request(opener=reader)
            check('current_reader_gets_archive_and_no_form_or_address', member[0] == 200 and 'reader-archive-cta' in member[2] and 'registration-form' not in member[2] and 'home-reader@example.test' not in member[2] and request('/bultenler', opener=reader)[0] == 200 and snapshot() == before)
            for label, created, revoked in (('expired', "now()-interval '8 days'", 'NULL'), ('revoked', 'now()', 'now()')):
                invalid_id = str(uuid.uuid4())
                sql(f'''INSERT INTO "ReaderSessions" ("Id","SubscriberId","SubscriberVersion","PeriodId","CreatedUtc","ExpiresUtc","RevokedUtc") SELECT '{invalid_id}',"Id","Version","PeriodId",{created},{created}+interval '7 days',{revoked} FROM "Subscribers" WHERE "AddressKey"='home-reader@example.test' ''')
                invalid_reader = client()
                request('/fixture-home/reader?session=' + invalid_id, opener=invalid_reader)
                check(label + '_session_cannot_get_archive_cta', 'reader-archive-cta' not in request(opener=invalid_reader)[2] and request('/bultenler', opener=invalid_reader)[0] == 401)
            unknown = client()
            request('/fixture-home/reader?session=' + str(uuid.uuid4()), opener=unknown)
            check('signed_but_unknown_session_is_not_membership', 'reader-archive-cta' not in request(opener=unknown)[2])
            check('alternating_cookies_never_share_personalization', all('reader-archive-cta' in request(opener=reader)[2] and 'registration-form' in request()[2] for _ in range(3)))
            forged = request(headers={'Cookie': 'AetherNotes.Reader=forged'})
            check('forged_cookie_cannot_get_archive_cta', forged[0] == 200 and 'reader-archive-cta' not in forged[2])
            before_rss = snapshot()
            for name, passed in verify_rss_removal(request).items():
                check(name, passed)
            check('retired_rss_and_navigation_reads_preserve_data_and_do_not_send', snapshot() == before_rss and not packet.exists())
            if a.browser_module and a.browser_executable:
                start()  # Reset HTTP attempt budgets before the independent browser journey.
                result = run(['node', str(a.browser_script or root / 'scripts/verification/home_subscription_browser_probe.cjs')], {'DEVCORE_PLAYWRIGHT_MODULE': str(a.browser_module), 'DEVCORE_BROWSER_EXECUTABLE': str(a.browser_executable), 'F46_REPORT_DIR': str(a.report.parent)}, required=False, timeout=150)
                if result.returncode:
                    print(result.stdout[-1400:], flush=True)
                browser_report = json.loads((a.report.parent / 'home-browser.json').read_text())
                check('real_chrome_desktop_mobile_keyboard_javascript_free', result.returncode == 0 and browser_report['passed'])
            sql('UPDATE "Subscribers" SET "Suppression"=1,"Version"="Version"+1,"UpdatedUtc"=now() WHERE "AddressKey"=\'home-reader@example.test\'')
            check('suppressed_stale_reader_loses_cta_on_next_get', 'reader-archive-cta' not in request(opener=reader)[2] and request('/bultenler', opener=reader)[0] == 401)
            start({'SUBSCRIPTIONS_ENABLED': 'false'})
            closed = request()
            denied = submit('closed@example.test')
            check('closed_registration_disabled_and_503_never_success', 'disabled="disabled"' in closed[2] and 'Abonelik başvuruları şu anda kapalı.' in html.unescape(closed[2]) and denied[0] == 503 and 'Adresiniz başvuruya uygunsa' not in html.unescape(denied[2]) and sql('SELECT count(*) FROM "Subscribers" WHERE "AddressKey"=\'closed@example.test\'') == '0')
            start()
            # PostgreSQL must observe an actual table lock, rather than a mocked storage exception.
            locker = subprocess.Popen(['psql', '-X', '-h', '127.0.0.1', '-p', pg_port, '-d', 'home_test', '-c', 'BEGIN; LOCK TABLE "PublicationSettings" IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(8); ROLLBACK;'], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                for _ in range(100):
                    if sql("SELECT count(*) FROM pg_locks WHERE relation='\"PublicationSettings\"'::regclass AND mode='AccessExclusiveLock' AND granted") == '1':
                        break
                    time.sleep(.02)
                began = time.monotonic()
                blocked = request()
                check('real_lock_timeout_503_no_fake_default_or_form', blocked[0] == 503 and time.monotonic() - began < 8 and 'registration-form' not in blocked[2] and 'id="publication-name"' not in blocked[2] and 'no-store' in blocked[1].get('cache-control', ''))
            finally:
                locker.wait(timeout=12)
            start()
            rate = [submit('quota@example.test') for _ in range(7)]
            check('existing_registration_rate_limit_still_429_without_false_success', any(r[0] == 429 and 'Adresiniz başvuruya uygunsa' not in html.unescape(r[2]) and 'no-store' in r[1].get('cache-control', '') for r in rate))
            # With the real DB stopped, home and form post must preserve failure status.
            start()
            token = Fields(request()[2]).values['__RequestVerificationToken']
            run(['pg_ctl', '-D', owned + '/pg', '-m', 'fast', '-w', 'stop'])
            pg_started = False
            outage, admission = request(), submit('outage@example.test', token)
            check('real_database_outage_home_and_admission_503', outage[0] == admission[0] == 503 and 'registration-form' not in outage[2] and 'id="publication-name"' not in outage[2] and 'Adresiniz başvuruya uygunsa' not in html.unescape(admission[2]) and all('no-store' in r[1].get('cache-control', '') for r in (outage, admission)))
            stop()
            stream.flush()
            check('logs_do_not_expose_addresses_settings_tokens_or_SQL', all(s not in (f / 'web.log').read_text() for s in ('home-reader@example.test', 'home-new@example.test', 'sender@example.test', 're_synthetic', 'SELECT ', 'Aether & Yeni', token)))
        finally:
            stop()
            stream.close()
            if pg_started:
                run(['pg_ctl', '-D', owned + '/pg', '-m', 'fast', '-w', 'stop'], required=False)
    check('owned_cluster_source_keys_and_packets_removed', not f.exists())
except Exception as e:
    checks['fixture_completed'] = False
    print(str(e) if isinstance(e, RuntimeError) else type(e).__name__, flush=True)
finally:
    a.report.write_text(json.dumps({'passed': bool(checks) and all(checks.values()), 'count': len(checks), 'checks': checks, 'scope': 'Real isolated PG16/MVC/cookies/CSRF/actual public SQL projection. Scheduler suppressed and transport fake only in owned source copy; no provider calls.'}, indent=2) + '\n')
raise SystemExit(0 if all(checks.values()) else 1)
