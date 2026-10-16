// Real Chrome native admin forms; isolated database and synthetic sender are owned by the Python fixture.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.F39_BASE,report=process.env.F39_REPORT_DIR;
assert.equal(base,'http://127.0.0.1:15234');fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],csp=[],consoleErrors=[];let browser,context;
function check(name,ok){checks[name]=!!ok;assert.ok(ok,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__f39Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f39Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('fixture-admin');await p.locator('#password').fill('f39-synthetic-password');
 await p.getByRole('button',{name:'Giriş yap',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');await p.goto(base+'/AdminNewsletter/Edit/9001');await p.getByRole('textbox',{name:'Yazı metni',exact:true}).waitFor();
 check('writing_explains_separate_sending_and_current_archive', (await p.locator('main.writing-page').innerText()).includes('Kaydetmek veya sitede yayımlamak e-posta göndermez.')&&(await p.locator('main.writing-page').innerText()).includes('Tam arşiv doğrulanmış aktif abonelere açıktır.')&&!(await p.locator('main.writing-page').innerText()).includes('E-posta gönderimi: Başlatılmadı'));
 await p.goto(base+'/AdminNewsletter');
 check('newsletter_has_distinct_preparation_and_history_links',await p.getByRole('link',{name:'Gönderim hazırla',exact:true}).count()===1&&await p.getByRole('link',{name:'Önceki gönderimler',exact:true}).count()===1);
 await p.getByRole('link',{name:'Gönderim hazırla',exact:true}).click();await p.getByRole('button',{name:'Gönderim kopyasını hazırla',exact:true}).click();await p.waitForURL('**/AdminCampaign/Details/**');
 const url=p.url();check('turkish_confirmation_starts_unstarted',await p.locator('html').getAttribute('lang')==='tr'&&(await p.getByRole('status').innerText())==='Başlatılmadı');
 check('no_recipient_or_body_in_start_form',await p.locator('form[action*="/Start"] input').evaluateAll(es=>es.map(e=>e.name).sort().join(','))==='Confirm,Id,Version,__RequestVerificationToken');
 await p.getByText('Gönderilecek sabit metni incele',{exact:true}).click();check('snapshot_is_encoded_text',(await p.locator('.campaign-text').innerText()).includes('<script>')&&await p.locator('.campaign-text script').count()===0);
 const checkbox=p.locator('form[action*="/Start"] input[name=Confirm]');await checkbox.check();await checkbox.focus();await p.keyboard.press('Tab');const start=p.getByRole('button',{name:'Abonelere gönderimi başlat',exact:true});
 check('keyboard_action_has_visible_focus',await start.evaluate(el=>document.activeElement===el&&getComputedStyle(el).outlineStyle!=='none'));
 check('desktop_no_horizontal_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.screenshot({path:path.join(report,'campaign-admin-desktop-actions.png'),fullPage:true});await p.locator('#admin-page-content').evaluate(el=>el.scrollTop=0);await p.screenshot({path:path.join(report,'campaign-admin-desktop.png'),fullPage:true});
 const response=p.waitForResponse(r=>r.url().endsWith('/AdminCampaign/Start')&&r.request().method()==='POST');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.keyboard.press('Enter')]);const r=await response;assert.equal(p.url(),url);
 check('explicit_start_native_post_and_prg',r.status()===302);await p.reload();
 check('accepted_is_distinct_from_delivery',(await p.locator('.campaign-page').innerText()).includes('Sağlayıcı kabulü, teslim veya okunma kanıtı değildir.'));
 const cancel=p.locator('form[action*="/Cancel"]');await cancel.locator('[name=Confirm]').check();await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),cancel.getByRole('button',{name:'Kalan gönderimleri iptal et',exact:true}).click()]);assert.equal(p.url(),url);
 check('cancel_native_post_updates_status',(await p.getByRole('status').innerText())==='İptal edildi'&&await p.locator('form[action*="/Start"],form[action*="/Retry"]').count()===0);
 await p.setViewportSize({width:390,height:844});await p.reload();check('mobile_no_horizontal_overflow_and_refresh_accessible',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)&&await p.getByRole('link',{name:'Durumu yenile',exact:true}).count()===1);
 await p.locator('#admin-page-content').evaluate(el=>el.scrollTop=0);await p.screenshot({path:path.join(report,'campaign-admin-mobile.png'),fullPage:true});
 const headers=await (await p.goto(url)).allHeaders();check('private_strict_csp_and_no_referrer',headers['cache-control'].includes('no-store')&&headers['x-robots-tag'].includes('noindex')&&headers['referrer-policy']==='no-referrer'&&headers['content-security-policy'].includes("style-src-attr 'none'"));
 check('no_csp_console_or_runtime_errors',!csp.length&&!errors.length&&!consoleErrors.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);
 fs.writeFileSync(path.join(report,'campaign-admin-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,csp_violations:csp,page_errors:errors,console_error_count:consoleErrors.length},null,2)+'\n');
 console.log(JSON.stringify({passed,count:Object.keys(checks).length}));if(!passed)process.exitCode=1;
});
