// Actual Chrome → MVC upload → Cloudinary → owned PostgreSQL round trip.
// Fault responses are labeled separately and never counted as provider evidence.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL, report = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
const fixtures = process.env.DEVCORE_MEDIA_FIXTURE_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
fs.mkdirSync(report, { recursive: true });
assert.ok(!fs.existsSync(path.join(report, 'images-browser.json')));
const real = {}, simulated = {}, errors = [], violations = [], assets = [];
let browser;
function check(name, value, target = real) { target[name] = !!value; assert.ok(value, name); }
function write() { fs.writeFileSync(path.join(report, 'images-browser.json'), JSON.stringify({
    real, simulated, assets, errors, violations,
    scope: 'Real Chrome/MVC/owned PostgreSQL, two small real Cloudinary uploads. Separately labeled simulated HTTP faults/cancellation; no remote deletion, email or production DB.'
}, null, 2) + '\n'); }
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => {
        window.__imageViolations = [...(window.__imageViolations || []), e.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', m => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource:')) errors.push(m.text().slice(0,300)); });
    page.on('dialog', dialog => dialog.type() === 'beforeunload' ? dialog.accept() : dialog.dismiss());
    const editor = page.getByRole('textbox', { name: 'Yazı metni', exact: true });
    const file = page.locator('[data-image-file]'), alt = page.locator('[data-image-alt]');
    const upload = page.getByRole('button', { name: 'Yükle ve ekle', exact: true });
    const status = page.locator('[data-image-status]');
    const json = async () => JSON.parse(await page.locator('[name=DocumentJson]').inputValue());
    const images = async () => (await json()).document.content.filter(n => n.type === 'image');
    const focusEditor = async () => { await editor.click(); await page.keyboard.press('ControlOrMeta+End'); };
    async function command(key) {
        await page.locator(`[data-writing-command=${key}]`).click();
        await page.waitForFunction(() => document.activeElement?.classList.contains('writing-document'));
    }
    async function save(expected = 302) {
        const response = page.waitForResponse(r => r.url().includes('/AdminWriting/') && r.request().method() === 'POST');
        await page.locator('[data-writing-save]').first().click();
        const result = await response;
        check('native_save_status_'+expected+'_'+Object.keys(real).length, result.status() === expected);
        await page.waitForLoadState(); await editor.waitFor();
    }
    await page.goto(base+'/fixture-admin/login');
    await page.locator('#username').fill('f17-admin'); await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button',{name:'Sign In',exact:true}).click(); await page.waitForURL('**/Admin/Dashboard');
    await page.goto(base+'/AdminWriting/Create'); await editor.waitFor();
    check('turkish_media_controls_start_fail_closed_without_file', await upload.isDisabled() &&
        await page.locator('[data-image-cancel]').isDisabled() && await page.locator('[data-image-update]').isDisabled());
    await page.locator('#post-title-input').fill('F12 gerçek görsel ve GIF yazımı');
    await page.locator('[name=CategoryId]').selectOption('1001');
    await editor.fill('Korunan başlangıç Türkçe 👋');
    const initial = await json();
    await file.setInputFiles({name:'bad.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg/>')});
    await upload.click();
    check('client_unsupported_file_keeps_document', JSON.stringify(await json())===JSON.stringify(initial) && (await status.innerText()).includes('JPEG'));
    await file.setInputFiles({name:'bad.png',mimeType:'image/png',buffer:Buffer.from('not a decoded image')});
    const badResponse = page.waitForResponse(r => r.url().endsWith('/AdminPost/UploadEditorImage'));
    await upload.click();
    check('real_server_rejects_forged_image_400', (await badResponse).status()===400);
    await page.waitForFunction(() => document.querySelector('[data-image-cancel]').disabled);
    check('real_rejected_upload_keeps_document_and_save_enabled', JSON.stringify(await json())===JSON.stringify(initial) && await page.locator('[data-writing-save]').first().isEnabled());
    const noCsrf = await context.request.post(base+'/AdminPost/UploadEditorImage', {multipart:{file:{name:'synthetic.png',mimeType:'image/png',buffer:fs.readFileSync(path.join(fixtures,'source.png'))}}});
    check('real_upload_requires_antiforgery', noCsrf.status()===400);
    const anon = await browser.newContext();
    const denied = await anon.request.post(base+'/AdminPost/UploadEditorImage', {multipart:{file:{name:'synthetic.png',mimeType:'image/png',buffer:Buffer.from('invalid')}}});
    check('real_upload_requires_admin', [400,404].includes(denied.status())); await anon.close();
    for (const kind of ['png','gif']) {
        await focusEditor();
        await file.setInputFiles(path.join(fixtures,'source.'+kind));
        await alt.fill(kind==='png' ? 'Türkçe <script> " 👋 açıklama' : '');
        let release, reached;
        const held = new Promise(resolve => { release = resolve; });
        const requestReached = new Promise(resolve => { reached = resolve; });
        const route = async r => { reached(); await held; await r.continue(); };
        await context.route('**/AdminPost/UploadEditorImage',route);
        const realResponse = page.waitForResponse(r => r.url().endsWith('/AdminPost/UploadEditorImage'));
        await upload.click();
        await requestReached;
        check(kind+'_pending_keeps_editor_editable_and_blocks_save', await page.locator('[data-writing-save]').first().isDisabled() && await editor.getAttribute('contenteditable')==='true');
        await focusEditor(); await page.keyboard.type(' Yükleme sürerken '+kind+' yazısı');
        release();
        const response = await realResponse;
        check(kind+'_real_upload_returns_200',response.status()===200);
        const payload = await response.json();
        assets.push({kind,url:payload.url,publicId:payload.publicId,width:payload.width,height:payload.height}); write();
        await page.waitForFunction(() => document.querySelector('[data-image-status]').textContent.includes('Görsel eklendi.'));
        await context.unroute('**/AdminPost/UploadEditorImage',route);
        check(kind+'_real_provider_metadata_and_mapped_insertion', (await images()).length===(kind==='png'?1:2) &&
            (await editor.innerText()).includes('Yükleme sürerken '+kind+' yazısı') && assets.at(-1).publicId && new URL(assets.at(-1).url).hostname==='res.cloudinary.com');
        const image = editor.locator('img').last();
        await image.waitFor();
        await image.evaluate(el => el.decode());
        check(kind+'_real_delivery_decodes_in_browser', await image.evaluate(el => el.complete && el.naturalWidth>0 && el.naturalHeight>0));
        if (kind==='gif') {
            const frames=new Set();
            for (let sample=0;sample<6;sample++) {
                const bytes=await image.screenshot({animations:'allow'});
                frames.add(require('node:crypto').createHash('sha256').update(bytes).digest('hex'));
                await page.waitForTimeout(220);
            }
            check('real_gif_visibly_animates_in_chrome',frames.size>1);
        }
        check(kind+'_canonical_image_omits_provider_identity_and_dimensions', !Object.hasOwn((await images()).at(-1).attrs,'publicId') && (await images()).at(-1).attrs.width==null && (await images()).at(-1).attrs.height==null);
        violations.push(...await page.evaluate(()=>window.__imageViolations||[]));
    }
    check('alt_is_dom_encoded_and_empty_gif_is_decorative', await editor.locator('img').first().getAttribute('alt')==='Türkçe <script> " 👋 açıklama' && await editor.locator('img').last().getAttribute('alt')==='' && await editor.locator('script').count()===0);
    await command('undo'); check('undo_removes_latest_image_without_provider_deletion',(await images()).length===1);
    await command('redo'); check('redo_restores_same_reference',(await images()).length===2 && (await images()).at(-1).attrs.src===assets.at(-1).url);
    await editor.locator('img').first().click();
    await alt.fill('👋'.repeat(301)); await page.locator('[data-image-update]').click();
    check('alt_over_three_hundred_runes_rejected', (await images())[0].attrs.alt==='Türkçe <script> " 👋 açıklama');
    await alt.fill('👋'.repeat(300)); await page.locator('[data-image-update]').click();
    check('three_hundred_rune_alt_update_is_supported',[...(await images())[0].attrs.alt].length===300);
    await save();
    check('native_real_database_round_trip_preserves_both_images',(await images()).length===2 && (await images())[0].attrs.src===assets[0].url && (await images())[1].attrs.src===assets[1].url && [...(await images())[0].attrs.alt].length===300);
    const beforeInvalid=JSON.stringify(await json());
    await page.locator('#post-title-input').fill(''); await save(400);
    check('native_validation_error_keeps_uploaded_images_and_document',JSON.stringify(await json())===beforeInvalid);
    await page.locator('#post-title-input').fill('F12 gerçek görsel ve GIF yazımı');
    for (const code of [400,413,429,503]) {
        const before=JSON.stringify(await json());
        await file.setInputFiles(path.join(fixtures,'source.png'));
        const fault=r=>r.fulfill({status:code,contentType:'application/json',body:JSON.stringify({success:false,message:'synthetic failure'})});
        await context.route('**/AdminPost/UploadEditorImage',fault);
        await upload.click(); await page.waitForFunction(()=>document.querySelector('[data-image-status]').textContent.includes('Metniniz korunuyor'));
        check('simulated_'+code+'_keeps_current_document_and_saves',JSON.stringify(await json())===before && await page.locator('[data-writing-save]').first().isEnabled(),simulated);
        await context.unroute('**/AdminPost/UploadEditorImage',fault);
    }
    const beforeCancel=JSON.stringify(await json());
    let releaseCancel;
    const cancelled=new Promise(resolve=>{releaseCancel=resolve;});
    const slow=async r=>{await cancelled; try {await r.fulfill({status:503,body:''});}catch{}};
    await context.route('**/AdminPost/UploadEditorImage',slow); await upload.click();
    await page.locator('[data-image-cancel]').click(); releaseCancel();
    await page.waitForFunction(()=>document.querySelector('[data-image-status]').textContent.includes('Yükleme iptal edildi'));
    check('simulated_cancel_keeps_document_and_unlocks_save',JSON.stringify(await json())===beforeCancel && await page.locator('[data-writing-save]').first().isEnabled(),simulated);
    await context.unroute('**/AdminPost/UploadEditorImage',slow);
    const malformed=r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({success:true,url:'javascript:alert(1)',publicId:'x',width:1,height:1})});
    await context.route('**/AdminPost/UploadEditorImage',malformed); await upload.click();
    await page.waitForFunction(()=>document.querySelector('[data-image-status]').textContent.includes('Görsel eklenemedi'));
    check('simulated_unsafe_provider_response_is_not_inserted',JSON.stringify(await json())===beforeCancel,simulated);
    await context.unroute('**/AdminPost/UploadEditorImage',malformed);
    const pasteBefore=JSON.stringify(await json());
    await editor.evaluate(el=>{const data=new DataTransfer();data.setData('text/html','<img src="data:image/png;base64,AAAA" onerror="alert(1)">');el.dispatchEvent(new ClipboardEvent('paste',{bubbles:true,cancelable:true,clipboardData:data}));});
    check('unsafe_image_paste_is_rejected_without_document_change',JSON.stringify(await json())===pasteBefore);
    await file.setInputFiles({name:'large.png',mimeType:'image/png',buffer:Buffer.alloc(8388609)}); await upload.click();
    check('client_large_file_is_rejected_without_upload',JSON.stringify(await json())===pasteBefore && (await status.innerText()).includes('8 MiB'));
    for (const width of [1440,390]) {
        await page.setViewportSize({width,height:1100});
        if (width<768) await page.waitForFunction(()=>getComputedStyle(document.querySelector('#admin-sidebar')).visibility==='hidden');
        // Resize updates the shared responsive navigation on the next animation frame.
        await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
        const geometry=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,
            outside:[...document.querySelectorAll('main *')].filter(el=>el.getBoundingClientRect().right>innerWidth)
                .map(el=>({tag:el.tagName,class:el.className,right:el.getBoundingClientRect().right})),
            styles:[...document.querySelectorAll('.writing-document [style]')].map(el=>el.tagName)}));
        fs.writeFileSync(path.join(report,'geometry-'+width+'.json'),JSON.stringify(geometry,null,2)+'\n');
        // Measure before screenshot instrumentation temporarily changes image DOM attributes.
        check('viewport_'+width+'_has_no_page_overflow_or_dynamic_styles',geometry.scroll<=geometry.width && geometry.styles.length===0);
        await page.screenshot({path:path.join(report,'images-'+width+'.png'),fullPage:true});
        // Playwright can restore an absent img style as style=""; remove only that test artifact.
        await editor.locator('img[style=""]').evaluateAll(elements=>elements.forEach(el=>el.removeAttribute('style')));
        await file.focus(); check('keyboard_focus_'+width,await file.evaluate(el=>document.activeElement===el));
    }
    violations.push(...await page.evaluate(()=>window.__imageViolations||[]));
    check('no_runtime_or_console_errors',errors.length===0);
    check('no_csp_violations',violations.length===0);
})().catch(e=>{real.completed=false; console.error(e.name+': '+e.message.slice(0,180)); process.exitCode=1;})
.finally(async()=>{write();if(browser)await browser.close();console.log(JSON.stringify({real_count:Object.keys(real).length,simulated_count:Object.keys(simulated).length,failed:[...Object.keys(real).filter(k=>!real[k]),...Object.keys(simulated).filter(k=>!simulated[k])],assets:assets.length}));});
