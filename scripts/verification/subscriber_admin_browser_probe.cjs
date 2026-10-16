// Native real Chrome filters/navigation; no subscriber mutation endpoints exist.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.F40_BASE,report=process.env.F40_REPORT_DIR;assert.equal(base,'http://127.0.0.1:15235');const checks={},errors=[],csp=[],consoleErrors=[];let browser,context;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__f40Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f40Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('fixture-admin');await p.locator('#password').fill('f40-synthetic-password');await p.getByRole('button',{name:'Giriş yap',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');await p.getByRole('link',{name:'Aboneler',exact:true}).click();
 check('dedicated_turkish_inventory_and_labels',await p.locator('html').getAttribute('lang')==='tr'&&await p.getByRole('heading',{name:'Aboneler',exact:true}).count()===1&&await p.getByLabel('Liste durumu').count()===1);
 check('server_bounded_25_addresses',await p.locator('.subscriber-row').count()===25);
 const text=await p.locator('.subscriber-page').innerText();check('growth_and_current_state_definitions_are_explicit',text.includes('Yeniden başvuru yeni adres sayılmaz')&&text.includes('net büyüme ölçümü değildir')&&text.includes('canlı gönderimin açık olduğu anlamına gelmez'));
 check('desktop_no_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.screenshot({path:path.join(report,'subscriber-desktop.png'),fullPage:true});
 await p.getByLabel('Büyüme başlangıcı').fill('2026-10-02');await p.getByLabel('Büyüme bitişi (dahil)').fill('2026-10-02');await p.getByLabel('Liste durumu').selectOption('Active');await p.getByLabel('Liste durumu').focus();for(let i=0;i<12&&!(await p.getByRole('button',{name:'Filtreleri uygula',exact:true}).evaluate(e=>e===document.activeElement));i++)await p.keyboard.press('Tab');
 check('keyboard_filter_button_has_visible_focus',await p.getByRole('button',{name:'Filtreleri uygula',exact:true}).evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.keyboard.press('Enter')]);
 check('native_get_filter_preserves_dates_and_status',new URL(p.url()).searchParams.get('status')==='Active'&&new URL(p.url()).searchParams.get('from')==='2026-10-02'&&new URL(p.url()).searchParams.get('to')==='2026-10-02'&&await p.locator('.subscriber-row').count()===10);
 check('active_filter_includes_blocked_addresses_and_separates_eligibility',(await p.locator('.subscriber-list').innerText()).includes('Gönderim engeli var')&&await p.locator('[data-total="eligible"]').innerText()==='8');
 await p.getByLabel('Liste durumu').selectOption('All');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:'Filtreleri uygula',exact:true}).click()]);const next=p.getByRole('link',{name:'Sonraki sayfa',exact:true});
 const href=await next.getAttribute('href');check('paging_links_keep_context_without_addresses',new URL(href,base).searchParams.get('from')==='2026-10-02'&&new URL(href,base).searchParams.get('to')==='2026-10-02'&&!href.includes('@'));
 await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),next.click()]);check('second_page_is_bounded',await p.locator('.subscriber-row').count()===11);
 await p.setViewportSize({width:390,height:844});await p.reload();await p.locator('#admin-page-content').evaluate(e=>e.scrollTop=0);check('mobile_no_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.screenshot({path:path.join(report,'subscriber-mobile.png'),fullPage:true});
 const response=await p.goto(base+'/AdminSubscriber');const headers=await response.allHeaders();check('strict_private_headers',headers['cache-control'].includes('no-store')&&headers['x-robots-tag'].includes('noindex')&&headers['referrer-policy']==='no-referrer'&&headers['content-security-policy'].includes("style-src-attr 'none'"));
 check('no_mutation_or_export_forms',await p.locator('.subscriber-page form:not([method=get])').count()===0&&!(await p.locator('.subscriber-page').innerText()).includes('Dışa aktar'));
 check('no_csp_console_or_runtime_errors',!csp.length&&!errors.length&&!consoleErrors.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);fs.writeFileSync(path.join(report,'subscriber-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,csp_violations:csp,page_errors:errors,console_error_count:consoleErrors.length},null,2)+'\n');if(!passed)process.exitCode=1;
});
