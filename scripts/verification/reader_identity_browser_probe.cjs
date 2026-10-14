// Real browser UI to owned MVC/PostgreSQL with a clearly simulated mailbox, never a real personal inbox.
const fs=require('node:fs');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.READER_TEST_BASE,report=process.env.READER_TEST_REPORT;
const seed=JSON.parse(fs.readFileSync(process.env.READER_TEST_PROOF_FILE,'utf8'));
const checks={},errors=[],csp=[],requests=[];let browser,stage='initial',failureType;
function check(name,value){checks[name]=!!value;stage=name;if(!value)throw new Error(name);}
(async()=>{
 check('owned_fixture',new URL(base).hostname==='127.0.0.1');
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE||undefined,timeout:20000});
 const context=await browser.newContext({viewport:{width:1440,height:900}});context.setDefaultTimeout(10000);
 await context.exposeBinding('__readerCsp',(_s,d)=>csp.push(d));
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>window.__readerCsp(e.effectiveDirective)));
 context.on('request',r=>requests.push({url:r.url(),headers:r.headers()}));
 const page=await context.newPage();page.on('pageerror',()=>errors.push('page-error'));
 const initial=await page.goto(base+'/sign-in');
 check('desktop_form_200',initial.status()===200&&await page.getByLabel('E-posta adresiniz',{exact:true}).isVisible());
 check('no_password_requested',await page.locator('input[type=password]').count()===0);
 await page.screenshot({path:process.env.READER_TEST_SCREENSHOT,fullPage:true});
 await page.setViewportSize({width:390,height:844});
 check('mobile_no_horizontal_overflow',await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth));
 await page.screenshot({path:process.env.READER_TEST_MOBILE_SCREENSHOT,fullPage:true});
 await page.getByLabel('E-posta adresiniz',{exact:true}).fill(seed.email);
 const submit=page.getByRole('button',{name:'Giriş bağlantısı gönder'});await submit.focus();
 check('keyboard_focus_visible',await submit.evaluate(b=>b===document.activeElement&&getComputedStyle(b).outlineStyle!=='none'));
 await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);
 check('generic_request_confirmation',(await page.locator('body').innerText()).includes('Adresiniz girişe uygunsa'));
 await page.waitForTimeout(250);
 let token;
 for(let i=0;i<100;i++){
  const rows=fs.existsSync(process.env.READER_TEST_MAILBOX)?fs.readFileSync(process.env.READER_TEST_MAILBOX,'utf8').trim().split('\n').filter(Boolean).map(x=>JSON.parse(x)):[];
  token=rows.filter(x=>x.recipient===seed.email).at(-1)?.token;if(token)break;
  await page.waitForTimeout(100);
 }
 check('worker_observer_mailbox_received_proof',!!token);
 await page.goto(base+'/abonelik/giris#t='+token);await page.waitForFunction(()=>!document.getElementById('reader-proof-submit').disabled);
 check('fragment_erased_before_confirmation',new URL(page.url()).hash==='');
 check('opening_link_creates_no_auth_cookie',(await context.cookies()).every(c=>c.name!=='AetherNotes.Reader'));
 check('proof_only_hidden_form_field',await page.locator('#reader-proof-token').inputValue()===token&&!(await page.locator('body').innerText()).includes(token));
 check('no_proof_in_storage_or_network',await page.evaluate(t=>!JSON.stringify({...localStorage,...sessionStorage}).includes(t),token)&&requests.every(r=>!r.url.includes(token)&&!(r.headers.referer||'').includes(token)));
 const button=page.getByRole('button',{name:'Bu cihazda giriş yap',exact:true});await button.focus();
 await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);
 check('keyboard_confirmation_opens_protected_session',new URL(page.url()).pathname==='/abonelik/oturum'&&(await page.locator('body').innerText()).includes('Aktif aboneliğiniz doğrulandı'));
 const cookie=(await context.cookies()).find(c=>c.name==='AetherNotes.Reader');
 check('reader_cookie_httponly_lax_persistent',!!cookie&&cookie.httpOnly&&cookie.sameSite==='Lax'&&cookie.expires>Date.now()/1000+6*86400);
 check('reader_cookie_is_not_admin',(await context.cookies()).every(c=>!c.name.includes('Admin'))&&(await context.request.get(base+'/AdminPost')).status()===404);
 await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Bu cihazdaki oturumu kapat'}).click()]);
 check('logout_clears_cookie_and_protected_access',(await context.cookies()).every(c=>c.name!=='AetherNotes.Reader')&&(await context.request.get(base+'/abonelik/oturum')).status()===401);
 await page.goto(base+'/abonelik/giris#t='+token);await page.waitForFunction(()=>!document.getElementById('reader-proof-submit').disabled);
 const replay=await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Bu cihazda giriş yap'}).click()]);
 check('used_link_is_400',replay[0].status()===400&&(await page.locator('body').innerText()).includes('kullanılmış'));
 await page.goto(base+'/abonelik/giris#t=invalid');await page.locator('#reader-proof-submit').waitFor();
 check('invalid_fragment_disabled',await page.locator('#reader-proof-submit').isDisabled()&&new URL(page.url()).hash==='');
 const nojs=await browser.newContext({javaScriptEnabled:false});const safe=await nojs.newPage();await safe.goto(base+'/abonelik/giris#t='+token);
 check('no_javascript_does_not_sign_in',await safe.locator('#reader-proof-submit').isDisabled()&&(await safe.locator('noscript').innerText()).includes('JavaScript'));
 check('standalone_no_third_party_requests',requests.every(r=>r.url.startsWith(base+'/')));
 check('csp_and_runtime_clean',csp.length===0&&errors.length===0);await nojs.close();await context.close();
})().catch(e=>{failureType=e.name;checks.completed=false;process.exitCode=1;}).finally(async()=>{
 const passed=Object.keys(checks).length>0&&Object.values(checks).every(Boolean);
 fs.writeFileSync(report,JSON.stringify({passed,count:Object.keys(checks).length,checks,stage,failureType,cspViolations:csp,pageErrors:errors.length,scope:'Real Chrome desktop/mobile/keyboard on owned MVC/PostgreSQL with simulated mailbox.'},null,2)+'\n');
 if(browser)await browser.close();if(!passed)process.exitCode=1;
});
