// Real Chrome/MVC acceptance; only owned synthetic fixture data is written.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR,port=process.env.DEVCORE_DOCUMENT_PG_PORT;
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(port,'55485');fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],violations=[];let browser,context,beforeUnload=0;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p',port,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'}).trim();
const snapshot=()=>sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";');
const editor=p=>p.getByRole('textbox',{name:'Yazı metni',exact:true});
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__newsletterCsp',(_source,directive)=>violations.push(directive));
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__newsletterCsp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('dialog',d=>{if(d.type()==='beforeunload')beforeUnload++;return d.accept();});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 // Existing remote media references are rendered with deterministic bytes; no upload/delivery assertion.
 await context.route('https://example.test/**',r=>r.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lSUAAAAASUVORK5CYII=','base64')}));
 await context.route('https://i.ytimg.com/**',r=>r.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lSUAAAAASUVORK5CYII=','base64')}));
 await context.route('https://www.youtube-nocookie.com/**',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><title>Reference only</title>'}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('f17-admin');await p.locator('#password').fill('f17-isolated-password');await p.getByRole('button',{name:'Giriş yap',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');
 await p.getByRole('link',{name:'Bültenler',exact:true}).click();await p.waitForURL('**/AdminNewsletter');
 check('dedicated_inventory_navigation',new URL(p.url()).pathname==='/AdminNewsletter' && await p.getByRole('heading',{name:'Bültenler',exact:true}).isVisible());
 await p.getByRole('link',{name:'Yeni bülten hazırla',exact:true}).click();await editor(p).waitFor();
 check('shared_editor_and_explicit_newsletter_scope',await p.locator('[data-recovery-scope=newsletter]').count()===1&&await p.getByRole('button',{name:'Site yayınını planla',exact:true}).isEnabled());
 await p.locator('[name=Title]').fill('F33 Chrome bülten konu');await p.locator('[name=Summary]').fill('Özet 👋');await p.locator('[name=CategoryId]').selectOption('1001');
 await editor(p).fill('Bülten taslağı Türkçe 👋');
 await p.waitForFunction(()=>!!localStorage.getItem('devcore_document_draft_v1_newsletter_create'));
 const newsletterRaw=await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_newsletter_create'));
 await p.goto(base+'/AdminWriting/Create');await editor(p).waitFor();
 check('article_create_does_not_offer_newsletter_copy',await p.locator('[data-recovery-panel]').isHidden());
 await editor(p).fill('Bağımsız makale taslağı');await p.waitForFunction(()=>!!localStorage.getItem('devcore_document_draft_v1_create'));
 const articleRaw=await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_create'));
 await p.goto(base+'/AdminNewsletter/Create');await editor(p).waitFor();
 check('newsletter_copy_survives_navigation_separately',await p.locator('[data-recovery-panel]').isVisible() && await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_newsletter_create'))===newsletterRaw);
 await p.getByRole('button',{name:'Taslağı geri yükle',exact:true}).click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));
 check('recovery_restores_newsletter_body_subject_summary',await p.locator('[name=Title]').inputValue()==='F33 Chrome bülten konu' && await p.locator('[name=Summary]').inputValue()==='Özet 👋' && (await editor(p).innerText()).includes('Bülten taslağı'));
 const before=snapshot();
 for(const [buttonName,endpoint] of [['Web önizlemesi (yeni sekme)','/AdminDocument/Preview'],['E-posta önizlemesi (yeni sekme)','/AdminEmail/Preview']]){
  const raw=await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_newsletter_create'));
  const next=context.waitForEvent('page');await p.getByRole('button',{name:buttonName,exact:true}).click();const tab=await next;await tab.waitForLoadState('domcontentloaded');
  check('native_private_preview_'+endpoint,tab.url()===base+endpoint && await tab.evaluate(()=>window.opener===null) && (await tab.locator('main').innerText()).includes('Bülten taslağı') && snapshot()===before && await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_newsletter_create'))===raw);
  await tab.close();
 }
 await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Taslağı kaydet',exact:true}).click()]);await editor(p).waitFor();
 const id=Number(await p.locator('[name=Id]').inputValue());assert.ok(id>0);
 check('commit_clears_only_newsletter_create_revision',await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_newsletter_create'))===null && await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_create'))===articleRaw);
 check('saved_newsletter_reopens_with_draft_state_and_no_send',new URL(p.url()).pathname==='/AdminNewsletter/Edit/'+id && (await p.locator('[data-site-publication-state]').innerText()).includes('Taslak') && sql(`SELECT "ContentKind"=4 AND "AccessScope"=1 AND NOT "IsPublished" FROM "Posts" WHERE "Id"=${id}`)==='t');
 const future=new Date(Date.now()+86400000*2);const local=new Date(future.getTime()+3*3600000).toISOString().slice(0,19);
 await p.locator('[name=PublishDate]').fill(local.replace(/:00$/,''));await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Site yayınını planla',exact:true}).click()]);await editor(p).waitFor();
 check('browser_schedule_displays_common_state',(await p.locator('[data-site-publication-state]').innerText()).includes('Planlandı'));
 const savedDate=sql(`SELECT "PublishDate" FROM "Posts" WHERE "Id"=${id}`);
 await p.locator('[name=Title]').fill('F33 normal kayıt konu');await p.locator('[name=PublishDate]').fill('2020-01-01T00:00');await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Değişiklikleri kaydet',exact:true}).click()]);await editor(p).waitFor();
 check('browser_save_preserves_scheduled_instant',sql(`SELECT "PublishDate" FROM "Posts" WHERE "Id"=${id}`)===savedDate && (await p.locator('[data-site-publication-state]').innerText()).includes('Planlandı'));
 await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Şimdi sitede yayımla',exact:true}).click()]);await editor(p).waitFor();
 check('browser_publish_remains_private_and_separate_from_send',(await p.locator('[data-site-publication-state]').innerText()).includes('Yayımlandı') && (await p.locator('main.writing-page').innerText()).includes('Kaydetmek veya sitede yayımlamak e-posta göndermez.') && sql(`SELECT "IsPublished" AND "AccessScope"=1 FROM "Posts" WHERE "Id"=${id}`)==='t');
 // Two browser forms compete: conflict page must retain the user's envelope and old version.
 const stale=await context.newPage();await stale.goto(p.url());await editor(stale).waitFor();const version=await stale.locator('[name=EditVersion]').inputValue();
 await p.locator('[name=Title]').fill('F33 kazanan');await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Değişiklikleri kaydet',exact:true}).click()]);await editor(p).waitFor();
 await stale.locator('[name=Title]').fill('F33 kaybolmayan konu');await editor(stale).fill('Çakışmada kaybolmayan metin 👋');
 const response=stale.waitForResponse(r=>r.url().includes('/AdminNewsletter/Edit/')&&r.request().method()==='POST');await stale.getByRole('button',{name:'Değişiklikleri kaydet',exact:true}).click();const conflict=await response;await editor(stale).waitFor();
 check('browser_conflict_retains_subject_document_old_version',conflict.status()===409 && await stale.locator('[name=Title]').inputValue()==='F33 kaybolmayan konu' && (await editor(stale).innerText()).includes('kaybolmayan metin') && await stale.locator('[name=EditVersion]').inputValue()===version);
 await stale.close();
 for(const width of [1440,390]){
  await p.setViewportSize({width,height:1000});await p.getByRole('button',{name:'Taslağı kaydet',exact:true}).focus();
  check('form_geometry_and_keyboard_focus_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth) && await p.getByRole('button',{name:'Taslağı kaydet',exact:true}).evaluate(el=>document.activeElement===el&&getComputedStyle(el).outlineStyle!=='none'));
  await p.screenshot({path:path.join(report,'newsletter-editor-'+width+'.png'),fullPage:true});
 }
 // Exercise the same composite document through both scoped newsletter previews.
 const composite={version:1,document:{type:'doc',content:[
  {type:'heading',attrs:{level:2},content:[{type:'text',text:'Türkçe bölüm 👋'}]},
  {type:'paragraph',content:[{type:'text',text:'Bülten taslağı zengin içerik'}]},
  {type:'codeBlock',attrs:{language:'csharp'},content:[{type:'text',text:'var örnek = "<script>literal</script>";'}]},
  {type:'table',content:[{type:'tableRow',content:[{type:'tableHeader',content:[{type:'paragraph',content:[{type:'text',text:'Başlık'}]}]},{type:'tableCell',content:[{type:'paragraph',content:[{type:'text',text:'Veri'}]}]}]}]},
  {type:'image',attrs:{src:'https://example.test/newsletter.gif',alt:'Bülten GIF'}},
  {type:'youtube',attrs:{src:'https://youtu.be/jNQXAC9IVRw'}}]}};
 const saved=await context.request.post(base+'/AdminNewsletter/Edit/'+id,{form:{Id:String(id),EditVersion:await p.locator('[name=EditVersion]').inputValue(),Title:'F33 zengin bülten',Summary:'Özet',Excerpt:'',ThumbnailAlt:'',CategoryId:'1001',IsActive:'true',SaveAction:'Save',PublishDate:await p.locator('[name=PublishDate]').inputValue(),DocumentJson:JSON.stringify(composite),__RequestVerificationToken:await p.locator('#postForm [name=__RequestVerificationToken]').inputValue()}});
 check('composite_newsletter_canonical_save',saved.status()===200);await p.reload();await editor(p).waitFor();
 for(const [buttonName,email] of [['Web önizlemesi (yeni sekme)',false],['E-posta önizlemesi (yeni sekme)',true]]){
  const before=snapshot(),next=context.waitForEvent('page');await p.getByRole('button',{name:buttonName,exact:true}).click();const tab=await next;await tab.waitForLoadState('domcontentloaded');
  check('composite_preview_'+(email?'email':'web'),await tab.locator('main table').count()===1 && await tab.locator('main pre code').count()===1 && await tab.locator('main img[src="https://example.test/newsletter.gif"]').count()===1 && await tab.locator('main iframe').count()===(email?0:1) && await tab.locator('main script').count()===0 && snapshot()===before);
  await tab.close();
 }
 // Long unbroken summary/title must fit the private inventory on narrow screens.
 sql(`UPDATE "Posts" SET "Summary"=repeat('x',500),"Title"=repeat('K',200) WHERE "Id"=${id}`);
 await p.goto(base+'/AdminNewsletter');
 for(const width of [1440,390]){await p.setViewportSize({width,height:1000});check('inventory_geometry_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth && document.querySelector('main.writing-page').scrollWidth<=document.querySelector('main.writing-page').clientWidth));await p.screenshot({path:path.join(report,'newsletter-list-'+width+'.png'),fullPage:true});}
 check('no_csp_or_runtime_errors',errors.length===0 && violations.length===0);check('existing_dirty_guard_still_runs',beforeUnload>0);
 const result={passed:true,count:Object.keys(checks).length,checks,errors,violations,fixturePostId:id,browserVersion:browser.version(),referenceMedia:'Deterministic remote image/frame bytes; no real provider upload/delivery claim'};
 fs.writeFileSync(path.join(report,'newsletter-browser.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({passed:true,count:result.count}));
})().catch(error=>{fs.writeFileSync(path.join(report,'newsletter-browser-failure.json'),JSON.stringify({passed:false,checks,errors,violations,error:error.message},null,2));console.error(error.message);process.exitCode=1;}).finally(async()=>{if(context)await context.close();if(browser)await browser.close();});
