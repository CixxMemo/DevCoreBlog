#!/usr/bin/env python3
"""Email-only HTTP admission/rendering with real no-write PostgreSQL snapshots."""
import argparse, html, json, os, subprocess
from pathlib import Path
from urllib.parse import urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers, extract_antiforgery_token
p=argparse.ArgumentParser();p.add_argument('--base-url',required=True);p.add_argument('--database-port',required=True);p.add_argument('--report',required=True);a=p.parse_args();base=a.base_url.rstrip('/');assert urlparse(base).hostname=='127.0.0.1'
checks={}
def snapshot():
 return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c','SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'],text=True)
def envelope(content):return json.dumps({'version':1,'document':{'type':'doc','content':content}},ensure_ascii=False)
admin,_=cookie_opener();assert submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2]==200
form=request_with_headers(admin,base+'/AdminWriting/Create');token=extract_antiforgery_token(form[2]);url=base+'/AdminEmail/Preview';before=snapshot()
valid=envelope([{'type':'heading','attrs':{'level':2},'content':[{'type':'text','text':'Türkçe başlık 👋'}]},{'type':'paragraph','content':[{'type':'text','text':'<script>yalnız metin</script>'}]},{'type':'table','content':[{'type':'tableRow','content':[{'type':'tableCell','content':[{'type':'paragraph','content':[{'type':'text','text':'Korunan hücre'}]}]}]}]},{'type':'image','attrs':{'src':'https://example.test/preview.gif','alt':'Örnek GIF'}},{'type':'youtube','attrs':{'src':'https://youtu.be/jNQXAC9IVRw'}}])
cases={'user_css':(envelope([{'type':'paragraph','attrs':{'style':'color:red'},'content':[{'type':'text','text':'x'}]}]),400),'valid':(valid,200),'malformed':('{',400),'unknown_version':(valid.replace('"version": 1','"version": 2'),400),'unknown_node':(envelope([{'type':'futureBlock'}]),400),'raw_html':(envelope([{'type':'html','text':'<script>x</script>'}]),400),'unsafe_image':(envelope([{'type':'image','attrs':{'src':'javascript:alert(1)'}}]),400),'text_limit':(envelope([{'type':'paragraph','content':[{'type':'text','text':'x'*200001}]}]),413),'byte_limit':('x'*1048577,413)}
for name,(doc,status) in cases.items():
 response=request_with_headers(admin,url,data={'Input.DocumentJson':doc,'Input.FromEditor':'true','__RequestVerificationToken':token,'SaveAction':'Publish','Id':'2003'})
 checks[name+'_status_and_no_database_change']=response[0]==status and snapshot()==before
 checks[name+'_private_headers']='no-store' in response[3].get('Cache-Control','') and response[3].get('X-Robots-Tag')=='noindex, nofollow, noarchive'
 checks[name+'_editor_view_has_no_json_form_or_receipt']='<textarea' not in response[2] and 'data-document-receipt' not in response[2]
 if name=='valid':
  checks['same_safe_email_renderer_composite_output']=all(t in html.unescape(response[2]) for t in ['Türkçe başlık','Korunan hücre','<table','example.test/preview.gif','https://www.youtube.com/watch?v=jNQXAC9IVRw','<script>yalnız metin</script>']) and '<script>yalnız metin</script>' not in response[2] and '<iframe' not in response[2] and 'Düz metin alternatifini göster' in response[2]
  checks['narrow_style_hash_csp']=all(t in response[3].get('Content-Security-Policy','') for t in ["script-src 'self'","script-src-attr 'none'","style-src-attr 'unsafe-hashes'"]) and response[3]['Content-Security-Policy'].count("'sha256-")==10 and "'unsafe-inline'" not in response[3]['Content-Security-Policy']
 else:
  checks[name+'_does_not_render_invalid_document']='class="email-preview-body"' not in response[2]
  checks[name+'_error_keeps_strict_policy']="style-src-attr 'none'" in response[3].get('Content-Security-Policy','')
csrf=request_with_headers(admin,url,data={'Input.DocumentJson':valid,'Input.FromEditor':'true'});checks['csrf_required_and_no_write']=csrf[0]==400 and snapshot()==before;checks['csrf_error_private_headers']='no-store' in csrf[3].get('Cache-Control','') and csrf[3].get('X-Robots-Tag')=='noindex, nofollow, noarchive'
anon,_=cookie_opener();login=request_with_headers(anon,base+'/fixture-admin/login');denied=request_with_headers(anon,url,data={'Input.DocumentJson':valid,'Input.FromEditor':'true','__RequestVerificationToken':extract_antiforgery_token(login[2])})
checks['auth_required_with_valid_anonymous_csrf']=denied[0]==404 and snapshot()==before
manual=request_with_headers(admin,url);checks['get_has_instructions_and_no_input_form']=manual[0]==200 and '<textarea' not in manual[2] and 'e-posta önizleme düğmesiyle' in manual[2] and snapshot()==before
flag=request_with_headers(admin,url,data={'Input.DocumentJson':valid,'Input.FromEditor':'invalid','__RequestVerificationToken':token});checks['malformed_display_flag_rejected']=flag[0]==400 and snapshot()==before
web=request_with_headers(admin,base+'/AdminDocument/Preview',data={'Input.DocumentJson':valid,'Input.FromEditor':'true','__RequestVerificationToken':token});checks['web_preview_policy_and_iframe_preserved']=web[0]==200 and "style-src-attr 'none'" in web[3].get('Content-Security-Policy','') and '<iframe' in web[2]
for page in ['/','/AdminWriting/Create']:
 r=request_with_headers(admin,base+page);checks['other_route_strict_'+page]="style-src-attr 'none'" in r[3].get('Content-Security-Policy','')
Path(a.report).write_text(json.dumps({'count':len(checks),'checks':checks},ensure_ascii=False,indent=2)+'\n');print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}));assert all(checks.values())
