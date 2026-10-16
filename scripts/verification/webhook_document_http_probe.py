#!/usr/bin/env python3
"""Versioned ingress acceptance against a runner-owned PostgreSQL/MVC process; no real credential input."""
import argparse, concurrent.futures, json, subprocess, time, urllib.request
from pathlib import Path
from urllib.parse import urlparse
from http_probe_support import cookie_opener, request_with_headers, wait_until_ready, admin_login_url, submit_login

p=argparse.ArgumentParser();p.add_argument('--base-url',required=True);p.add_argument('--database-port',required=True);p.add_argument('--report',required=True)
p.add_argument('--mode',choices=['baseline','acceptance','restart','missing'],default='acceptance');a=p.parse_args()
class Checks(dict):
 def __setitem__(self,key,value):
  super().__setitem__(key,value);print('vol1_f17_'+key+'='+str(value).lower(),flush=True)
  report.write_text(json.dumps({'passed':all(self.values()),'count':len(self),'checks':self,'fixtures':fixtures},ensure_ascii=False,indent=2)+'\n')
base=a.base_url.rstrip('/');assert urlparse(base).hostname=='127.0.0.1';checks=Checks();fixtures={}
def sql(q):return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],text=True).strip()
def quote(v):return "'"+v.replace("'","''")+"'"
def snapshot():return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(r) FROM "WebhookReceipts" r ORDER BY "Key";')
def document(text='F17_BODY_NEEDLE İstanbul %_\\ <script>F17_SECRET_BODY</script>'):
 return json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'heading','attrs':{'level':2},'content':[{'type':'text','text':'Başlık'}]},{'type':'paragraph','content':[{'type':'text','text':text}]}]}},ensure_ascii=False)
def payload(title='F17 original',**extra):return {'title':title,'documentJson':document(),'categoryId':1001,'contentKind':'AiNews',**extra}
def send(key='f17-key',data=None,raw=None,secret='f17-webhook',path='/api/webhooks/v2/posts',content_type='application/json',retry=True,client=None):
 if client is None:client,_=cookie_opener()
 headers={'Content-Type':content_type}
 if secret is not None:headers['X-DevCore-Secret']=secret
 if key is not None:headers['Idempotency-Key']=key
 body=raw if raw is not None else json.dumps(data if data is not None else payload(),ensure_ascii=False).encode()
 end=time.monotonic()+65
 while True:
  res=request_with_headers(client,base+path,raw_data=body,headers=headers)
  if res[0]!=429 or not retry or time.monotonic()>end:break
  time.sleep(.5)
 try:decoded=json.loads(res[2])
 except ValueError:decoded={}
 return res[0],decoded,res[3]
def get(path):client,_=cookie_opener();return request_with_headers(client,base+path)
def receipt_count(key):return sql('SELECT count(*) FROM "WebhookReceipts" WHERE "ContractVersion"=2 AND "Key"='+quote(key))
client,_=cookie_opener();wait_until_ready(client,admin_login_url(base),'Yönetici girişi',30)
original=payload(isPublished=True,publishDate='2020-01-02T06:04:05.1234567+03:00')
report=Path(a.report)
if a.mode=='baseline':
 before=sql('SELECT count(*) FROM "Posts";')
 response=send(data=original)
 checks['before_version_two_post_unsupported']=response[0]==405
 checks['before_attempt_does_not_write']=sql('SELECT count(*) FROM "Posts";')==before
elif a.mode=='missing':
 before=snapshot()
 checks['empty_config_correct_header_fail_closed']=send(raw=b'{')[0]==401
 checks['empty_config_no_header_fail_closed']=send(secret=None)[0]==401
 checks['empty_config_preserves_rows']=snapshot()==before
elif a.mode=='restart':
 before=snapshot();saved=json.loads((report.parent/'webhook-document-http.json').read_text())['fixtures']['original']
 response=send(data=original)
 checks['restart_changed_publish_policy_replays_original']=response[0]==200 and response[1]==saved
 checks['restart_replay_does_not_write']=snapshot()==before
 prime_home=get('/');checks['prime_public_cache']=prime_home[0]==200
 published=send('f17-public',payload('F17 public article',isPublished=True,publishDate='2020-01-02T03:04:05Z'))
 assert published[0]==200,published[:2]
 id=published[1]['postId'];slug=published[1]['slug'];fixtures['public']={'id':id,'slug':slug}
 checks['json_webhook_insert_invalidates_public_list']=slug in get('/')[2]
 checks['explicit_publish_enabled']=published[1]['isPublished'] and sql(f'SELECT "IsPublished" FROM "Posts" WHERE "Id"={id}')=='t'
 page=get('/post/'+slug)
 checks['published_json_uses_safe_public_renderer']=page[0]==200 and 'F17_BODY_NEEDLE' in page[2] and '<script>F17_SECRET_BODY</script>' not in page[2]
 checks['published_plain_text_search_connected']=slug in get('/ara?query=F17_BODY_NEEDLE')[2]
 for kind,scope in [('Newsletter','Subscribers'),('Philosophy','Subscribers')]:
  hidden=send('f17-private-'+kind,payload('F17 private '+kind,contentKind=kind,accessScope=scope,isPublished=True))
  assert hidden[0]==200,hidden[:2]
  slug=hidden[1]['slug'];checks['private_'+kind+'_server_excluded']=get('/post/'+slug)[0]==404 and slug not in get('/ara?query=F17_BODY_NEEDLE')[2] and slug not in get('/sitemap.xml')[2] and slug not in get('/api/public/posts/latest')[2]
  fixtures[kind]=hidden[1]
 future=send('f17-future',payload('F17 scheduled',isPublished=True,publishDate='2099-01-01T00:00:00Z'))
 checks['future_published_stays_hidden']=future[0]==200 and future[1]['isPublished'] and get('/post/'+future[1]['slug'])[0]==404
 # HTTP method cannot mutate; CSRF exception applies only to the secret-authenticated POST.
 before=snapshot();checks['get_cannot_create']=get('/api/webhooks/v2/posts')[0]==404 and snapshot()==before
else:
 before=snapshot();first=send(data=original);assert first[0]==200,first[:2]
 fixtures['original']=first[1];id=first[1]['postId']
 checks['defaults_server_owned_fields']=sql(f'SELECT "ViewCount"=0 AND "EditVersion"=1 AND "IsActive" AND "UpdatedDate" IS NULL AND "Content"=\'\' AND "ContentKind"=1 AND "AccessScope"=0 FROM "Posts" WHERE "Id"={id}')=='t'
 checks['publish_permission_disabled_is_draft']=not first[1]['isPublished'] and get('/post/'+first[1]['slug'])[0]==404
 checks['utc_db_precision_matches_response']=first[1]['publishDate']=='2020-01-02T03:04:05.123456Z'
 stored=json.loads(sql(f'SELECT json_build_object(\'version\',"DocumentVersion",\'json\',"DocumentJson",\'plain\',"DocumentPlainText",\'words\',"DocumentWordCount",\'minutes\',"DocumentReadingMinutes") FROM "Posts" WHERE "Id"={id}'))
 checks['canonical_json_and_all_derivatives_commit_together']=stored['version']==1 and stored['json']==original['documentJson'] and stored['plain']=='Başlık\nF17_BODY_NEEDLE İstanbul %_\\ <script>F17_SECRET_BODY</script>' and stored['words']>0 and stored['minutes']==1
 stable=snapshot();again=send(data=dict(reversed(list(original.items()))));checks['equivalent_outer_order_replays_exact']=again[:2]==first[:2] and snapshot()==stable
 checks['different_payload_conflicts']=send(data={**original,'title':'different'})[0]==409 and snapshot()==stable
 checks['canonical_text_wire_change_conflicts']=send(data={**original,'documentJson':original['documentJson']+' '})[0]==409
 # Same key independently creates v1 Markdown and v2 JSON, without prefix namespace collisions.
 legacy={'title':'F17 legacy original','content':'Legacy Markdown','categoryId':1001,'publishDate':'2020-01-01T00:00:00Z'}
 old=send(data=legacy,path='/api/webhooks/posts');checks['v1_and_v2_same_key_independent']=old[0]==200 and old[1]['postId']!=id and sql('SELECT count(*) FROM "WebhookReceipts" WHERE "Key"=\'f17-key\'')=='2'
 checks['legacy_response_contract_replays']=send(data=legacy,path='/api/webhooks/posts')[:2]==old[:2] and set(old[1])=={'success','postId','title','slug','isPublished','publishDate','message'}
 checks['legacy_payload_unknown_fields_unchanged']=send(data={**legacy,'id':999999},path='/api/webhooks/posts')[:2]==old[:2]
 checks['v2_payload_never_interpreted_by_legacy']=send('f17-legacy-reject',payload(),path='/api/webhooks/posts')[0]==400
 stable=snapshot()
 for name,invalid in [('content',{'content':'raw HTML'}),('identity',{'id':99999}),('derived',{'documentPlainText':'forged'}),('cover',{'coverImageUrl':'https://example.test/cover.png'}),('classification',{'contentKind':'Unclassified'}),('numeric-kind',{'contentKind':1}),('numeric-scope',{'accessScope':1}),('newsletter-public',{'contentKind':'Newsletter','accessScope':'Public'}),('missing-category',{'categoryId':999999}),('inactive-category',{'categoryId':1002}),('title-limit',{'title':'x'*201}),('summary-limit',{'summary':'x'*501}),('time-no-offset',{'publishDate':'2020-01-02T03:04:05'}),('time-invalid',{'publishDate':'invalid'})]:
  key='f17-invalid-'+name;response=send(key,{**payload(),**invalid});checks['reject_'+name]=response[0]==400 and receipt_count(key)=='0'
 for name,raw in [('duplicate',b'{"title":"a","title":"b"}'),('syntax',b'{'),('empty',b'null'),('depth',('['*18+'0'+']'*18).encode()),('missing-required',b'{}')]:
  key='f17-wire-'+name;checks['wire_'+name]=send(key,raw=raw)[0]==400 and receipt_count(key)=='0'
 for name,doc in [('html',{'version':1,'document':{'type':'doc','content':[{'type':'html','attrs':{'html':'<script>x</script>'}}]}}),('unknown-version',{'version':99,'document':{'type':'doc'}}),('dangerous-url',{'version':1,'document':{'type':'doc','content':[{'type':'image','attrs':{'src':'javascript:alert(1)'}}]}})]:
  key='f17-doc-'+name;checks['document_'+name]=send(key,payload(documentJson=json.dumps(doc)))[0]==400 and receipt_count(key)=='0'
 checks['invalid_requests_preserve_all_rows']=snapshot()==stable
 key='f17-fix-input';bad=send(key,payload(documentJson='{}'));good=send(key,payload('F17 corrected'));checks['invalid_then_valid_does_not_consume_key']=bad[0]==400 and good[0]==200
 checks['missing_key_rejected']=send(None)[0]==400
 checks['invalid_key_rejected']=send('bad key')[0]==400 and send('x'*129)[0]==400
 checks['wrong_content_type_rejected']=send('f17-type',content_type='text/plain')[0]==415
 stable=snapshot();checks['auth_before_invalid_json']=send('f17-noauth',raw=b'{',secret='wrong')[0]==401 and send('f17-noheader',secret=None)[0]==401 and snapshot()==stable
 admin,_=cookie_opener();assert submit_login(admin,base,'f17-admin','f17-isolated-password')[2]==200
 checks['admin_cookie_never_grants_ingress']=send('f17-cookie',secret=None,client=admin)[0]==401
 checks['secret_without_cookie_or_csrf_works']=first[0]==200
 checks['json_budget_413_does_not_consume_key']=send('f17-doc-budget',payload(documentJson=' '*1048577))[0]==413 and receipt_count('f17-doc-budget')=='0'
 # curl handles an early rejection while still transmitting a large body; urllib loses the response on BrokenPipe.
 import tempfile
 def large_send(chunked=False):
  with tempfile.NamedTemporaryFile() as body, tempfile.NamedTemporaryFile() as output:
   body.write(b' '*2097153);body.flush()
   cmd=['curl','--silent','--show-error','--max-time','10','--output',output.name,'--write-out','%{http_code}',
        '-H','Content-Type: application/json','-H','X-DevCore-Secret: f17-webhook','-H','Idempotency-Key: f17-large',
        '-H','Expect: 100-continue','--data-binary','@'+body.name]
   if chunked:cmd+=['-H','Transfer-Encoding: chunked','-H','Content-Length:']
   end=time.monotonic()+65
   while True:
    result=subprocess.run(cmd+[base+'/api/webhooks/v2/posts'],capture_output=True,text=True)
    status=int(result.stdout or '0')
    if status!=429 or time.monotonic()>end:break
    time.sleep(.5)
   assert result.returncode==0,(result.returncode,result.stderr,status)
   return status
 checks['wire_budget_413_does_not_consume_key']=large_send()==413 and receipt_count('f17-large')=='0'
 checks['chunked_wire_budget_413']=large_send(True)==413 and receipt_count('f17-large')=='0'
 sql('CREATE FUNCTION f17_delay() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."ContractVersion"=2 AND NEW."Key" LIKE \'f17-race%\' THEN PERFORM pg_sleep(.2); END IF; RETURN NEW; END $$; CREATE TRIGGER f17_delay BEFORE INSERT ON "WebhookReceipts" FOR EACH ROW EXECUTE FUNCTION f17_delay();')
 concurrent_payload=payload('F17 concurrent')
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:results=list(pool.map(lambda _:send('f17-race',concurrent_payload),range(4)))
 checks['parallel_same_key_one_result']=all(r[:2]==results[0][:2] and r[0]==200 for r in results)
 checks['parallel_same_key_one_post_and_receipt']=receipt_count('f17-race')=='1' and sql('SELECT count(*) FROM "Posts" WHERE "Title"=\'F17 concurrent\'')=='1'
 with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:results2=list(pool.map(lambda title:send('f17-race-conflict',payload(title)),['F17 race A','F17 race B']))
 checks['parallel_different_body_one_winner']=sorted(r[0] for r in results2)==[200,409] and receipt_count('f17-race-conflict')=='1' and sql('SELECT count(*) FROM "Posts" WHERE "Title" IN (\'F17 race A\',\'F17 race B\')')=='1'
 sql('DROP TRIGGER f17_delay ON "WebhookReceipts"; DROP FUNCTION f17_delay();')
 sql(f'UPDATE "Posts" SET "Title"=\'F17 later edit\',"IsPublished"=true WHERE "Id"={id}')
 checks['later_edits_leave_original_replay']=send(data=original)[:2]==first[:2]
 raceid=results[0][1]['postId'];sql(f'DELETE FROM "Posts" WHERE "Id"={raceid}')
 checks['deleted_post_never_recreated']=send('f17-race',concurrent_payload)[:2]==results[0][:2] and sql('SELECT "PostId" IS NULL FROM "WebhookReceipts" WHERE "ContractVersion"=2 AND "Key"=\'f17-race\'')=='t' and sql(f'SELECT count(*) FROM "Posts" WHERE "Id"={raceid}')=='0'
 sql('CREATE FUNCTION f17_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."ContractVersion"=2 AND NEW."Key"=\'f17-failure\' THEN RAISE EXCEPTION \'synthetic failure\'; END IF; RETURN NEW; END $$; CREATE TRIGGER f17_fail BEFORE INSERT ON "WebhookReceipts" FOR EACH ROW EXECUTE FUNCTION f17_fail();')
 stable=snapshot();failed=send('f17-failure',payload('F17 rollback'))
 checks['db_receipt_failure_rolls_back_both']=failed[0]==500 and snapshot()==stable and receipt_count('f17-failure')=='0'
 checks['db_error_has_no_provider_or_body_details']=not any(v in json.dumps(failed[1]) for v in ['synthetic failure','Npgsql','F17_BODY_NEEDLE','DocumentJson'])
 sql('DROP TRIGGER f17_fail ON "WebhookReceipts"; DROP FUNCTION f17_fail();')
 checks['db_error_same_key_can_retry']=send('f17-failure',payload('F17 rollback'))[0]==200
 # Constraint itself rejects unknown namespaces, independent of the application.
 rejected=subprocess.run(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-v','ON_ERROR_STOP=1','-c','UPDATE "WebhookReceipts" SET "ContractVersion"=3 WHERE "Key"=\'f17-key\';'],capture_output=True,text=True)
 checks['database_version_constraint']=rejected.returncode!=0 and 'CK_WebhookReceipts_ContractVersion' in rejected.stderr
 statuses=[send('f17-rate',secret='wrong',raw=b'{',retry=False)[0] for _ in range(21)]
 checks['bounded_rate_limit_queue_closed']=429 in statuses and all(s in [401,429] for s in statuses)
 checks['shared_secret_not_echoed']=all('f17-webhook' not in json.dumps(r[1]) for r in [first,again,failed])
report.write_text(json.dumps({'passed':all(checks.values()),'count':len(checks),'checks':checks,'fixtures':fixtures},ensure_ascii=False,indent=2)+'\n')
assert checks and all(checks.values()),[k for k,v in checks.items() if not v]
