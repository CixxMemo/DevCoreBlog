// Real Chrome: home form uses the existing HTTP admission path with and without JS.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base='http://127.0.0.1:15239',out=process.env.F46_REPORT_DIR,checks={},errors=[],csp=[],consoleErrors=[];
let browser,context;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 context=await browser.newContext({viewport:{width:1440,height:1000},locale:'tr-TR'});
 await context.exposeBinding('__f46Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f46Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const page=await context.newPage();await page.goto(base+'/',{waitUntil:'domcontentloaded'});
 check('headline_central_preferences_and_primary_action',await page.getByRole('heading',{name:'Yapay zekâyı deneyerek öğren',exact:true}).count()===1&&(await page.locator('#publication-name').textContent())==='Aether & Yeni'&&await page.getByRole('button',{name:'Abone ol',exact:true}).count()===1);
 check('single_semantic_form_no_duplicate_ids',await page.locator('#registration-form').count()===1&&await page.evaluate(()=>{const ids=[...document.querySelectorAll('[id]')].map(e=>e.id);return new Set(ids).size===ids.length;})&&await page.getByLabel('E-posta adresiniz',{exact:true}).count()===1);
 check('desktop_no_feed_discovery_or_rss_navigation',await page.locator('link[type="application/rss+xml"],link[type="application/atom+xml"],a[href="/rss.xml"],#rss-follow-link').count()===0);
 await page.getByLabel('E-posta adresiniz',{exact:true}).focus();await page.keyboard.press('Tab');
 check('keyboard_email_to_submit_visible_focus',await page.getByRole('button',{name:'Abone ol',exact:true}).evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 await page.keyboard.press('Enter');check('native_required_prevents_empty_submission',new URL(page.url()).pathname==='/'&&await page.getByLabel('E-posta adresiniz',{exact:true}).evaluate(e=>e.validity.valueMissing));
 await page.screenshot({path:path.join(out,'home-desktop.png'),fullPage:true});
 await page.setViewportSize({width:390,height:844});await page.reload({waitUntil:'domcontentloaded'});
 check('mobile_no_horizontal_overflow',await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await page.locator('#mobile-menu-btn').click();
 check('mobile_drawer_keeps_email_subscription_without_rss',await page.getByRole('link',{name:'Bültene abone ol',exact:true}).isVisible()&&await page.locator('a[href="/rss.xml"],#rss-follow-link').count()===0);
 await page.keyboard.press('Escape');
 check('mobile_drawer_escape_restores_focus',await page.locator('#mobile-menu-btn').evaluate(e=>e===document.activeElement&&e.getAttribute('aria-expanded')==='false'));
 check('mobile_form_fits_viewport',await page.getByLabel('E-posta adresiniz',{exact:true}).evaluate(e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;}));
 await page.screenshot({path:path.join(out,'home-mobile.png'),fullPage:true});
 // Disable only native validation to exercise the real server response.
 await page.locator('#registration-form').evaluate(e=>e.noValidate=true);await page.getByLabel('E-posta adresiniz',{exact:true}).fill('not an address');
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded'}),page.getByRole('button',{name:'Abone ol',exact:true}).click()]);
 check('server_error_is_associated_and_never_success',new URL(page.url()).pathname==='/abone-ol'&&await page.getByLabel('E-posta adresiniz',{exact:true}).evaluate(e=>e.getAttribute('aria-invalid')==='true'&&e.getAttribute('aria-describedby').split(' ').every(id=>document.getElementById(id)))&&await page.getByRole('status').count()===0);
 await page.getByLabel('E-posta adresiniz',{exact:true}).fill('home-browser@example.test');
 await Promise.all([page.waitForNavigation({waitUntil:'domcontentloaded'}),page.getByRole('button',{name:'Abone ol',exact:true}).click()]);
 check('actual_form_response_generic_and_verification_required',(await page.getByRole('status').innerText()).includes('Adresiniz başvuruya uygunsa')&&(await page.locator('main').innerText()).includes('adresinizi doğruladıktan sonra etkinleşir'));
 await page.goto(base+'/?kind=philosophy',{waitUntil:'domcontentloaded'});check('discovery_filter_selects_actual_kind',await page.locator('#home-kind').inputValue()==='philosophy');
 check('fixture_reader_cookie_issued',(await context.request.get(base+'/fixture-home/reader')).status()===204);await page.goto(base+'/',{waitUntil:'domcontentloaded'});
 check('valid_reader_sees_archive_instead_of_registration',await page.locator('#reader-archive-cta').count()===1&&await page.locator('#registration-form').count()===0);
 await Promise.all([page.waitForURL('**/bultenler',{waitUntil:'domcontentloaded'}),page.locator('#reader-archive-cta').click()]);check('reader_cta_reaches_authorized_archive',new URL(page.url()).pathname==='/bultenler');
 const nojs=await browser.newContext({javaScriptEnabled:false,locale:'tr-TR',viewport:{width:390,height:844}});const n=await nojs.newPage();await n.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));await n.goto(base+'/',{waitUntil:'domcontentloaded'});await n.getByLabel('E-posta adresiniz',{exact:true}).fill('home-nojs@example.test');
 await Promise.all([n.waitForNavigation({waitUntil:'domcontentloaded'}),n.getByRole('button',{name:'Abone ol',exact:true}).click()]);check('javascript_free_real_admission_works',(await n.getByRole('status').innerText()).includes('Adresiniz başvuruya uygunsa'));await nojs.close();
 const dark=await browser.newContext({viewport:{width:390,height:844},colorScheme:'dark'});const d=await dark.newPage();await d.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));await d.goto(base+'/',{waitUntil:'domcontentloaded'});await d.evaluate(()=>document.documentElement.classList.add('dark'));await d.screenshot({path:path.join(out,'home-dark-mobile.png'),fullPage:true});
 check('dark_mode_email_text_and_background_differ',await d.getByLabel('E-posta adresiniz',{exact:true}).evaluate(e=>{const s=getComputedStyle(e);return s.color!==s.backgroundColor&&s.backgroundColor!=='rgba(0, 0, 0, 0)';}));await dark.close();
 check('no_runtime_console_or_csp_errors',!errors.length&&!consoleErrors.length&&!csp.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'home-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,page_errors:errors,csp_violations:csp,console_error_count:consoleErrors.length},null,2)+'\n');if(!passed)process.exitCode=1;
});
