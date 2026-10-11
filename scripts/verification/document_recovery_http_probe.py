#!/usr/bin/env python3
"""Recovery admission and commit receipts against real MVC and owned disposable PostgreSQL."""
import argparse, json, os, re, subprocess, uuid
from pathlib import Path
from urllib.parse import urlparse
from http_probe_support import cookie_opener, submit_login, request_with_headers, extract_antiforgery_token
from f22_edit_conflict_probe import field
p=argparse.ArgumentParser()
p.add_argument('--base-url',required=True);p.add_argument('--database-port',required=True);p.add_argument('--report',required=True)
a=p.parse_args();base=a.base_url.rstrip('/');assert urlparse(base).hostname=='127.0.0.1'
checks={}
def sql(query):
 return subprocess.check_output(['psql','-X','-h','127.0.0.1','-p',a.database_port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',query],text=True)
def snapshot(): return sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";')
admin,_=cookie_opener();assert submit_login(admin,base,os.environ['DEVCORE_TEST_ADMIN_USERNAME'],os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])[2]==200
form=request_with_headers(admin,base+'/AdminWriting/Create');token=extract_antiforgery_token(form[2])
doc=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':'Kurtarılan Türkçe 👋'}]}]}},ensure_ascii=False)
common={'Title':'F14 kayıt makbuzu','CategoryId':'1001','IsActive':'true','SaveAction':'SaveDraft','PublishDate':field(form[2],'PublishDate'),'DocumentJson':doc,'__RequestVerificationToken':token}
url=base+'/AdminDocument/ValidateRecovery';before=snapshot()
cases={'valid':(doc,200),'unknown_version':(doc.replace('"version": 1','"version": 2'),400),'unknown_node':(doc.replace('"paragraph"','"futureNode"'),400),'malformed':('{',400),'raw_html':(json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'html','text':'<script>x</script>'}]}}),400),'dangerous_url':(json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'image','attrs':{'src':'javascript:alert(1)'}}]}}),400),'byte_limit':('x'*1048577,413)}
for name,(payload,status) in cases.items():
 response=request_with_headers(admin,url,data={'Input.DocumentJson':payload,'__RequestVerificationToken':token})
 checks[name+'_admission_without_write']=response[0]==status and snapshot()==before
 checks[name+'_private_response']='no-store' in response[3].get('Cache-Control','')
checks['csrf_required']=request_with_headers(admin,url,data={'Input.DocumentJson':doc})[0]==400 and snapshot()==before
anon,_=cookie_opener();anonymous_form=request_with_headers(anon,base+'/fixture-admin/login');anonymous=request_with_headers(anon,url,data={'Input.DocumentJson':doc,'__RequestVerificationToken':extract_antiforgery_token(anonymous_form[2])});checks['authentication_required']=anonymous[0]==404 and snapshot()==before and 'valid' not in anonymous[2]
get_response=request_with_headers(admin,url);checks['get_cannot_admit']=get_response[0]==404 and snapshot()==before
revision=str(uuid.uuid4()); saved=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'RecoveryRevision':revision});post_id=int(field(saved[2],'Id'))
checks['commit_receipt_matches_source_and_revision']=saved[0]==200 and 'data-document-receipt' in saved[2] and 'data-source-id="0"' in saved[2] and f'data-revision="{revision}"' in saved[2]
checks['receipt_consumed_once']='data-document-receipt' not in request_with_headers(admin,base+f'/AdminWriting/Edit/{post_id}')[2]
checks['query_cannot_forge_receipt']='data-document-receipt' not in request_with_headers(admin,base+f'/AdminWriting/Edit/{post_id}?RecoveryReceipt.Revision={revision}&RecoveryReceipt.SourcePostId=0')[2]
for name,changes in [('invalid_metadata',{'Title':''}),('invalid_revision',{'RecoveryRevision':'arbitrary'})]:
 original=snapshot();response=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'RecoveryRevision':revision,**changes})
 checks[name+'_400_has_no_receipt_or_write']=response[0]==400 and 'data-document-receipt' not in response[2] and snapshot()==original
edit=request_with_headers(admin,base+f'/AdminWriting/Edit/{post_id}');edit_common={**common,'Id':str(post_id),'EditVersion':field(edit[2],'EditVersion'),'SaveAction':'Save','RecoveryRevision':revision}
updated=request_with_headers(admin,base+f'/AdminWriting/Edit/{post_id}',data=edit_common)
checks['edit_commit_owns_edit_key']=updated[0]==200 and f'data-source-id="{post_id}"' in updated[2]
original=snapshot();conflict=request_with_headers(admin,base+f'/AdminWriting/Edit/{post_id}',data=edit_common)
checks['409_has_no_receipt_and_retains_input']=conflict[0]==409 and 'data-document-receipt' not in conflict[2] and revision==field(conflict[2],'RecoveryRevision') and snapshot()==original
try:
 sql('CREATE FUNCTION f14_reject_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION \'synthetic write rejection\'; END $$; CREATE TRIGGER f14_reject BEFORE INSERT OR UPDATE ON "Posts" FOR EACH ROW EXECUTE FUNCTION f14_reject_write();')
 original=snapshot();failure=request_with_headers(admin,base+'/AdminWriting/Create',data={**common,'RecoveryRevision':revision})
 checks['503_has_no_receipt_and_retains_input']=failure[0]==503 and 'data-document-receipt' not in failure[2] and revision==field(failure[2],'RecoveryRevision') and snapshot()==original
finally: sql('DROP TRIGGER IF EXISTS f14_reject ON "Posts"; DROP FUNCTION IF EXISTS f14_reject_write();')
Path(a.report).write_text(json.dumps({'count':len(checks),'checks':checks,'fixturePostId':post_id,'anonymousStatus':anonymous[0],'getStatus':get_response[0]},ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'count':len(checks),'failed':[k for k,v in checks.items() if not v]}));assert all(checks.values())
