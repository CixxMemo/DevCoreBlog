// Real Chrome: Turkish display, associated server/client errors and keyboard/mobile use.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base='http://127.0.0.1:15238',out=process.env.F44_REPORT_DIR,checks={},errors=[],csp=[],consoleErrors=[];
let browser,context;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 context=await browser.newContext({viewport:{width:1440,height:1000},locale:'tr-TR'});
 await context.exposeBinding('__f44Csp',(_s,v)=>csp.push(v));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__f44Csp(e.effectiveDirective)));
 context.on('page',p=>{p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 const p=await context.newPage();await p.goto(base+'/fixture-admin/login');
 check('login_labels_turkish',await p.getByLabel('Kullanıcı adı',{exact:true}).count()===1&&await p.getByLabel('Parola',{exact:true}).count()===1);
 await p.getByLabel('Kullanıcı adı',{exact:true}).fill('fixture-admin');await p.keyboard.press('Tab');
 check('login_keyboard_reaches_password',await p.locator('#password').evaluate(e=>e===document.activeElement));
 await p.locator('#password').fill('f44-synthetic-password');await p.keyboard.press('Tab');
 check('login_keyboard_reaches_submit',await p.getByRole('button',{name:'Giriş yap',exact:true}).evaluate(e=>e===document.activeElement));
 await Promise.all([p.waitForURL('**/Admin/Dashboard'),p.keyboard.press('Enter')]);
 check('shell_turkish_and_static_brand',await p.locator('html').getAttribute('lang')==='tr'&&(await p.locator('#admin-sidebar').textContent()).includes('Aether Letter')&&await p.getByRole('button',{name:'Oturumu kapat',exact:true}).count()===1);
 check('dashboard_operational_labels_turkish',(await p.locator('#database-status').innerText())==='Doğrulandı (PostgreSQL)'&&(await p.locator('#media-status').innerText()).includes('Yapılandırılmadı'));
 await p.goto(base+'/AdminCategory/Create');check('category_label_targets_custom_id',await p.getByLabel('Kategori adı',{exact:true}).getAttribute('id')==='category-name-input');
 await p.getByRole('button',{name:'Kategoriyi kaydet',exact:true}).click();
 check('client_required_is_turkish',await p.locator('[data-valmsg-for=Name]').innerText()==='Kategori adı gereklidir.');
 check('client_error_associated_and_invalid',await p.locator('#category-name-input').evaluate(e=>e.getAttribute('aria-invalid')==='true'&&e.getAttribute('aria-describedby').split(' ').some(id=>document.getElementById(id)?.textContent.includes('Kategori adı gereklidir.'))));
 await p.getByLabel('Kategori adı',{exact:true}).fill('F44 English user content');check('corrected_category_passes_client_validation',await p.locator('form[action="/AdminCategory/Create"]').evaluate(e=>e.checkValidity()&&window.jQuery(e).valid()));await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:'Kategoriyi kaydet',exact:true}).click()]);
 check('successful_save_preserves_user_language',await p.getByRole('link',{name:'F44 English user content',exact:true}).count()===1&&(await p.locator('main').innerText()).includes('DÜZENLE'));
 await p.goto(base+'/AdminWriting/Create');await p.getByRole('textbox',{name:'Yazı metni',exact:true}).waitFor();
 check('json_editor_associated_with_document_error',await p.getByRole('textbox',{name:'Yazı metni',exact:true}).evaluate(e=>e.getAttribute('aria-describedby').split(' ').includes('DocumentJson__error')&&!!document.getElementById('DocumentJson__error')));
 await p.goto(base+'/AdminPost/Create');await p.locator('.toastui-editor-defaultUI').waitFor();
 check('legacy_editor_toolbar_turkish',await p.getByRole('button',{name:/Kalın/}).count()>0&&await p.getByRole('button',{name:/Başlıklar/}).count()>0&&(await p.locator('.toastui-editor-mode-switch').innerText()).includes('Görsel'));
 await p.screenshot({path:path.join(out,'admin-editor-desktop.png'),fullPage:true});
 await p.goto(base+'/AdminPost?status=Draft');check('enum_label_turkish_value_unchanged',await p.locator('#status-filter').inputValue()==='Draft'&&(await p.locator('#status-filter option:checked').innerText())==='Taslak');
 await p.goto(base+'/Admin/Automations');await p.locator('[data-copy-snippet]').first().click();await p.getByText('KOPYALANDI!',{exact:true}).waitFor();
 check('clipboard_success_and_toast_turkish',(await p.locator('#admin-toast-container').innerText()).includes('[BAŞARILI]')&&await p.getByRole('button',{name:'Bildirimi kapat',exact:true}).count()===1);
 // Bypass native/client validation to observe the actual server-generated field response.
 await p.goto(base+'/AdminCategory/Create');await p.evaluate(()=>{const f=document.querySelector('form[action="/AdminCategory/Create"]');f.noValidate=true;HTMLFormElement.prototype.submit.call(f);});await p.waitForLoadState('domcontentloaded');
 await p.locator('#Name__error').waitFor();
 check('server_error_has_accessible_name_description',await p.getByLabel('Kategori adı',{exact:true}).evaluate(e=>e.getAttribute('aria-invalid')==='true'&&e.getAttribute('aria-describedby').split(' ').every(id=>!!document.getElementById(id))));
 await p.getByLabel('Kategori adı',{exact:true}).focus();await p.keyboard.press('Tab');check('invalid_form_keyboard_reaches_save',await p.getByRole('button',{name:'Kategoriyi kaydet',exact:true}).evaluate(e=>e===document.activeElement));
 await p.screenshot({path:path.join(out,'category-error-desktop.png'),fullPage:true});
 await p.setViewportSize({width:390,height:844});await p.reload();
 check('mobile_has_no_horizontal_overflow',await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await p.screenshot({path:path.join(out,'category-error-mobile.png'),fullPage:true});
 await p.getByRole('button',{name:'Yönetim menüsünü aç',exact:true}).click();check('mobile_drawer_turkish_and_keyboard_trapped',await p.getByRole('button',{name:'Yönetim menüsünü kapat',exact:true}).count()===1&&await p.locator('#admin-sidebar').evaluate(e=>e.contains(document.activeElement)));
 await p.keyboard.press('Escape');check('mobile_escape_returns_focus',await p.locator('#admin-menu-toggle').evaluate(e=>e===document.activeElement&&e.getAttribute('aria-expanded')==='false'));
 const nojs=await browser.newContext({javaScriptEnabled:false,locale:'tr-TR'});await nojs.addCookies(await context.cookies());const n=await nojs.newPage();await n.goto(base+'/AdminCategory/Create');await n.locator('form[action="/AdminCategory/Create"]').evaluate(e=>e.noValidate=true);await Promise.all([n.waitForNavigation({waitUntil:'domcontentloaded'}),n.getByRole('button',{name:'Kategoriyi kaydet',exact:true}).click()]);check('javascript_free_server_validation_turkish',await n.locator('#Name__error').innerText()==='Kategori adı gereklidir.');await nojs.close();
 check('no_runtime_console_or_CSP_errors',!errors.length&&!consoleErrors.length&&!csp.length);
})().catch(e=>{checks.completed=false;console.log(e.name+': '+e.message);process.exitCode=1;}).finally(async()=>{
 if(context)await context.close();if(browser)await browser.close();const passed=!!Object.keys(checks).length&&Object.values(checks).every(Boolean);fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'admin-language-browser.json'),JSON.stringify({passed,count:Object.keys(checks).length,checks,page_errors:errors,csp_violations:csp,console_error_count:consoleErrors.length},null,2)+'\n');if(!passed)process.exitCode=1;
});
