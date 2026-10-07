#!/usr/bin/env python3
"""Check information pages on the disposable F17 application only."""
import json
import urllib.request
import urllib.error
from html.parser import HTMLParser

base = 'http://127.0.0.1:15180'
checks = {}
class Page(HTMLParser):
    def __init__(self, body):
        super().__init__(); self.links=[]; self.metas=[]; self.tags=[]; self.forms=[]; self.feed(body)
    def handle_starttag(self, tag, attrs):
        attrs=dict(attrs); self.tags.append(tag)
        if tag=='form': self.forms.append(attrs)
        if tag=='a': self.links.append(attrs)
        if tag=='link': self.metas.append(attrs)
def fetch(path, method='GET', headers=None):
    req=urllib.request.Request(base+path,method=method,headers=headers or {})
    try:
        with urllib.request.urlopen(req,timeout=10) as r:
            return r.status,dict(r.headers),r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code,dict(e.headers),e.read().decode()
def check(name, condition):
    checks[name]=bool(condition); assert condition,name
for path in ['/about','/contact']:
    status,headers,body=fetch(path,headers={'X-Forwarded-Host':'spoof.example','X-Forwarded-Proto':'http'})
    page=Page(body)
    check(path+'_200',status==200)
    check(path+'_no_store','no-store' in headers.get('Cache-Control',''))
    check(path+'_identity','Mehmet Can' in body)
    check(path+'_canonical',any(m.get('rel')=='canonical' and m.get('href')=='https://blog.example.test'+path for m in page.metas))
    check(path+'_footer',all(any(a.get('href')==target for a in page.links) for target in ['/about','/contact','/rss.xml']))
    check(path+'_reader_sign_in',any(a.get('href')=='/sign-in' for a in page.links))
    check(path+'_no_admin_link',all(a.get('href')!='/Account/Login' for a in page.links))
    check(path+'_github',any(a.get('href')=='https://github.com/CixxMemo' for a in page.links))
    check(path+'_no_message_form',all(f.get('action')=='/ara' and f.get('method')=='get' for f in page.forms))
    check(path+'_not_article','"@type":"BlogPosting"' not in body)
    status,_,body=fetch(path,'HEAD');check(path+'_HEAD',status==200 and body=='')
    status,_,_=fetch(path,'POST');check(path+'_POST_rejected',status in [400,405])
    status,_,_=fetch(path,headers={'Host':'spoof.example'});check(path+'_unknown_host_rejected',status==400)
status,_,body=fetch('/contact');page=Page(body)
check('mailto_exact',any(a.get('href')=='mailto:mehmet.kocakurt.can@gmail.com' for a in page.links))
status,_,body=fetch('/about?author=Fake&email=attacker@example.test')
check('query_cannot_replace_identity','Mehmet Can' in body and 'attacker@example.test' not in body)
print(json.dumps({'checks':checks,'passed':len(checks)},indent=2))
