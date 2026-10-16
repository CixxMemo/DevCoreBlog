// Actual Chrome settings form, CSRF/PRG, read-back, keyboard and responsive/privacy behavior.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.F42_BASE,report=process.env.F42_REPORT_DIR;assert.equal(base,'http://127.0.0.1:15237');
const checks={},errors=[],csp=[],consoleErrors=[];let browser,context;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__f42Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f42Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('fixture-admin');await p.locator('#password').fill('f42-synthetic-password');await Promise.all([p.waitForURL('**/Admin/Dashboard'),p.getByRole('button',{name:'Giriş yap',exact:true}).click()]);
 await p.getByRole('link',{name:'Yayın ayarları',exact:true}).click();
 check('turkish_settings_and_accessible_editorial_fields',await p.locator('html').getAttribute('lang')==='tr'&&await p.getByRole('heading',{name:'Yayın ayarları',exact:true}).count()===1&&await p.getByLabel('Yayın adı',{exact:true}).count()===1&&await p.getByLabel('Hedef ritim bilgisi',{exact:true}).count()===1);
 const section=p.locator('.publication-page'),text=await section.innerText();
 check('sender_configuration_does_not_claim_domain_verification',text.includes('doğrulaması veya gerçek teslim kanıtı değildir')&&text.includes('Mevcut kampanyalar ve başlamış gönderimler'));
 check('secret_origin_and_sender_header_not_editable',await section.locator('input:not([type=hidden]),textarea').evaluateAll(es=>es.map(e=>e.name).sort().join(','))==='ApproveSender,Cadence,Name');
 await p.getByLabel('Yayın adı',{exact:true}).fill('Aether & Deneme');await p.getByLabel('Hedef ritim bilgisi',{exact:true}).fill('Haftalık; deney hazır olduğunda.');
 await p.locator('[name=ApproveSender]').check();await p.locator('[name=ApproveSender]').focus();
 for(let i=0;i<8;i++){await p.keyboard.press('Tab');if(await p.getByRole('button',{name:'Yayın ayarlarını kaydet',exact:true}).evaluate(e=>e===document.activeElement))break;}
 check('keyboard_submit_has_visible_focus',await p.getByRole('button',{name:'Yayın ayarlarını kaydet',exact:true}).evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 const posted=p.waitForResponse(r=>r.url().endsWith('/AdminPublication/Save')&&r.request().method()==='POST');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.keyboard.press('Enter')]);
 check('native_post_csrf_and_prg_reads_saved_values',(await posted).status()===302&&await p.locator('[data-setting=name]').innerText()==='Aether & Deneme'&&await p.locator('[data-setting=cadence]').innerText()==='Haftalık; deney hazır olduğunda.');
 check('saved_ampersand_remains_encoded_plain_text',await p.locator('[data-setting=name]').evaluate(e=>e.innerHTML==='Aether &amp; Deneme'));
 check('desktop_no_horizontal_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.locator('#admin-page-content').evaluate(e=>e.scrollTop=0);await p.screenshot({path:path.join(report,'publication-desktop.png'),fullPage:true});
 await p.getByRole('button',{name:'Yayın ayarlarını kaydet',exact:true}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'publication-desktop-form.png'),fullPage:true});
 await p.setViewportSize({width:390,height:844});await p.reload();await p.locator('#admin-page-content').evaluate(e=>e.scrollTop=0);check('mobile_no_horizontal_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.screenshot({path:path.join(report,'publication-mobile.png'),fullPage:true});
 await p.getByRole('button',{name:'Yayın ayarlarını kaydet',exact:true}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'publication-mobile-form.png'),fullPage:true});
 const r=await p.reload(),h=await r.allHeaders();check('private_headers_and_strict_CSP',h['cache-control'].includes('no-store')&&h['x-robots-tag'].includes('noindex')&&h['referrer-policy']==='no-referrer'&&h['content-security-policy'].includes("style-src-attr 'none'"));
 check('no_csp_console_or_runtime_errors',!csp.length&&!errors.length&&!consoleErrors.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);fs.mkdirSync(report,{recursive:true});fs.writeFileSync(path.join(report,'publication-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,csp_violations:csp,page_errors:errors,console_error_count:consoleErrors.length},null,2)+'\n');if(!passed)process.exitCode=1;
});
