// Actual Chrome, native MVC submissions, persistent profile and two tabs. Network/storage faults are labeled.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL, report=process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname,'127.0.0.1');fs.mkdirSync(report,{recursive:true});
assert.ok(!fs.existsSync(path.join(report,'recovery-browser.json')));
const checks={},errors=[],consoleErrors=[],resourceErrors=[],violations=[],controlled=[];let context;
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'devcore-json-recovery-'));
const key='devcore_document_draft_v1_create',legacy='devcore_editor_draft_create';
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const textbox=p=>p.getByRole('textbox',{name:'Yazı metni',exact:true});
const title=p=>p.locator('#post-title-input');
const raw=p=>p.evaluate(k=>localStorage.getItem(k),key);
const doc=p=>p.locator('[name=DocumentJson]').inputValue();
async function attach(p){p.on('console',m=>{if(m.type()==='error'){if(m.text().startsWith('Failed to load resource:'))resourceErrors.push(m.text());else consoleErrors.push(m.text());}});p.on('pageerror',e=>errors.push(e.name+':'+e.message));p.on('dialog',d=>d.type()==='beforeunload'?d.accept():d.dismiss());}
async function launch(){
 context=await chromium.launchPersistentContext(profile,{headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE,viewport:{width:1440,height:1000}});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 // No provider playback claim: test only recovery of the admitted image/video references.
 await context.route('https://example.test/**',r=>r.fulfill({status:200,contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lSUAAAAASUVORK5CYII=','base64')}));
 await context.route('https://www.youtube-nocookie.com/**',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><title>Reference fixture</title>'}));
 await context.exposeBinding('__reportRecoveryCsp',(_source,directive)=>violations.push(directive));
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>{window.__recoveryCsp=[...(window.__recoveryCsp||[]),e.effectiveDirective];void window.__reportRecoveryCsp(e.effectiveDirective);}));
 return context.pages()[0]||await context.newPage();
}
async function create(p){await p.goto(base+'/AdminWriting/Create');await textbox(p).waitFor();}
async function waitSaved(p){await p.waitForFunction(k=>{try{return JSON.parse(localStorage.getItem(k))?.fields.Title===document.querySelector('[name=Title]').value;}catch{return false;}},key);}
async function submit(p,expected=302){const pending=p.waitForResponse(r=>r.url().includes('/AdminWriting/')&&r.request().method()==='POST');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:/^(Taslağı kaydet|Değişiklikleri kaydet)$/}).first().click()]);const response=await pending;check('native_status_'+expected+'_'+Object.keys(checks).length,response.status()===expected);await p.waitForLoadState();await textbox(p).waitFor();}
async function clearCreate(p){await p.evaluate(k=>localStorage.removeItem(k),key);}
(async()=>{
 let p=await launch();await attach(p);
 await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('f17-admin');await p.locator('#password').fill('f17-isolated-password');await p.getByRole('button',{name:'Sign In',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');
 await create(p);await p.evaluate(k=>localStorage.setItem(k,'F14 legacy sentinel'),legacy);
 await title(p).fill('Kurtarılan Türkçe 👋');await p.locator('[name=CategoryId]').selectOption('1001');await p.locator('[name=Summary]').fill('Korunan özet');await p.locator('[name=Excerpt]').fill('Korunan kısa açıklama');await p.locator('[name=ThumbnailAlt]').fill('Korunan kapak');
 await textbox(p).fill('Kaydedilmemiş Türkçe metin 👋');await waitSaved(p);const original=await raw(p),record=JSON.parse(original);
 check('local_save_has_no_secret_or_transient_fields',Object.keys(record.fields).sort().join(',')==='CategoryId,DocumentJson,EditVersion,Excerpt,IsActive,PublishDate,Summary,ThumbnailAlt,Title' && record.postId===0 && record.version===1);
 await p.reload();await textbox(p).waitFor();check('reload_offers_without_overwriting_server_form',await p.locator('[data-recovery-panel]').isVisible() && await title(p).inputValue()==='' && !(await textbox(p).innerText()).includes('Kaydedilmemiş'));
 await p.locator('[data-recovery-restore]').click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));
 check('server_admitted_restore_preserves_metadata_and_json',await title(p).inputValue()===record.fields.Title && await doc(p)===record.fields.DocumentJson && await p.locator('[name=Summary]').inputValue()==='Korunan özet' && await raw(p)===original);
 check('restore_returns_keyboard_focus_to_writing',await textbox(p).evaluate(el=>el===document.activeElement));
 // Real browser restart preserves origin storage while session cookie is reauthenticated if needed.
 await context.close();p=await launch();await attach(p);await p.goto(base+'/fixture-admin/login');
 await p.locator('#username').fill('f17-admin');await p.locator('#password').fill('f17-isolated-password');await p.getByRole('button',{name:'Sign In',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');await create(p);
 await textbox(p).waitFor();check('browser_restart_preserves_draft_offer',await raw(p)===original && await p.locator('[data-recovery-panel]').isVisible());
 for(const width of [1440,390]){
  await p.setViewportSize({width,height:1000});await p.locator('[data-recovery-restore]').focus();
  check('recovery_controls_and_focus_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)&&await p.locator('[data-recovery-restore]').evaluate(el=>document.activeElement===el&&getComputedStyle(el).outlineStyle!=='none'));
  check('turkish_date_and_site_zone_'+width,/\d{2} [^ ]+ \d{4} \d{2}:\d{2} \(Europe\/Istanbul\)/.test(await p.locator('[data-recovery-date]').innerText()));
  await p.screenshot({path:path.join(report,'recovery-'+width+'.png'),fullPage:true});
 }
 await p.locator('[data-recovery-restore]').click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));await submit(p);
 check('actual_create_commit_clears_only_create_revision',await raw(p)===null && (await p.locator('[data-document-receipt]').innerText()).includes('temizlendi'));
 check('legacy_markdown_key_untouched',await p.evaluate(k=>localStorage.getItem(k),legacy)==='F14 legacy sentinel');
 const editUrl=p.url(),id=await p.locator('[name=Id]').inputValue(),editKey='devcore_document_draft_v1_edit_'+id;
 await title(p).fill('Edit yerel taslak');await p.waitForFunction(k=>!!localStorage.getItem(k),editKey);await p.reload();await textbox(p).waitFor();
 check('edit_identity_offer_uses_separate_key',await p.locator('[data-recovery-panel]').isVisible()&&await raw(p)===null&&await p.evaluate(k=>JSON.parse(localStorage.getItem(k)).postId,editKey)===Number(id));
 await p.locator('[data-recovery-restore]').click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));await submit(p);
 check('edit_commit_clears_edit_revision',await p.evaluate(k=>localStorage.getItem(k),editKey)===null);
 // Two real same-origin tabs: storage events and native Web Locks coordinate writes.
 await create(p);const q=await context.newPage();await attach(q);await create(q);
 await title(p).fill('Sekme A');await waitSaved(p);const tabA=await raw(p);
 await q.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('çakışması'));
 await title(q).fill('Sekme B korunuyor');await q.waitForTimeout(450);
 check('second_tab_pauses_and_preserves_both_texts',await raw(q)===tabA && await title(q).inputValue()==='Sekme B korunuyor' && await title(p).inputValue()==='Sekme A');
 await q.locator('[data-recovery-restore]').click();await q.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));
 await title(q).fill('Sekme B daha yeni');await waitSaved(q);const tabB=await raw(q);
 check('explicit_restore_allows_new_revision',JSON.parse(tabB).revision!==JSON.parse(tabA).revision);
 await q.close();await clearCreate(p);await p.reload();await textbox(p).waitFor();
 // Pause genuine save response, write a newer record from another tab, then resume redirect/receipt.
 await title(p).fill('Makbuz yarış yazısı');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('Makbuz gövdesi');await waitSaved(p);
 let resume,arrived;const held=new Promise(r=>arrived=r),release=new Promise(r=>resume=r);
 await p.route('**/AdminWriting/Create',async route=>{if(route.request().method()!=='POST')return route.continue();const response=await route.fetch({maxRedirects:0});arrived();await release;await route.fulfill({response});});
 const saving=submit(p);await held;const writer=await context.newPage();await attach(writer);await writer.goto(base+'/Admin/Dashboard');
 const newerRaw=await writer.evaluate(k=>{const v=JSON.parse(localStorage.getItem(k));v.revision=crypto.randomUUID();v.fields.Title='Yeni sekme taslağı';v.savedAt=new Date().toISOString();const encoded=JSON.stringify(v);localStorage.setItem(k,encoded);return encoded;},key);
 resume();await saving;await p.unroute('**/AdminWriting/Create');
 check('server_commit_receipt_never_deletes_newer_tab',await raw(p)===newerRaw && (await p.locator('[data-document-receipt]').innerText()).includes('korundu'));await writer.close();
 await clearCreate(p);await create(p);await title(p).fill('400 taslağı');await textbox(p).fill('400 metni');await waitSaved(p);
 // Bypass native category validation only to exercise genuine server 400; no response stubbing.
 await p.locator('[name=CategoryId]').evaluate(el=>{el.append(new Option('Synthetic invalid','999999'));el.value='999999';el.dispatchEvent(new Event('change',{bubbles:true}));});await submit(p,400);
 check('real_400_keeps_draft_and_no_receipt',!!await raw(p) && await title(p).inputValue()==='400 taslağı' && await p.locator('[data-document-receipt]').count()===0);
 // A real validator text-limit response must keep both the encoded form and local copy.
 await clearCreate(p);await create(p);await title(p).fill('413 taslağı');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('x'.repeat(200001));await waitSaved(p);const limitedRaw=await raw(p),limitedJson=await doc(p);
 const limitedResponse=p.waitForResponse(r=>r.url().includes('/AdminWriting/Create')&&r.request().method()==='POST');
 await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:'Taslağı kaydet',exact:true}).click()]);
 check('real413_keeps_encoded_document_and_local_revision',(await limitedResponse).status()===413 && await raw(p)===limitedRaw && await doc(p)===limitedJson && await p.locator('[data-document-fallback]').isVisible() && await p.locator('[data-document-receipt]').count()===0);
 // Actual PostgreSQL rejects only this synthetic title; finally removes the trigger.
 const pgPort=process.env.DEVCORE_DOCUMENT_PG_PORT;assert.equal(pgPort,'55484');
 const sql=query=>require('node:child_process').execFileSync('psql',['-X','-h','127.0.0.1','-p',pgPort,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',query],{encoding:'utf8'});
 await clearCreate(p);await create(p);await title(p).fill('F14 browser 503');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('503 korunacak metin');await waitSaved(p);
 try {
  sql(`CREATE FUNCTION f14_browser_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW."Title" = 'F14 browser 503' THEN RAISE EXCEPTION 'synthetic rejection'; END IF; RETURN NEW; END $$; CREATE TRIGGER f14_browser_reject BEFORE INSERT ON "Posts" FOR EACH ROW EXECUTE FUNCTION f14_browser_reject();`);
  await submit(p,503);check('real503_keeps_local_and_form_without_receipt',!!await raw(p) && await title(p).inputValue()==='F14 browser 503' && await p.locator('[data-document-receipt]').count()===0);
 } finally {sql('DROP TRIGGER IF EXISTS f14_browser_reject ON "Posts"; DROP FUNCTION IF EXISTS f14_browser_reject();');}
 await submit(p);check('503_retry_commit_clears_matching_copy',await raw(p)===null);
 await clearCreate(p);await create(p);await title(p).fill('Ağ taslağı');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('Ağ metni');await waitSaved(p);const networkRaw=await raw(p);
 await p.route('**/AdminWriting/Create',r=>r.request().method()==='POST'?r.abort('internetdisconnected'):r.continue());
 await p.getByRole('button',{name:'Taslağı kaydet',exact:true}).click();await p.waitForTimeout(600);await p.unroute('**/AdminWriting/Create');
 // Chromium error navigation may lose the DOM, but origin storage still retains the pre-submit copy.
 await create(p);check('controlled_submit_network_failure_keeps_copy',await raw(p)===networkRaw && await p.locator('[data-recovery-panel]').isVisible());controlled.push('Aborted only the native save request to simulate network failure.');
 // Unknown records are never silently erased or overwritten. Old Markdown sentinel stays separate.
 for(const [name,unknown] of Object.entries({future:JSON.stringify({...record,version:2}),foreign:JSON.stringify({...record,postId:12345}),malformed:'{',legacy:'{"version":1,"fields":{"Content":"Markdown"}}',oversized:'x'.repeat(2162689)})){
  await p.evaluate(([k,v])=>localStorage.setItem(k,v),[key,unknown]);await p.reload();await textbox(p).waitFor();await title(p).fill('Mevcut metin korunuyor');await p.waitForTimeout(400);
  check(name+'_preserved_and_restore_disabled',await raw(p)===unknown && await p.locator('[data-recovery-restore]').isDisabled() && await title(p).inputValue()==='Mevcut metin korunuyor');
 }
 // Valid local envelope can carry a malicious node; the real server refuses it before editor mutation.
 const bad={...record,fields:{...record.fields,DocumentJson:JSON.stringify({version:1,document:{type:'doc',content:[{type:'html',text:'<script>window.compromised=true</script>'}]}})}};
 await p.evaluate(([k,v])=>localStorage.setItem(k,v),[key,JSON.stringify(bad)]);await p.reload();await textbox(p).waitFor();await textbox(p).fill('Yerinde kalan metin');const retained=await doc(p);
 await p.locator('[data-recovery-restore]').click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('doğrulanamadı'));
 check('server_rejects_untrusted_document_without_dom_loss',await doc(p)===retained && await raw(p)===JSON.stringify(bad) && !await p.evaluate(()=>window.compromised));
 // Genuine server validation, delayed by routing, must not overwrite typing after the request began.
 await p.evaluate(([k,v])=>localStorage.setItem(k,v),[key,original]);await p.reload();await textbox(p).waitFor();let resumeValidation,validationArrived;
 const validationHeld=new Promise(r=>validationArrived=r),validationRelease=new Promise(r=>resumeValidation=r);
 await p.route('**/AdminDocument/ValidateRecovery',async route=>{const response=await route.fetch();validationArrived();await validationRelease;await route.fulfill({response});});
 await p.locator('[data-recovery-restore]').click();await validationHeld;await textbox(p).fill('Doğrulama sırasında yazıldı');const concurrent=await doc(p);resumeValidation();
 await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('Üzerine yazılmadı'));
 check('typing_during_validation_wins_without_deleting_recovery',await doc(p)===concurrent && await raw(p)===original);await p.unroute('**/AdminDocument/ValidateRecovery');
 // Persist a supported composite document via the production recovery format and restore through real admission.
 const composite={...record,fields:{...record.fields,DocumentJson:JSON.stringify({version:1,document:{type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'Metin 👋'}]},{type:'table',content:[{type:'tableRow',content:[{type:'tableCell',content:[{type:'paragraph',content:[{type:'text',text:'Hücre'}]}]}]}]},{type:'image',attrs:{src:'https://example.test/recovery.gif',alt:'GIF'}},{type:'youtube',attrs:{src:'https://youtu.be/jNQXAC9IVRw'}}]}})}};
 await p.evaluate(([k,v])=>localStorage.setItem(k,v),[key,JSON.stringify(composite)]);await p.reload();await textbox(p).waitFor();await p.locator('[data-recovery-restore]').click();await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('geri yüklendi'));
 check('composite_restore_keeps_text_table_image_video',await p.locator('.writing-document table').count()===1 && await p.locator('.writing-document img').count()===1 && await p.locator('.writing-document iframe').count()===1 && (await doc(p)).includes('Hücre'));
 violations.push(...await p.evaluate(()=>window.__recoveryCsp||[]));
 // Quota and missing coordination are controlled failures; normal server save remains available.
 await clearCreate(p);await create(p);
 await p.evaluate(()=>{const set=Storage.prototype.setItem;Storage.prototype.setItem=function(k,v){if(k.startsWith('devcore_document_draft_v1_'))throw new DOMException('Synthetic quota','QuotaExceededError');return set.call(this,k,v);};});
 await title(p).fill('Depolama hatası yazısı');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('Sunucuya yine kaydedilir');await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('kullanılamıyor'));
 check('controlled_quota_failure_keeps_form_save_enabled',await raw(p)===null && await p.getByRole('button',{name:'Taslağı kaydet',exact:true}).isEnabled());await submit(p);controlled.push('Injected localStorage quota failure for only JSON recovery keys.');
 await clearCreate(p);await create(p);await p.addInitScript(()=>{if(sessionStorage.getItem('f14-no-locks')==='1'){sessionStorage.removeItem('f14-no-locks');Object.defineProperty(navigator,'locks',{value:undefined,configurable:true});}});await p.evaluate(()=>sessionStorage.setItem('f14-no-locks','1'));await p.reload();await textbox(p).waitFor();
 await title(p).fill('Kilit yok yazısı');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('Kilit yok metni');await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('kullanılamıyor'));
 check('controlled_missing_locks_never_uses_unsafe_fallback',await raw(p)===null);await submit(p);controlled.push('Disabled Web Locks in one page only; no unsafe storage fallback.');
 await clearCreate(p);await create(p);await p.evaluate(()=>{const get=Storage.prototype.getItem;Storage.prototype.getItem=function(k){if(k.startsWith('devcore_document_draft_v1_'))throw new DOMException('Synthetic denied','SecurityError');return get.call(this,k);};});
 await title(p).fill('Depolama erişimi yok');await p.locator('[name=CategoryId]').selectOption('1001');await textbox(p).fill('Yazı korunuyor');await p.waitForFunction(()=>document.querySelector('[data-recovery-status]').textContent.includes('kullanılamıyor'));
 check('controlled_denied_storage_keeps_server_action_enabled',await p.getByRole('button',{name:'Taslağı kaydet',exact:true}).isEnabled());await submit(p);controlled.push('Injected getItem SecurityError only for JSON recovery.');
 // Real edit conflict after another authorized client commits, keeping the submitted text and local revision.
 const staleUrl=p.url(),staleId=await p.locator('[name=Id]').inputValue(),staleKey='devcore_document_draft_v1_edit_'+staleId;
 const other=await context.newPage();await attach(other);await other.goto(staleUrl);await textbox(other).waitFor();await title(other).fill('Diğer sekme sunucu kaydı');await submit(other);await other.close();
 await title(p).fill('409 korunacak metin');await submit(p,409);
 check('real409_retains_local_revision_and_current_form',!!await p.evaluate(k=>localStorage.getItem(k),staleKey) && await title(p).inputValue()==='409 korunacak metin' && await p.locator('[data-document-receipt]').count()===0);
 violations.push(...await p.evaluate(()=>window.__recoveryCsp||[]));check('no_page_errors',errors.length===0);check('no_application_console_errors',consoleErrors.length===0);check('no_csp_violations',violations.length===0);
 fs.writeFileSync(path.join(report,'recovery-browser.json'),JSON.stringify({count:Object.keys(checks).length,checks,errors,consoleErrors,resourceErrors,violations,controlled,scope:'Real Chrome/native MVC/owned PostgreSQL; external image and video references are stubbed, playback is not asserted.'},null,2)+'\n');
 console.log(JSON.stringify({count:Object.keys(checks).length,failed:[]}));
})().catch(e=>{fs.writeFileSync(path.join(report,'recovery-browser-failure.json'),JSON.stringify({checks,errors,violations,error:e.stack},null,2));console.error(e.stack);process.exitCode=1;}).finally(async()=>{await context?.close();fs.rmSync(profile,{recursive:true,force:true});});
