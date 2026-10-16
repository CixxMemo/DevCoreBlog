"""Check retired RSS routes and public navigation on the owned MVC fixture."""
from html.parser import HTMLParser
from urllib.parse import urlsplit


class PublicLinks(HTMLParser):
    def __init__(self, body):
        super().__init__()
        self.feed_links = []
        self.forms = []
        self.feed(body)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'form':
            self.forms.append(attrs)
        if tag not in ('a', 'link'):
            return
        path = urlsplit(attrs.get('href', '')).path.lower().rstrip('/')
        if attrs.get('type', '').lower() in ('application/rss+xml', 'application/atom+xml') or path in ('/rss.xml', '/seo/rss'):
            self.feed_links.append(attrs)


def verify(request):
    """Return explicit HTTP checks; the caller additionally compares its full DB snapshot."""
    checks = {}
    for path in ('/rss.xml', '/rss.xml/', '/RSS.XML', '/rss.xml?count=999&page=2',
                 '/Seo/Rss', '/seo/rss/', '/Seo/Rss?slug=home-4'):
        for method in ('GET', 'HEAD'):
            status, headers, body = request(path, method=method, headers={'Accept': 'application/rss+xml'})
            checks[f'rss_removed_{method}_{path}'] = (
                status == 404 and 'location' not in headers and
                'application/rss+xml' not in headers.get('content-type', '') and
                '<rss' not in body.lower() and '<feed' not in body.lower() and
                'PRIVATE_HOME_TITLE' not in body and 'Deney sonucunu birlikte' not in body and
                'no-store' in headers.get('cache-control', '') and
                bool(headers.get('content-security-policy')) and
                headers.get('x-content-type-options') == 'nosniff' and
                (method != 'HEAD' or body == ''))
    for path in ('/', '/?kind=news', '/category/home-fixture', '/ara?query=F48',
                 '/bulten', '/about', '/contact', '/sign-in', '/abone-ol'):
        status, _, body = request(path)
        links = PublicLinks(body)
        checks['no_rss_discovery_or_navigation_' + path] = status == 200 and not links.feed_links
    status, _, body = request('/')
    forms = PublicLinks(body).forms
    checks['rss_replaced_by_existing_protected_email_form'] = (
        status == 200 and any(form.get('action') == '/abone-ol' and form.get('method', '').lower() == 'post' for form in forms) and
        '__RequestVerificationToken' in body and 'href="/sign-in"' in body)
    return checks
