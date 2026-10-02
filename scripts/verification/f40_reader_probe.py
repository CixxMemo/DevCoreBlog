#!/usr/bin/env python3
"""Bounded reader HTTP acceptance using only the owned disposable F17 fixture."""
import argparse
import json
import re
import subprocess
from html.parser import HTMLParser
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlencode, urlparse, parse_qs
from urllib.request import urlopen

class Page(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.cards, self.next, self.previous = [], [], []
        self.feed(html)
    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag != 'a': return
        href = attrs.get('href', '')
        if 'post-card' in attrs.get('class', '').split(): self.cards.append(href)
        if 'page=' in href: self.next.append(href)

def request(path):
    try:
        with urlopen('http://127.0.0.1:15171'+path) as response:
            return response.status, response.read().decode()
    except HTTPError as error:
        return error.code, error.read().decode()

def search(**values):
    return request('/ara?' + urlencode(values))

def sql(statement):
    return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p','55443','-d','devcoreblog_f01_test',
        '-At','-v','ON_ERROR_STOP=1','-c',statement]).decode().strip()

parser=argparse.ArgumentParser()
parser.add_argument('--fixture-root')
parser.add_argument('--seed',action='store_true')
parser.add_argument('--baseline',action='store_true')
parser.add_argument('--application-log')
args=parser.parse_args()
if args.seed:
    root=Path(args.fixture_root).resolve()
    assert root.parent==Path('/tmp').resolve() and root.name.startswith('devcoreblog-f17.')
    assert (root/'postgres/postmaster.pid').read_text().splitlines()[3]=='55443'
    sql('''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion")
        VALUES (1003,'F40 Second Category','f40-second','2000-01-01Z',true,1);
        INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content","ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion")
        SELECT 4000+n,'F40 needle '||lpad(n::text,2,'0'),'f40-needle-'||n,'F40 summary','','',true,
        'F40 body text',0,'2000-01-01Z',CASE WHEN n>36 THEN 1003 ELSE 1001 END,'2000-01-01Z',true,1
        FROM generate_series(1,41) n;
        INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content","ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion")
        VALUES (4050,'Body match','f40-body','','','',true,'needle',0,'2000-01-01Z',1001,'2000-01-01Z',true,1),
        (4051,'needle','f40-exact','','','',true,'exact',0,'2000-01-01Z',1001,'2000-01-01Z',true,1),
        (4052,'Literal %_ character','f40-literal','','','',true,'literal',0,'2000-01-01Z',1001,'2000-01-01Z',true,1);
        UPDATE "Posts" SET "Title"='F40 needle hidden' WHERE "Id" IN (2002,2003,2004,2007,2008);''')
status, first = search(query='needle', category='f01-active', pageSize=18)
_, second = search(query='needle', category='f01-active', pageSize=18, page=2)
p1, p2 = Page(first), Page(second)
checks={
    'page_size_18': status==200 and len(p1.cards)==18,
    'no_page_overlap': len(p2.cards)==18 and not set(p1.cards)&set(p2.cards),
    'stable_rank_and_id': p1.cards==['/yazi/f40-exact']+['/yazi/f40-needle-'+str(n) for n in range(1,18)],
    'repeat_order_stable': Page(search(query='needle',category='f01-active',pageSize=18)[1]).cards==p1.cards,
    'next_preserves_filters': any(parse_qs(urlparse(link).query)=={'query':['needle'],'category':['f01-active'],'page':['2'],'pageSize':['18']} for link in p1.next),
    'previous_preserves_filters': any(parse_qs(urlparse(link).query)=={'query':['needle'],'category':['f01-active'],'page':['1'],'pageSize':['18']} for link in p2.next),
    'total_and_category_context': '38 articles found' in first and 'F01 Active Category' in first and 'Search query:' in first,
    'body_matches_after_title_matches': '/yazi/f40-body' in Page(search(query='needle',category='f01-active',pageSize=18,page=3)[1]).cards,
    'inactive_draft_future_hidden': all(slug not in first+second for slug in ['href="/yazi/f01-draft"','href="/yazi/f01-future-visible-marker"','href="/yazi/f01-inactive-post"','href="/yazi/f01-inactive-category"']),
    'page_size_27': len(Page(search(query='needle',pageSize=27)[1]).cards)==27,
    'page_size_default_9': len(Page(search(query='needle')[1]).cards)==9,
    'literal_percent_underscore': Page(search(query='%_')[1]).cards==['/yazi/f40-literal'],
    'unknown_category_404': search(query='needle',category='absent')[0]==404,
    'inactive_category_404': search(query='needle',category='f01-inactive')[0]==404,
    'absent_page_404': search(query='needle',page=1000)[0]==404,
    'empty_first_page_200': search(query='not-found-marker')[0]==200,
    'empty_next_page_404': search(query='not-found-marker',page=2)[0]==404,
    'blank_prompts_without_all_posts': not Page(search(query='   ')[1]).cards,
    'sql_injection_is_literal': not Page(search(query="' OR 1=1--")[1]).cards,
    'term_is_encoded': '&lt;script&gt;' in search(query='<script>')[1] and '<strong><script>' not in search(query='<script>')[1],
}
for field,value in [('query','x'*101),('category','x'*201),('page',-1),('page',0),('page',1001),('page',2147483648),('page','bad'),('pageSize',1000000),('pageSize',10),('pageSize','bad')]:
    checks[f'invalid_{field}_{str(value)[:10]}_400']=search(**{field:value})[0]==400
for route in ['/', '/kategori/f01-active']:
    name='home' if route=='/' else 'category'
    for field,value in [('page',-1),('page',1001),('page','bad'),('pageSize',10)]:
        checks[f'{name}_{field}_{value}_400']=request(route+'?'+urlencode({field:value}))[0]==400
    checks[name+'_size_18']=len(Page(request(route+'?pageSize=18')[1]).cards)==18
    checks[name+'_absent_page_404']=request(route+'?page=1000')[0]==404

if not args.baseline:
    locale=sql("SELECT current_setting('server_version'), datcollate, datctype, datlocprovider FROM pg_database WHERE datname=current_database();")
    examples={term:Page(search(query=term)[1]).cards for term in ['İstanbul','İSTANBUL','istanbul','IĞDIR','ığdır','ğüşiöç','ĞÜŞİÖÇ']}
    # Compare HTTP matching with the real database locale, without assuming Turkish folding.
    for term, cards in examples.items():
        escaped=term.replace("'","''")
        expected=sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2006 AND ("Title" ILIKE \'%'+escaped+'%\' OR "Content" ILIKE \'%'+escaped+'%\');')
        checks['locale_'+term]=('/yazi/f01-turkce-karakterler' in cards)==bool(expected)
    print(json.dumps({'postgres':locale,'turkish_examples':examples},ensure_ascii=False,indent=2))
    if args.application_log:
        lines=Path(args.application_log).read_text().splitlines()
        commands=[]
        for i,line in enumerate(lines):
            if 'SELECT p."Id", p."Title", p."Slug", p."Summary"' in line:
                block=[]
                for item in lines[i:]:
                    if item.startswith('info:'):break
                    block.append(item)
                if any('ILIKE' in item for item in block):commands.append('\n'.join(block))
        checks['sql_projection_has_no_body']=bool(commands) and all('p."Content"' not in command.split('FROM')[0] for command in commands)
        checks['sql_filter_order_limit_offset']=any(all(token in command for token in ['ILIKE','"IsPublished"','"IsActive"','"PublishDate" <=','ORDER BY','LIMIT','OFFSET']) for command in commands)
        filtered=[command for command in commands if 'c."Slug" =' in command]
        counts=[]
        for i,line in enumerate(lines):
            if 'SELECT count(*)::int' in line:
                block=[]
                for item in lines[i:]:
                    if item.startswith('info:'): break
                    block.append(item)
                command='\n'.join(block)
                if 'ILIKE' in command and 'c."Slug" =' in command: counts.append(command)
        checks['sql_count_keeps_category_and_term']=bool(counts)
        proof=Path('docs/uygulama-kayitlari/kanit/F40/search-sql.log')
        proof.write_text('\n\n'.join(counts[:1]+filtered[:2])+'\n')
print(json.dumps({'baseline':args.baseline,'checks':checks},ensure_ascii=False,indent=2))
assert any(not x for x in checks.values()) if args.baseline else all(checks.values()),checks
