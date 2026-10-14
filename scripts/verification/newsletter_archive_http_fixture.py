"""Private archive acceptance on the reader fixture's owned PostgreSQL and MVC process only."""
import html,json,re,subprocess,os
from pathlib import Path

def run_archive_checks(root,fixture,report_dir,request,sql,seed,login,csrf,new_client,start,private,base_env,browser_module,browser_executable):
 checks={};capture=fixture/'archive-sql.txt';canary='F31_PRIVATE_BODY'
 def check(name,value):
  checks[name]=bool(value)
  if not value:raise RuntimeError('Archive acceptance: '+name)
 def quote(value):return "'"+value.replace("'","''")+"'"
 def commands():return capture.read_text() if capture.exists() else ''
 def snapshot():return sql('SELECT to_jsonb(p)::text FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c)::text FROM "Categories" c ORDER BY "Id";')
 def get(path,client=None,method=None):return request(path,method=method,opener=client)
 original=snapshot()
 try:
  check('owned_reader_fixture',str(fixture).startswith('/tmp/devcoreblog-reader-'))
  # Dedicated archive cases get a fresh budget in this owned fixture, after the F30 budget tests.
  sql('UPDATE "ReaderLoginBudgets" SET "Count"=0')
  sql('''INSERT INTO "Categories" ("Id","Name","Slug","CreatedDate","IsActive","EditVersion") VALUES
   (981001,'F31 Deneyler','f31-deneyler','2020-01-01Z',true,1),(981002,'F31 Kapalı','f31-kapali','2020-01-01Z',false,1)''')
  body=canary+' <script>window.f31Injected=1</script> Türkçe deney. '+('Deney sonucu okuyucuya açık. '*12).strip()
  doc=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':body}]}]}},ensure_ascii=False)
  words=len(re.findall(r'\w+',body));minutes=(words+199)//200
  rows=[('allowed',True,True,981001,4,1,'2020-01-01Z'),('draft',True,False,981001,4,1,'2020-01-01Z'),('future',True,True,981001,4,1,'2999-01-01Z'),('inactive',False,True,981001,4,1,'2020-01-01Z'),('category-off',True,True,981002,4,1,'2020-01-01Z'),('public',True,True,981001,1,0,'2020-01-01Z'),('other-private',True,True,981001,1,1,'2020-01-01Z')]
  rows += [('page-'+str(i),True,True,981001,4,1,'2020-01-01Z') for i in range(1,11)]
  for i,(name,active,published,category,kind,scope,date) in enumerate(rows):
   title='F31 Başlık <script>window.f31Title=1</script>' if name=='allowed' else 'F31 '+name
   saved_body='F31_PUBLIC_BODY' if scope==0 else body
   saved_doc=json.dumps({'version':1,'document':{'type':'doc','content':[{'type':'paragraph','content':[{'type':'text','text':saved_body}]}]}},ensure_ascii=False)
   saved_words=len(re.findall(r'\w+',saved_body))
   sql(f'''INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","Excerpt","ThumbnailUrl","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","IsPublished","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes","EditVersion")
    VALUES ({982000+i},{quote(title)},{quote('f31-'+name)},'Kısa deney özeti','F31_LEGACY_FORBIDDEN','F31_EXCERPT_FORBIDDEN','',{category},'2020-01-01Z',{str(active).lower()},777,{quote(date)},{str(published).lower()},{kind},{scope},1,{quote(saved_doc)},{quote(saved_body)},{saved_words},{(saved_words+199)//200},1)''')
  baseline=snapshot();_,anon=new_client();_,admin=new_client()
  before=commands()
  for path in ('/bultenler','/bulten/f31-allowed','/bulten/missing'):
   status,h,b=get(path,anon);check('anonymous_401_private_'+path,status==401 and private(h) and canary not in b)
   status,h,b=get(path,anon,'HEAD');check('anonymous_HEAD_401_'+path,status==401 and private(h) and not b)
  anti=csrf('/fixture-admin/login',admin)
  check('admin_login',request('/fixture-admin/login',{'username':'fixture-admin','password':'synthetic-reader-password','__RequestVerificationToken':anti},opener=admin)[0]==302)
  check('admin_only_has_no_archive_access',all(get(p,admin)[0]==401 for p in ('/bultenler','/bulten/f31-allowed')))
  check('denied_requests_execute_no_archive_SQL',commands()==before)
  active=seed();check('reader_can_return_to_archive',login(active['token'],return_url='/bultenler')[0]==302)
  sql('UPDATE "Posts" SET "IsPublished"=false WHERE "Id" BETWEEN 982000 AND 982016')
  status,h,b=get('/bultenler');check('empty_archive_honest_200',status==200 and private(h) and 'Henüz yayımlanmış bülten yok.' in html.unescape(b))
  sql('UPDATE "Posts" SET "IsPublished"=("Slug"<>\'f31-draft\') WHERE "Id" BETWEEN 982000 AND 982016')
  status,h,b=get('/bultenler');check('reader_list_200_private',status==200 and private(h))
  check('bounded_first_page_and_no_body',b.count('class="archive-card"')==9 and canary not in b and 'DocumentJson' not in b)
  check('list_title_encoded','<script>window.f31Title=1</script>' not in b and 'window.f31Title=1' in b)
  first=re.findall(r'href="(/bulten/f31-[^"]+)"',b)
  status,_,b2=get('/bultenler?page=2');second=re.findall(r'href="(/bulten/f31-[^"]+)"',b2)
  check('stable_disjoint_paging',status==200 and len(second)==2 and not set(first)&set(second))
  check('list_SQL_excludes_body_and_pages_in_database',all(s not in '\n'.join(x for x in commands().split('--F31--') if 'F31 archive list' in x) for s in ('"DocumentJson"','"Content"','"DocumentPlainText"','"Excerpt"')) and all(s in commands() for s in ('F31 archive list','LIMIT','OFFSET','ORDER BY')))
  for value in ('0','1001','bad'):
   status,h,_=get('/bultenler?page='+value);check('bad_page_'+value,status==400 and private(h))
  check('empty_later_page_404',get('/bultenler?page=3')[0]==404)
  status,h,b=get('/bulten/f31-allowed');check('validated_document_200',status==200 and canary in b and private(h))
  check('body_safe_encoded_no_legacy_fallback','<script>window.f31Injected=1</script>' not in b and 'window.f31Injected=1' in b and 'F31_LEGACY_FORBIDDEN' not in b and 'F31_EXCERPT_FORBIDDEN' not in b)
  check('private_no_public_metadata','BlogPosting' not in b and 'og:' not in b and 'rel="canonical"' not in b and '<title>Bülten sayısı' in html.unescape(b))
  check('turkish_site_date','01 Oca 2020' in html.unescape(b) and 'dk okuma' in b)
  for path in ('/bultenler','/bulten/f31-allowed'):
   status,h,b=get(path,method='HEAD');check('reader_HEAD_'+path,status==200 and not b and private(h))
  for name in ('draft','future','inactive','category-off','public','other-private','missing'):
   status,h,b=get('/bulten/f31-'+name);check('excluded_'+name,status==404)
   check('excluded_'+name+'_private_no_body',private(h) and canary not in b)
  check('oversized_slug_400',get('/bulten/'+'a'*201)[0]==400)
  status,h,b=get('/bulten/f31-forced-fault');check('unexpected_500_retains_private_headers',status==500 and private(h) and 'F31_FORCED_EXCEPTION_SECRET' not in b and canary not in b)
  check('GET_HEAD_do_not_update_posts_or_counters',snapshot()==baseline)
  check('private_success_not_shared_with_anonymous',get('/bulten/f31-allowed',anon)[0]==401 and get('/bultenler',anon)[0]==401)
  for path in ('/post/f31-allowed','/yazi/f31-allowed','/ara?query=F31','/sitemap.xml','/api/public/posts/latest','/rss.xml','/bulten'):
   status,h,b=get(path,anon)
   check('no_private_body_in_public_'+path,canary not in b and 'window.f31Injected=1' not in b)
  # Missing proof facts and canonical corruption are real DB faults, never a raw HTML fallback.
  sql('UPDATE "Posts" SET "DocumentWordCount"="DocumentWordCount"+1 WHERE "Slug"=\'f31-allowed\'')
  check('corrupt_derived_facts_fail_closed_503',get('/bulten/f31-allowed')[0]==503)
  sql('UPDATE "Posts" SET "DocumentWordCount"="DocumentWordCount"-1, "DocumentJson"=\'{\' WHERE "Slug"=\'f31-allowed\'')
  check('corrupt_JSON_fail_closed_503',get('/bulten/f31-allowed')[0]==503)
  sql('UPDATE "Posts" SET "DocumentJson"='+quote(doc)+' WHERE "Slug"=\'f31-allowed\'')
  sql('ALTER TABLE "Posts" RENAME TO "F31OwnedPostsUnavailable"')
  try:
   status,h,b=get('/bultenler');check('actual_storage_fault_503_private',status==503 and private(h) and canary not in b and 'Npgsql' not in b)
  finally:sql('ALTER TABLE "F31OwnedPostsUnavailable" RENAME TO "Posts"')
  check('storage_recovery_reads_archive',get('/bulten/f31-allowed')[0]==200)
  start();check('restart_preserves_access',get('/bulten/f31-allowed')[0]==200)
  sql('UPDATE "Posts" SET "PublishDate"=\'2020-01-01Z\' WHERE "Slug"=\'f31-future\'')
  check('scheduled_boundary_visible_next_request',get('/bulten/f31-future')[0]==200)
  for name,update in [('suppressed','"Suppression"=1'),('pending','"Status"=0,"VerifiedUtc"=NULL'),('period_changed','"PeriodId"=gen_random_uuid()'),('version_changed','"Version"="Version"+1')]:
   subject=seed();login(subject['token']);sql('UPDATE "Subscribers" SET '+update+' WHERE "Id"='+quote(subject['subscriberId']))
   check('current_'+name+'_denied',get('/bultenler')[0]==401 and get('/bulten/f31-allowed')[0]==401)
  expired=seed();login(expired['token']);sql('UPDATE "ReaderSessions" SET "CreatedUtc"="CreatedUtc"-interval \'8 days\',"ExpiresUtc"="ExpiresUtc"-interval \'8 days\' WHERE "SubscriberId"='+quote(expired['subscriberId']))
  check('expired_session_denied',get('/bulten/f31-allowed')[0]==401)
  subject=seed();login(subject['token']);check('CSRF_logout_cannot_revoke',request('/abonelik/oturumu-kapat',{})[0]==400 and get('/bulten/f31-allowed')[0]==200)
  check('logout_closes_archive',request('/abonelik/oturumu-kapat',{'__RequestVerificationToken':csrf('/abonelik/oturum')})[0]==302 and get('/bultenler')[0]==401)
  subject=seed();login(subject['token']);anti=csrf('/abonelik/cik')
  check('unsubscribe_closes_archive',request('/abonelik/cik',{'Token':subject['exitToken'],'__RequestVerificationToken':anti})[0]==200 and get('/bulten/f31-allowed')[0]==401)
  # Prove the archived content is still unavailable to this old cookie after reactivation.
  sql('UPDATE "Subscribers" SET "Status"=1,"VerifiedUtc"="UpdatedUtc" WHERE "Id"='+quote(subject['subscriberId']))
  check('reactivation_does_not_revive_session',get('/bulten/f31-allowed')[0]==401)
  if browser_module:
   subject=seed();proof_file=fixture/'proof.json'
   proc=subprocess.run(['node',str(root/'scripts/verification/newsletter_archive_browser_probe.cjs')],cwd=root,env={**base_env,'DEVCORE_PLAYWRIGHT_MODULE':str(browser_module),'DEVCORE_BROWSER_EXECUTABLE':str(browser_executable or ''),'ARCHIVE_BASE':'http://127.0.0.1:15226','ARCHIVE_PROOF_FILE':str(proof_file),'ARCHIVE_REPORT_DIR':str(report_dir)},capture_output=True,text=True,timeout=90)
   result=json.loads((report_dir/'archive-browser.json').read_text());check('real_chrome_archive',proc.returncode==0 and result['passed'])
  check('read_only_post_history_unchanged',sql('SELECT bool_and("ViewCount"=777 AND "EditVersion"=1) FROM "Posts" WHERE "Id" BETWEEN 982000 AND 982016')=='t')
 finally:
  sql('DELETE FROM "Posts" WHERE "Id" BETWEEN 982000 AND 982016; DELETE FROM "Categories" WHERE "Id" IN (981001,981002)')
  check('owned_posts_restored',snapshot()==original)
  if capture.exists():(report_dir/'archive-sql.txt').write_text(capture.read_text())
  (report_dir/'archive-http.json').write_text(json.dumps({'passed':bool(checks) and all(checks.values()),'count':len(checks),'checks':checks,'scope':'Real owned PostgreSQL/MVC access matrix, publication, canonical renderer, SQL projection, no public leakage; no real email.'},indent=2)+'\n')
