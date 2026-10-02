"""HTTP acceptance against the disposable F17 cluster seeded with f38_seed.sql."""
import argparse, json, subprocess
from pathlib import Path
from html.parser import HTMLParser
from urllib.request import urlopen
class Page(HTMLParser):
    def __init__(self, html):
        super().__init__(); self.cards=[];self.featured=[];self.ranking=[];self.stack=[];self.h1=0;self.feed(html)
    def handle_starttag(self,tag,attrs):
        attrs=dict(attrs)
        if tag=='h1':self.h1+=1
        if tag=='a':
            classes=attrs.get('class','').split();href=attrs.get('href','')
            if 'post-card' in classes:self.cards.append(href)
            if 'post-card-featured' in classes:self.featured.append(href)
            if 'most-read' in self.stack:self.ranking.append(href)
        if tag not in ['input','img','meta','link','br','hr']:self.stack.append(attrs.get('id',''))
    def handle_endtag(self,tag):
        if tag not in ['input','img','meta','link','br','hr'] and self.stack:self.stack.pop()
def fetch(path):
    with urlopen('http://127.0.0.1:15171'+path) as response:return response.read().decode()
p=argparse.ArgumentParser();p.add_argument('--baseline',action='store_true');p.add_argument('--empty',action='store_true');p.add_argument('--small',action='store_true');p.add_argument('--one',action='store_true');p.add_argument('--fixture-root');a=p.parse_args()
checks={}
if a.empty or a.small or a.one:
    root = Path(a.fixture_root or '').resolve()
    assert root.parent == Path('/tmp').resolve() and root.name.startswith('devcoreblog-f17.'), 'Owned temporary fixture required'
    pid = (root / 'postgres/postmaster.pid').read_text().splitlines()
    assert pid[3] == '55443', 'Unexpected fixture PostgreSQL port'
if a.small or a.one:
    ids = '3001,3002,3003,3004,3005,3006,3007,3008,3009,3010,3011,3012'
    if a.one: ids += ',2005,2006'
    command = ['psql','-h','127.0.0.1','-p','55443','-d','devcoreblog_f01_test','-v','ON_ERROR_STOP=1','-c']
    subprocess.run(command + [f'UPDATE "Posts" SET "IsActive"=false WHERE "Id" IN ({ids});'], check=True, stdout=subprocess.DEVNULL)
    try:
        html=fetch('/?f38=fixture');page=Page(html)
        expected=1 if a.one else 3
        checks={'small_card_count':len(page.cards)==expected,'small_no_duplicate_cards':len(set(page.cards))==expected,'small_one_h1':page.h1==1,'small_bounded_ranking':len(page.ranking)==expected}
    finally:
        subprocess.run(command + [f'UPDATE "Posts" SET "IsActive"=true WHERE "Id" IN ({ids});'], check=True, stdout=subprocess.DEVNULL)
elif a.empty:
    # This fixed local port belongs to our disposable fixture; never a production connection.
    subprocess.run(['psql','-h','127.0.0.1','-p','55443','-d','devcoreblog_f01_test','-v','ON_ERROR_STOP=1','-c','DELETE FROM "Posts"; DELETE FROM "Categories";'],check=True,stdout=subprocess.DEVNULL)
    html=fetch('/?f38=fixture');page=Page(html)
    checks={'empty_no_cards':not page.cards,'empty_no_ranking':not page.ranking,'empty_one_h1':page.h1==1,'empty_explained':'No articles published yet.' in html,'empty_topics_explained':'No active topics yet.' in html,'empty_no_category_links':'href="/kategori/' not in html}
else:
    one=fetch('/?f38=fixture');two=fetch('/?page=2&f38=fixture');p1,p2=Page(one),Page(two)
    checks={'first_page_featured':len(p1.featured)==1,'second_page_no_featured':not p2.featured,'first_page_no_duplicate_cards':len(p1.cards)==len(set(p1.cards)),'second_page_all_six_cards':len(p2.cards)==6,'no_page_overlap':not set(p1.cards)&set(p2.cards),'global_winner_first':bool(p1.ranking) and p1.ranking[0]=='/yazi/f38-article-12','ranking_bounded':len(p1.ranking)==5,'ranking_tie_order':p1.ranking[1:3]==['/yazi/f38-article-1','/yazi/f38-article-2'],'ranking_equal_on_pages':p1.ranking==p2.ranking and bool(p1.ranking),'one_h1_each':p1.h1==p2.h1==1,'counts_explained':'15 articles' in one and '15 articles' in two,'honest_label':'Most Read' in one and 'Trending' not in one,'hidden_never_leaks':'999999' not in one+two and all(slug not in one+two for slug in ['f01-draft','f01-future-visible-marker','f01-inactive-post','f01-inactive-category']),'deterministic_tie_second_page':p2.cards==['/yazi/f38-article-'+str(n) for n in range(7,13)],'repeat_request_stable':Page(fetch('/?page=2&f38=fixture')).cards==p2.cards}
print(json.dumps({'mode':'baseline' if a.baseline else 'empty' if a.empty else 'one' if a.one else 'small' if a.small else 'current','checks':checks},indent=2))
assert any(not v for v in checks.values()) if a.baseline else all(checks.values()),checks
