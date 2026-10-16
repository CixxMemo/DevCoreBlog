// Real Chrome reads actual MVC/SQL metrics; provider delivery itself is outside this read-only fixture.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.F41_BASE,id=process.env.F41_ID,report=process.env.F41_REPORT_DIR;
assert.equal(base,'http://127.0.0.1:15236');assert.match(id,/^[a-f0-9-]{36}$/);fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],csp=[],consoleErrors=[];let browser,context;
function check(name,ok){checks[name]=!!ok;assert.ok(ok,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__f41Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f41Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('fixture-admin');await p.locator('#password').fill('f41-synthetic-password');
 await Promise.all([p.waitForURL('**/Admin/Dashboard'),p.getByRole('button',{name:'Sign In',exact:true}).click()]);
 await p.goto(base+'/AdminCampaign/Details/'+id);await p.getByRole('link',{name:'Denemeleri ve teslim gözlemlerini incele',exact:true}).click();
 const section=p.locator('.campaign-page');const text=await section.innerText();
 check('separate_turkish_metrics_from_campaign_details',await p.locator('html').getAttribute('lang')==='tr'&&await p.getByRole('heading',{name:'Gönderim ölçümleri',exact:true}).count()===1);
 check('real_database_counts_are_distinct',await p.locator('[data-metric=selected]').innerText()==='8'&&await p.locator('[data-metric=attempts]').innerText()==='8'&&await p.locator('[data-metric=accepted]').innerText()==='3'&&await p.locator('[data-metric=delivered]').innerText()==='3');
 check('definitions_unknown_and_independent_facts_are_explicit',text.includes('birden fazla deneme aynı alıcıyı çoğaltmaz')&&text.includes('bu sayılar toplanmaz')&&text.includes('teslim edilmediğini kanıtlamaz')&&text.includes('okunma ölçülmüyor'));
 check('encoded_subject_is_plain_text',await p.getByRole('heading',{name:'Metrik <b> & konu',exact:true}).count()===1&&await section.locator('h2 b').count()===0);
 check('no_addresses_body_or_mutation_forms',!text.includes('@example.test')&&!text.includes('Private F41 body')&&await section.locator('form,button').count()===0);
 check('desktop_no_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await p.locator('#admin-page-content').evaluate(e=>e.scrollTop=0);await p.screenshot({path:path.join(report,'metrics-desktop.png'),fullPage:true});
 await p.getByRole('heading',{name:'Çağrı denemeleri',exact:true}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'metrics-attempts.png'),fullPage:true});
 await p.getByRole('heading',{name:'Eşleşen sağlayıcı gözlemleri',exact:true}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'metrics-provider.png'),fullPage:true});
 const refresh=p.getByRole('link',{name:'Ölçümleri yenile',exact:true});await refresh.focus();
 check('keyboard_refresh_has_visible_focus',await refresh.evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.keyboard.press('Enter')]);check('native_readonly_refresh_keeps_campaign',new URL(p.url()).searchParams.get('id')===id||new URL(p.url()).pathname.endsWith('/'+id));
 const before=p.url();await p.waitForTimeout(1200);check('no_polling_or_automatic_reload',p.url()===before&&await p.locator('meta[http-equiv=refresh]').count()===0);
 await p.setViewportSize({width:390,height:844});await p.reload();await p.locator('#admin-page-content').evaluate(e=>e.scrollTop=0);check('mobile_no_overflow_and_navigation_accessible',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)&&await p.getByRole('link',{name:'Gönderim durumuna dön',exact:true}).count()===1);await p.screenshot({path:path.join(report,'metrics-mobile.png'),fullPage:true});
 await p.getByRole('heading',{name:'Eşleşen sağlayıcı gözlemleri',exact:true}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'metrics-mobile-provider.png'),fullPage:true});
 const r=await p.reload(),headers=await r.allHeaders();check('strict_private_headers',headers['cache-control'].includes('no-store')&&headers['x-robots-tag'].includes('noindex')&&headers['referrer-policy']==='no-referrer'&&headers['content-security-policy'].includes("style-src-attr 'none'"));
 check('no_csp_console_or_runtime_errors',!csp.length&&!errors.length&&!consoleErrors.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);
 fs.writeFileSync(path.join(report,'metrics-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,csp_violations:csp,page_errors:errors,console_error_count:consoleErrors.length},null,2)+'\n');if(!passed)process.exitCode=1;
});
