// Real Chrome, native forms and real MVC/worker/PostgreSQL; mailbox delivery is explicitly simulated.
const fs=require('node:fs'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.REGISTRATION_TEST_BASE,report=process.env.REGISTRATION_TEST_REPORT;
const mailbox=process.env.REGISTRATION_TEST_MAILBOX,pg=process.env.REGISTRATION_TEST_PG_PORT;
const checks={},errors=[],csp=[],requests=[];let browser,failureType;
function check(name,value){checks[name]=!!value;if(!value)throw new Error(name);}
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p',pg,'-d','registration_http','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'}).trim();
const state=email=>sql(`SELECT "Status" || ':' || "Version" FROM "Subscribers" WHERE "AddressKey"='${email}'`);
async function mail(email){for(let i=0;i<120;i++){const rows=fs.existsSync(mailbox)?fs.readFileSync(mailbox,'utf8').trim().split('\n').filter(Boolean).map(JSON.parse):[];const row=rows.find(r=>r.recipient===email);if(row)return row;await new Promise(r=>setTimeout(r,100));}throw new Error('MailboxTimeout');}
(async()=>{
 check('owned_loopback_fixture',new URL(base).hostname==='127.0.0.1'&&/^\d{4,5}$/.test(pg));
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE||undefined});
 const context=await browser.newContext({viewport:{width:1440,height:900},locale:'tr-TR'});
 await context.exposeBinding('__registrationCsp',(_s,d)=>csp.push(d));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>window.__registrationCsp(e.effectiveDirective)));
 context.on('request',r=>requests.push({url:r.url(),method:r.method(),headers:r.headers()}));
 context.on('page',p=>p.on('pageerror',()=>errors.push('pageerror')));
 const page=await context.newPage();await page.goto(base+'/');
 await page.getByRole('link',{name:'Bültene abone ol',exact:true}).first().click();await page.waitForURL(base+'/abone-ol');
 check('native_navigation_reaches_form',await page.getByRole('heading',{name:'Bültene abone ol',exact:true}).count()===1);
 const input=page.getByLabel('E-posta adresiniz',{exact:true}),button=page.getByRole('button',{name:'Abone ol',exact:true});
 check('turkish_label_help_and_email_input',await input.getAttribute('type')==='email'&&await input.getAttribute('autocomplete')==='email'&&await input.getAttribute('aria-describedby')==='registration-help registration-errors');
 const before=requests.filter(r=>r.method==='POST'&&r.url===base+'/abone-ol').length;
 await button.click();check('native_required_prevents_empty_submission',await input.evaluate(e=>!e.validity.valid)&&requests.filter(r=>r.method==='POST'&&r.url===base+'/abone-ol').length===before);
 await input.fill('browser@example.test');await input.press('Tab');
 check('keyboard_focus_visible',await button.evaluate(e=>e===document.activeElement&&getComputedStyle(e).outlineStyle!=='none'));
 await page.screenshot({path:process.env.REGISTRATION_TEST_SCREENSHOT_DIR+'/desktop.png',fullPage:true});
 await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);const generic=await page.content();
 check('native_post_generic_acknowledgement',(await page.locator('[role=status]').innerText()).includes('başvuruya uygunsa')&&!generic.includes('browser@example.test'));
 check('request_does_not_activate',state('browser@example.test')==='0:1');
 const proof=await mail('browser@example.test');await page.goto(base+'/abonelik/dogrula#t='+proof.token);
 await page.waitForFunction(()=>!document.getElementById('verification-submit').disabled);
 check('mail_link_get_is_read_only',state('browser@example.test')==='0:1'&&new URL(page.url()).hash==='');
 await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'E-posta adresimi doğrula',exact:true}).click()]);
 check('native_confirmation_activates_once',state('browser@example.test')==='1:2'&&(await page.locator('body').innerText()).includes('E-posta adresiniz doğrulandı'));
 await page.goto(base+'/abone-ol');await page.getByLabel('E-posta adresiniz',{exact:true}).fill('browser@example.test');
 await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Abone ol',exact:true}).click()]);
 check('existing_active_has_identical_public_html',generic===await page.content());
 const nojs=await browser.newContext({viewport:{width:390,height:844},javaScriptEnabled:false,locale:'tr-TR'});const mobile=await nojs.newPage();await mobile.goto(base+'/abone-ol');
 check('mobile_no_horizontal_overflow',await mobile.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth));
 await mobile.getByLabel('E-posta adresiniz',{exact:true}).fill('nojs@example.test');
 await mobile.screenshot({path:process.env.REGISTRATION_TEST_SCREENSHOT_DIR+'/mobile.png',fullPage:true});
 await Promise.all([mobile.waitForNavigation(),mobile.getByRole('button',{name:'Abone ol',exact:true}).click()]);
 check('registration_works_without_javascript',state('nojs@example.test')==='0:1'&&(await mobile.locator('[role=status]').innerText()).includes('başvuruya uygunsa'));
 const nojsProof=await mail('nojs@example.test');await mobile.goto(base+'/abonelik/dogrula#t='+nojsProof.token);
 check('nojs_cannot_bypass_proof_confirmation',await mobile.getByRole('button',{name:'E-posta adresimi doğrula',exact:true}).isDisabled()&&state('nojs@example.test')==='0:1');
 await page.goto(base+'/abone-ol');
 const invalid=await page.evaluate(async()=>{const csrf=document.querySelector('input[name=__RequestVerificationToken]').value;const r=await fetch('/abone-ol',{method:'POST',body:new URLSearchParams({Email:'<img src=x onerror=alert(1)>@example.test',__RequestVerificationToken:csrf})});return {status:r.status,body:await r.text()};});
 const invalidText=await page.evaluate(body=>new DOMParser().parseFromString(body,'text/html').body.textContent,invalid.body);
 check('server_rejects_and_encodes_invalid_input',invalid.status===400&&invalidText.includes('Geçerli, tek bir e-posta adresi girin.')&&!invalid.body.includes('<img src=x onerror=alert(1)>'));
 check('proof_not_in_urls_referrer_or_browser_storage',requests.every(r=>!r.url.includes(proof.token)&&!(r.headers.referer||'').includes(proof.token))&&await page.evaluate(t=>!JSON.stringify({...localStorage,...sessionStorage}).includes(t),proof.token));
 check('no_auth_cookie',(await context.cookies()).every(c=>!c.name.includes('Admin')&&!c.name.includes('Reader')&&c.name!=='.AspNetCore.Cookies'));
 check('csp_and_page_errors_clean',csp.length===0&&errors.length===0);
 await nojs.close();await context.close();
})().catch(error=>{failureType=error.name;checks.completed=false;process.exitCode=1;}).finally(async()=>{
 if(browser)await browser.close();const passed=Object.keys(checks).length>0&&Object.values(checks).every(Boolean);
 fs.writeFileSync(report,JSON.stringify({passed,count:Object.keys(checks).length,checks,failureType,diagnostics:{cspDirectives:csp,pageErrorCount:errors.length},scope:'Real Chrome desktop/mobile/keyboard and native no-JS registration; real MVC/worker/PostgreSQL, simulated fixture mailbox.'},null,2)+'\n');if(!passed)process.exitCode=1;
});
