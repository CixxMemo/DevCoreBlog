"""AY03 on the owned PostgreSQL/MVC fixture: published newsletters are public, hidden ones stay hidden, old addresses redirect once."""
import argparse
import html
import json
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', required=True)
parser.add_argument('--database-port', required=True)
parser.add_argument('--report-dir', type=Path, required=True)
args = parser.parse_args()
base = args.base_url.rstrip('/')
assert urllib.parse.urlparse(base).hostname == '127.0.0.1'
assert args.database_port in ['55485', '55478']
args.report_dir.mkdir(parents=True, exist_ok=True)
checks = {}


def sql(query):
    return subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', args.database_port, '-d', 'devcoreblog_f01_test',
                                    '-At', '-v', 'ON_ERROR_STOP=1', '-c', query], text=True).strip()


def literal(value):
    return "'" + value.replace("'", "''") + "'"


def snapshot():
    return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";')


def check(name, passed):
    checks[name] = bool(passed)
    assert passed, name


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *unused):
        return None


opener = urllib.request.build_opener(NoRedirect())


def fetch(path, method='GET', headers=None):
    request = urllib.request.Request(base + path, method=method, headers=headers or {}, data=b'' if method == 'POST' else None)
    try:
        response = opener.open(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, dict(response.headers.items()), response.read().decode('utf-8', 'replace')


def body_of(name):
    return 'F32_BODY_' + name.replace('-', '_').upper()


def title_of(name):
    return 'F32 başlık ' + name


original = snapshot()
active = int(sql('SELECT "Id" FROM "Categories" WHERE "IsActive" ORDER BY "Id" LIMIT 1'))
inactive = int(sql('SELECT "Id" FROM "Categories" WHERE NOT "IsActive" ORDER BY "Id" LIMIT 1'))
# name, active, published, category, kind, scope, date. now(): earlier stage posts must not push these off the first listing page.
fixture = [('newsletter', True, True, active, 4, 1, 'now()'), ('scoped-article', True, True, active, 1, 1, 'now()'),
           ('draft', True, False, active, 4, 1, 'now()'), ('future', True, True, active, 4, 1, "'2999-01-01'"),
           ('inactive', False, True, active, 4, 1, 'now()'), ('inactive-category', True, True, inactive, 4, 1, 'now()')]
opened = ('newsletter', 'scoped-article')
hidden = ('draft', 'future', 'inactive', 'inactive-category')
ids = []
try:
    for index, (name, is_active, published, category, kind, scope, date) in enumerate(fixture):
        identity = 932000 + index
        ids.append(identity)
        document = json.dumps({'version': 1, 'document': {'type': 'doc', 'content': [{'type': 'paragraph', 'content': [{'type': 'text', 'text': body_of(name)}]}]}})
        sql(f'''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","Excerpt","ThumbnailUrl","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","IsPublished","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes","EditVersion")
            VALUES ({identity},{literal(title_of(name))},{literal('f32-' + name)},'Kısa özet.',{literal(body_of(name))},'','',{category},'2020-01-01',{str(is_active).lower()},0,{date},{str(published).lower()},{kind},{scope},1,{literal(document)},{literal(body_of(name))},1,1,1)''')

    seeded = snapshot()
    for path in ('/bulten', '/bultenler', '/bulten?page=2', '/BULTEN', '/bultenler/'):
        status, headers, _ = fetch(path)
        check('list_address_redirects_home_' + path, status == 301 and headers.get('Location') == '/')
    status, headers, payload = fetch('/bulten', method='HEAD')
    check('head_redirects_without_body', status == 301 and payload == '' and headers.get('Location') == '/')
    check('post_is_not_accepted', fetch('/bulten', method='POST')[0] in (400, 404, 405))
    for name in opened:
        status, headers, _ = fetch('/bulten/f32-' + name)
        check('old_address_redirects_to_single_public_address_' + name, status == 301 and headers.get('Location') == '/post/f32-' + name)
    for name in hidden:
        check('old_address_of_hidden_post_stays_404_' + name, fetch('/bulten/f32-' + name)[0] == 404)
    check('old_address_of_absent_post_is_404', fetch('/bulten/f32-yok')[0] == 404)
    check('overlong_slug_rejected', fetch('/bulten/' + 'x' * 201)[0] == 400)
    check('redirects_write_nothing_and_count_nothing', snapshot() == seeded)

    for name in opened:
        status, headers, payload = fetch('/post/f32-' + name)
        check('anonymous_reads_' + name, status == 200 and body_of(name) in payload and title_of(name) in html.unescape(payload))
        check('public_article_metadata_' + name, 'BlogPosting' in payload and 'noindex' not in payload.split('</head>')[0] and 'blog.example.test/post/f32-' + name in payload)
        check('security_headers_kept_' + name, "script-src 'self'" in headers.get('Content-Security-Policy', '') and headers.get('X-Content-Type-Options') == 'nosniff')
    for name in hidden:
        status, _, payload = fetch('/post/f32-' + name)
        check('hidden_post_stays_404_' + name, status == 404 and body_of(name) not in payload)
        check('hidden_post_head_stays_404_' + name, fetch('/post/f32-' + name, method='HEAD')[0] == 404)

    surfaces = {'home': '/?pageSize=27', 'search': '/ara?query=' + urllib.parse.quote('F32 başlık'), 'category': '/category/' + sql(f'SELECT "Slug" FROM "Categories" WHERE "Id"={active}') + '?pageSize=27', 'sitemap': '/sitemap.xml'}
    for label, path in surfaces.items():
        status, _, payload = fetch(path)
        text = html.unescape(payload)
        marker = (lambda name: 'f32-' + name) if label == 'sitemap' else title_of
        check('opened_posts_listed_in_' + label, status == 200 and all(marker(name) in text for name in opened))
        check('hidden_posts_absent_from_' + label, all(marker(name) not in text for name in hidden))
    portfolio = fetch('/api/public/posts/latest')
    check('hidden_posts_absent_from_portfolio', portfolio[0] == 200 and not any('f32-' + name in portfolio[2] for name in hidden))
    # A seeded article: reading a fixture post would change its counter and break the restore check below.
    related = fetch('/post/f32-scoped-article')
    check('hidden_posts_absent_from_related', related[0] == 200 and not any(title_of(name) in html.unescape(related[2]) for name in hidden))

    visible = int(sql('''SELECT count(*) FROM "Posts" p JOIN "Categories" c ON c."Id"=p."CategoryId" WHERE p."IsActive" AND p."IsPublished" AND c."IsActive" AND p."PublishDate"<=now() AND p."AccessScope" IN (0,1) AND p."ContentKind" IN (0,1,2,3,4)'''))
    counted = re.search(r'(\d+) yazı', html.unescape(fetch('/?pageSize=27')[2]))
    check('home_total_matches_database_rule', counted is not None and int(counted.group(1)) == visible)
    searched = re.search(r'(\d+) (?:yazı|sonuç)', html.unescape(fetch(surfaces['search'])[2]))
    check('search_total_counts_only_opened_posts', searched is not None and int(searched.group(1)) == len(opened))

    forged = fetch('/post/f32-newsletter', headers={'Cookie': 'AetherNotes.Reader=forged'})
    check('forged_reader_cookie_changes_nothing', forged[0] == 200 and body_of('newsletter') in forged[2])
    counter = sql('SELECT "ViewCount" FROM "Posts" WHERE "Slug"=\'f32-newsletter\'')
    fetch('/post/f32-newsletter', method='HEAD')
    fetch('/bulten/f32-newsletter')
    check('head_and_redirect_do_not_count', sql('SELECT "ViewCount" FROM "Posts" WHERE "Slug"=\'f32-newsletter\'') == counter)
    fetch('/post/f32-newsletter')
    check('real_read_counts_once', int(sql('SELECT "ViewCount" FROM "Posts" WHERE "Slug"=\'f32-newsletter\'')) == int(counter) + 1)

    sql('UPDATE "Posts" SET "PublishDate"=\'2020-01-01\' WHERE "Slug"=\'f32-future\'')
    check('scheduled_post_opens_on_next_request_once_due', fetch('/post/f32-future')[0] == 200 and fetch('/bulten/f32-future')[0] == 301)
    sql('UPDATE "Posts" SET "IsPublished"=false WHERE "Slug"=\'f32-newsletter\'')
    check('unpublishing_hides_again', fetch('/post/f32-newsletter')[0] == 404 and fetch('/bulten/f32-newsletter')[0] == 404)
    sql('UPDATE "Posts" SET "IsPublished"=true WHERE "Slug"=\'f32-newsletter\'')
    sql('''UPDATE "Posts" SET "DocumentJson"='{"version":1,"document":{"type":"doc","content":"bozuk"}}' WHERE "Slug"='f32-newsletter' ''')
    broken = fetch('/post/f32-newsletter')
    check('broken_document_stays_safe_503', broken[0] == 503 and 'bozuk' not in broken[2] and body_of('newsletter') not in broken[2])

    (args.report_dir / 'newsletter-public-http.json').write_text(json.dumps({'passed': True, 'count': len(checks), 'checks': checks,
        'scope': 'Actual owned MVC and PostgreSQL. Former subscriber posts public, hidden posts hidden, one permanent redirect for old addresses.'}, indent=2) + '\n')
finally:
    if ids:
        sql('DELETE FROM "Posts" WHERE "Id" IN (' + ','.join(map(str, ids)) + ')')
    assert snapshot() == original, 'Owned fixture was not restored'
print('newsletter_public_http=true')
