// Actual Chrome native editor -> preview -> test prepare -> submit; only the provider transport is the owned fixture sender.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.F38_BASE,report=process.env.F38_REPORT_DIR,packet=process.env.F38_PACKET_FILE,pg=process.env.F38_PG_PORT;
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(new URL(base).port,'15233');assert.equal(pg,'55533');
assert.ok(packet.startsWith('/tmp/devcoreblog-test-email-'));fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],consoleErrors=[],violations=[];let browser,context;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p',pg,'-d','test_email_http','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'});
assert.ok(sql('SHOW data_directory').trim().startsWith('/tmp/devcoreblog-test-email-'));
const snapshot=()=>sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id";SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";SELECT to_jsonb(c) FROM "NewsletterCampaigns" c ORDER BY "Id";');
const count=()=>fs.existsSync(packet)?fs.readFileSync(packet,'utf8').trim().split('\n').filter(Boolean).length:0;
const text=p=>p.getByRole('textbox',{name:'Yazı metni',exact:true});
async function openPreview(p){const next=context.waitForEvent('page');await p.getByRole('button',{name:'E-posta önizlemesi (yeni sekme)',exact:true}).click();const tab=await next;await tab.waitForLoadState('domcontentloaded');return tab;}
async function state(p){return{doc:await p.locator('[name=DocumentJson]').inputValue(),revision:await p.locator('[name=RecoveryRevision]').inputValue(),copy:await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_create'))};}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});context=await browser.newContext({viewport:{width:1440,height:1000},colorScheme:'light'});
 await context.exposeBinding('__f38Csp',(_source,directive)=>violations.push(directive));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f38Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});p.on('dialog',d=>d.accept());});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('fixture-admin');await p.locator('#password').fill('f38-synthetic-password');
 await p.getByRole('button',{name:'Giriş yap',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');await p.goto(base+'/AdminWriting/Create');await text(p).waitFor();
 await text(p).fill('Tarayıcı F38 Türkçe 👋 <script> yalnız metin');await p.waitForFunction(()=>localStorage.getItem('devcore_document_draft_v1_create')?.includes('Tarayıcı'));
 const sourceBefore=await state(p),before=snapshot(),calls=count();let tab=await openPreview(p);
 check('editor_and_preview_do_not_send',count()===calls&&snapshot()===before&&JSON.stringify(await state(p))===JSON.stringify(sourceBefore));
 await tab.getByRole('button',{name:'Test e-postası hazırla',exact:true}).click();await tab.waitForURL('**/AdminTestEmail/Prepare');
 check('prepare_has_single_readonly_recipient_and_no_send_side_effect',await tab.getByText('operator@example.test',{exact:true}).count()===1
  &&await tab.locator('input[name="Input.Recipient"],input[name="Input.To"],input[name="To"]').count()===0&&count()===calls&&snapshot()===before);
 check('prepare_is_turkish_and_describes_limits',await tab.locator('html').getAttribute('lang')==='tr'&&(await tab.locator('main').innerText()).includes('en fazla üç'));
 const subject=tab.getByRole('textbox',{name:'E-posta konusu',exact:true});await subject.fill('Tarayıcı denemesi 👋');await subject.focus();
 await tab.keyboard.press('Tab');check('keyboard_submit_focus_is_visible',await tab.getByRole('button',{name:'Test e-postası gönder',exact:true}).evaluate(el=>el===document.activeElement&&getComputedStyle(el).outlineStyle!=='none'));
 check('desktop_does_not_overflow',await tab.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await tab.screenshot({path:path.join(report,'test-email-desktop.png'),fullPage:true});
 const response=context.waitForEvent('response',{predicate:r=>r.url()===base+'/AdminTestEmail/Send'&&r.request().method()==='POST'});
 await tab.keyboard.press('Enter');const sent=await response;await tab.waitForURL('**/AdminTestEmail/Send');
 check('keyboard_submission_accepted_exactly_one_test',sent.status()===200&&count()===calls+1);
 check('only_closed_form_fields_cross_send_boundary',[...new URLSearchParams(sent.request().postData()).keys()].sort().join(',')==='Input.DocumentJson,Input.RequestId,Input.Subject,__RequestVerificationToken');
 check('result_distinguishes_acceptance_from_delivery',(await tab.getByRole('status').innerText()).includes('teslim edildiği veya okunduğu anlamına gelmez'));
 const headers=await sent.allHeaders();check('result_is_private_with_strict_csp',headers['cache-control'].includes('no-store')&&headers['x-robots-tag'].includes('noindex')&&headers['content-security-policy'].includes("style-src-attr 'none'"));
 check('source_editor_local_copy_and_database_are_preserved',snapshot()===before&&JSON.stringify(await state(p))===JSON.stringify(sourceBefore));
 const wire=JSON.parse(fs.readFileSync(packet,'utf8').trim().split('\n').at(-1));check('server_message_has_test_label_and_safe_exact_snapshot',wire.Subject==='[TEST] Tarayıcı denemesi 👋'&&wire.Recipient==='operator@example.test'&&wire.Html.includes('&lt;script&gt;')&&wire.Text.includes('Tarayıcı F38')&&!wire.Html.includes('<script>'));
 await p.setViewportSize({width:390,height:844});const mobile=await openPreview(p);await mobile.getByRole('button',{name:'Test e-postası hazırla',exact:true}).click();await mobile.waitForURL('**/AdminTestEmail/Prepare');
 await mobile.setViewportSize({width:390,height:844});check('mobile_test_form_is_readable_and_does_not_overflow',await mobile.getByRole('textbox',{name:'E-posta konusu',exact:true}).isVisible()
  &&await mobile.getByRole('button',{name:'Test e-postası gönder',exact:true}).isVisible()&&await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await mobile.screenshot({path:path.join(report,'test-email-mobile.png'),fullPage:true});check('mobile_preparation_does_not_send_another_message',count()===calls+1&&snapshot()===before);
 check('no_query_document_or_recipient_leak',tab.url()===base+'/AdminTestEmail/Send'&&mobile.url()===base+'/AdminTestEmail/Prepare');
 check('no_csp_or_runtime_errors',violations.length===0&&errors.length===0&&consoleErrors.length===0);
 await mobile.close();await tab.close();
})().catch(e=>{checks['completed']=false;process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=Object.values(checks).every(Boolean)&&!!Object.keys(checks).length;
 fs.writeFileSync(path.join(report,'test-email-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,page_errors:errors,csp_violations:violations,console_error_count:consoleErrors.length},null,2)+'\n');
 console.log(JSON.stringify({passed,checks:Object.keys(checks).length}));if(!passed)process.exitCode=1;
});
