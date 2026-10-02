#!/usr/bin/env python3
"""Reading acceptance against the owned disposable F17 PostgreSQL fixture."""
import argparse
import json
import subprocess
from html.parser import HTMLParser
from pathlib import Path
from urllib.request import urlopen

class Article(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.headings = []
        self.toc = []
        self.related = []
        self.h1 = 0
        self.feed(html)

    def handle_starttag(self, tag, attributes):
        attributes = dict(attributes)
        if tag == 'h1':
            self.h1 += 1
        target = attributes.get('id', '')
        if tag.startswith('h') and target.startswith('markdown-section-'):
            self.headings.append(target)
        if tag == 'a':
            href = attributes.get('href', '')
            if href.startswith('#markdown-section-'):
                self.toc.append(href[1:])
            if 'post-card' in attributes.get('class', '').split():
                self.related.append(href)

def fetch(path):
    with urlopen('http://127.0.0.1:15171' + path) as response:
        return response.read().decode()

def seed(root):
    root = Path(root).resolve()
    assert root.parent == Path('/tmp').resolve() and root.name.startswith('devcoreblog-f17.')
    assert (root / 'postgres/postmaster.pid').read_text().splitlines()[3] == '55443'
    long_text = '# Start\n\n' + 'Readable technical words. ' * 150 + '''

## Repeated **heading**

### Repeated **heading**

## Code & table

```csharp
Console.WriteLine("safe copy text");
'''+ '// ' + 'long_code_' * 120 + '''
```

| Name | Value |
| --- | --- |
| wide | ''' + 'wide_' * 140 + ''' |

![Actual supplied description](/f39-intentionally-missing.png)

[video](https://youtu.be/dQw4w9WgXcQ)

<script>window.__f39Xss=true</script>

[unsafe](javascript:alert(1))
'''
    # Reference definitions contain no rendered article text.
    short_text = 'A short **readable** article.\n\n' + '\n'.join(
        f'[unused{n}]: https://example.test/{n} "' + 'metadata ' * 100 + '"' for n in range(20))
    values = []
    for post_id, title, slug, content in [(3001, 'F39 Long article', 'f39-long', long_text),
                                           (3002, 'F39 Short article', 'f39-short', short_text)]:
        quote = lambda value: "'" + value.replace("'", "''") + "'"
        values.append(f'({post_id},{quote(title)},{quote(slug)},\'\',\'\',\'\',true,{quote(content)},0,\'2020-01-01Z\',1001,\'2020-01-01Z\',true,1)')
    sql = '''INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content","ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion") VALUES ''' + ','.join(values) + ';'
    subprocess.run(['psql', '-X', '-h', '127.0.0.1', '-p', '55443', '-d', 'devcoreblog_f01_test',
                    '-v', 'ON_ERROR_STOP=1'], input=sql, text=True, check=True, stdout=subprocess.DEVNULL)

parser = argparse.ArgumentParser()
parser.add_argument('--seed', action='store_true')
parser.add_argument('--fixture-root')
parser.add_argument('--baseline', action='store_true')
args = parser.parse_args()
if args.seed:
    seed(args.fixture_root)
long_html = fetch('/yazi/f39-long')
short_html = fetch('/yazi/f39-short')
page = Article(long_html)
checks = {
    'single_page_h1': page.h1 == 1,
    'heading_ids_unique': len(page.headings) == 4 and len(set(page.headings)) == 4,
    'toc_targets_exact': page.toc == page.headings and len(page.toc) == 4,
    'short_no_toc': not Article(short_html).toc,
    'reading_ignores_reference_metadata': '>1 min read<' in short_html,
    'long_reading_estimate': '>3 min read<' in long_html,
    'related_bounded': 0 < len(page.related) <= 3,
    'related_excludes_current': '/yazi/f39-long' not in page.related,
    'related_hides_draft_future_inactive': all(x not in long_html for x in
        ['href="/yazi/f01-draft"', 'href="/yazi/f01-future-visible-marker"',
         'href="/yazi/f01-inactive-post"', 'href="/yazi/f01-inactive-category"']),
    'raw_html_encoded': '<script>window.__f39Xss=true</script>' not in long_html,
    'unsafe_url_rejected': 'href="javascript:' not in long_html,
    'youtube_allowlist_preserved': 'https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ' in long_html,
    'code_text_preserved': 'Console.WriteLine(&quot;safe copy text&quot;);' in long_html or 'Console.WriteLine("safe copy text");' in long_html,
    'copy_module_present': '/js/article-reading.js' in long_html,
    'no_invented_updated_date': 'Updated ' not in long_html,
}
print(json.dumps({'baseline': args.baseline, 'checks': checks}, indent=2))
assert any(not value for value in checks.values()) if args.baseline else all(checks.values()), checks
