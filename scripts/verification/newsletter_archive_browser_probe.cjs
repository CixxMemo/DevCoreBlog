// Real Chrome archive reading with an owned synthetic login proof; no external email provider.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.ARCHIVE_BASE,dir=process.env.ARCHIVE_REPORT_DIR;
const seed=JSON.parse(fs.readFileSync(process.env.ARCHIVE_PROOF_FILE,'utf8'));
const checks={},violations=[],errors=[];let browser,stage='initial',failure;
function check(name,ok){stage=name;checks[name]=!!ok;if(!ok)throw new Error(name);}
(async()=>{
 check('owned_loopback',new URL(base).hostname==='127.0.0.1');
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE||undefined});
 const context=await browser.newContext({viewport:{width:1440,height:900}});
 await context.exposeBinding('__archiveCsp',(_s,d)=>violations.push(d));
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>window.__archiveCsp(e.effectiveDirective)));
 const page=await context.newPage();page.on('pageerror',()=>errors.push('page-error'));
 check('anonymous_archive_401',(await context.request.get(base+'/bultenler')).status()===401);
 await page.goto(base+'/abonelik/giris#t='+seed.token);await page.locator('#reader-proof-submit').waitFor();
 await page.waitForFunction(()=>!document.getElementById('reader-proof-submit').disabled);
 await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Bu cihazda giriş yap',exact:true}).click()]);
 await page.getByRole('link',{name:'Tam bülten arşivini aç'}).click();
 check('session_to_archive_link',new URL(page.url()).pathname==='/bultenler'&&await page.getByRole('heading',{name:'Bülten arşivi',exact:true}).isVisible());
 check('nine_bounded_cards',await page.locator('.archive-card').count()===9);
 await page.screenshot({path:path.join(dir,'archive-desktop.png'),fullPage:true});
 const issue=page.locator('.archive-card a[href="/bulten/f31-allowed"]');await issue.focus();
 check('keyboard_focus_visible',await issue.evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);
 check('reader_opens_canonical_body',(await page.locator('.document-content').innerText()).includes('F31_PRIVATE_BODY'));
 check('authored_script_is_only_text',await page.evaluate(()=>!window.f31Injected&&!window.f31Title)&&await page.locator('.document-content script').count()===0);
 check('private_noindex_and_no_public_metadata',await page.locator('meta[name=robots]').getAttribute('content')==='noindex, nofollow, noarchive'&&await page.locator('link[rel=canonical],script[type="application/ld+json"],meta[property^="og:"]').count()===0);
 await page.setViewportSize({width:390,height:844});
 check('mobile_detail_no_horizontal_overflow',await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 await page.screenshot({path:path.join(dir,'archive-mobile-detail.png'),fullPage:true});
 await page.getByRole('link',{name:'Bülten arşivine dön',exact:true}).click();
 check('mobile_list_no_horizontal_overflow',await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 await Promise.all([page.waitForNavigation(),page.getByRole('link',{name:'Sonraki',exact:true}).click()]);
 check('native_second_page',new URL(page.url()).search==='?page=2'&&await page.locator('.archive-card').count()===3);
 const nojs=await browser.newContext({javaScriptEnabled:false});await nojs.addCookies(await context.cookies());const plain=await nojs.newPage();
 check('no_javascript_reader_detail',(await plain.goto(base+'/bulten/f31-allowed')).status()===200&&(await plain.locator('body').innerText()).includes('F31_PRIVATE_BODY'));
 await nojs.close();await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Bu cihazdaki oturumu kapat',exact:true}).click()]);
 check('logout_denies_old_archive',(await context.request.get(base+'/bulten/f31-allowed')).status()===401);
 check('CSP_and_runtime_clean',violations.length===0&&errors.length===0);
 await context.close();
})().catch(e=>{failure=e.name;checks.completed=false;process.exitCode=1;}).finally(async()=>{
 const passed=Object.keys(checks).length>0&&Object.values(checks).every(Boolean);
 fs.writeFileSync(path.join(dir,'archive-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,stage,failure,cspViolations:violations,pageErrors:errors.length},null,2)+'\n');
 if(browser)await browser.close();if(!passed)process.exitCode=1;
});
