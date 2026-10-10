// Real keyboard/toolbar/save journey on a disposable MVC/PostgreSQL fixture.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL;
const report = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
fs.mkdirSync(report, { recursive: true });
assert.ok(!fs.existsSync(path.join(report, 'writing-browser.json')));
const checks = {}, errors = [], violations = [];
let browser;
function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
function canonical(node) {
    // Omitted and explicit null/default attributes have identical v1 semantics.
    const result = { type: node.type };
    if (node.text !== undefined) result.text = node.text;
    const attrs = Object.fromEntries(Object.entries(node.attrs || {}).filter(([k,v]) => v !== null && !(k === 'start' && v === 1)).sort(([a], [b]) => a.localeCompare(b)));
    if (Object.keys(attrs).length) result.attrs = attrs;
    if (node.content?.length) result.content = node.content.map(canonical);
    if (node.marks?.length) result.marks = node.marks.map(canonical).sort((a,b) => a.type.localeCompare(b.type));
    return result;
}
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await context.route('https://fonts.googleapis.com/**', r => r.fulfill({ contentType: 'text/css', body: '' }));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => {
        window.__writingViolations = [...(window.__writingViolations || []), e.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', m => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource:')) errors.push(m.text().slice(0, 400)); });
    page.on('dialog', dialog => dialog.type() === 'prompt' ? dialog.accept('https://example.test/link') :
        dialog.type() === 'beforeunload' ? dialog.accept() : dialog.dismiss());
    await page.goto(base + '/fixture-admin/login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard');
    const recoveryKey = 'devcore_editor_draft_create';
    const recoveryValue = 'F09 synthetic legacy recovery remains untouched';
    await page.evaluate(({ key, value }) => localStorage.setItem(key, value), { key: recoveryKey, value: recoveryValue });
    await page.goto(base + '/AdminPost');
    await page.getByRole('link', { name: '+ Yeni yazı', exact: true }).click();
    const editor = page.getByRole('textbox', { name: 'Yazı metni', exact: true });
    await editor.waitFor();
    check('shared_turkish_form_loads_local_editor', await page.locator('[data-writing-command]').count() === 16 && await page.locator('[data-document-fallback]').isHidden());
    await page.locator('#post-title-input').fill('F09 tarayıcı yazısı');
    await page.locator('[name=CategoryId]').selectOption('1001');
    await editor.fill('Kalıcı Türkçe metin 👋');
    await editor.press('ControlOrMeta+a');
    await page.getByRole('button', { name: 'Kalın', exact: true }).click();
    check('toolbar_bold_updates_hidden_canonical_envelope', JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document.content[0].content[0].marks.some(m => m.type === 'bold'));
    await page.getByRole('button', { name: 'Geri al', exact: true }).click();
    check('undo_removes_last_formatting', !JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document.content[0].content[0].marks);
    await page.getByRole('button', { name: 'Yinele', exact: true }).click();
    check('redo_restores_last_formatting', JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document.content[0].content[0].marks.some(m => m.type === 'bold'));
    for (const name of ['İtalik', 'Altı çizili', 'Satır içi kod', 'Bağlantı'])
        await page.getByRole('button', { name, exact: true }).click();
    const first = JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document;
    check('toolbar_can_combine_all_five_allowed_marks', first.content[0].content[0].marks.length === 5);
    const savedResponse = page.waitForResponse(r => r.url().includes('/AdminWriting/Create') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
    check('native_create_save_redirects_after_commit', (await savedResponse).status() === 302);
    await page.waitForURL('**/AdminWriting/Edit/*');
    await editor.waitFor();
    const url = page.url();
    check('create_reopens_same_document_and_success_receipt', JSON.stringify(canonical(JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document)) === JSON.stringify(canonical(first)) && await page.getByText('Yazı kaydedildi.', { exact: true }).count() === 1);
    const title = page.locator('#post-title-input');
    const stale = await context.newPage();
    await stale.goto(url);
    await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).waitFor();
    await title.fill('F09 kazanan başlık');
    await page.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true }).click();
    await page.waitForLoadState();
    await editor.waitFor();
    await stale.locator('#post-title-input').fill('F09 korunacak başlık');
    await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).fill('F09 kaybolmayacak Türkçe metin 👋');
    const conflictResponse = stale.waitForResponse(r => r.url().includes('/AdminWriting/Edit/') && r.request().method() === 'POST');
    await stale.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true }).click();
    const conflict = await conflictResponse;
    await stale.waitForLoadState();
    await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).waitFor();
    check('real_stale_edit_returns_private_409', conflict.status() === 409 && (await conflict.allHeaders())['cache-control'].includes('no-store'));
    check('409_keeps_metadata_document_and_original_revision', await stale.locator('#post-title-input').inputValue() === 'F09 korunacak başlık' && (await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).innerText()).includes('kaybolmayacak') && await stale.locator('[name=EditVersion]').inputValue() === '1');
    check('409_editor_works_under_strict_csp', (await conflict.allHeaders())['content-security-policy'].includes("style-src-attr 'none'"));
    for (const width of [1440, 390]) {
        await stale.setViewportSize({ width, height: 1000 });
        if (width < 768) await stale.waitForFunction(() =>
            getComputedStyle(document.querySelector('#admin-sidebar')).visibility === 'hidden');
        const reload = stale.getByRole('link', { name: 'Güncel yazıyı yeniden aç', exact: true });
        await reload.focus(); await stale.keyboard.press('Shift+Tab'); await stale.keyboard.press('Tab');
        check('keyboard_focus_visible_' + width, await reload.evaluate(e => e === document.activeElement && getComputedStyle(e).outlineStyle !== 'none'));
        check('mobile_desktop_no_page_overflow_' + width, await stale.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        check('writing_form_is_visible_within_viewport_' + width, await stale.locator('.writing-page').evaluate(e => {
            const r = e.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth;
        }));
        await stale.screenshot({ path: path.join(report, 'writing-conflict-' + width + '.png'), fullPage: true });
    }
    await stale.getByRole('link', { name: 'Güncel yazıyı yeniden aç', exact: true }).click();
    await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).waitFor();
    await stale.locator('[name=Summary]').fill('F09 mobil kayıt');
    const mobileSave = stale.waitForResponse(r => r.url().includes('/AdminWriting/Edit/') && r.request().method() === 'POST');
    await stale.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true }).click();
    check('mobile_save_reopens_committed_metadata', (await mobileSave).status() === 302);
    await stale.waitForLoadState(); await stale.getByRole('textbox', { name: 'Yazı metni', exact: true }).waitFor();
    check('mobile_saved_summary_survives_reload', await stale.locator('[name=Summary]').inputValue() === 'F09 mobil kayıt');
    await page.goto(base + '/AdminWriting/Create');
    await editor.waitFor();
    await title.fill('F09 hatada korunacak');
    await editor.fill('F09 kategori hatasında korunacak metin');
    // Select is deliberately invalid but submission is sent to exercise the server, rather than native validation.
    await page.locator('[name=CategoryId]').evaluate(e => { const o = new Option('Geçersiz', '999999'); e.add(o); e.value = '999999'; });
    const invalidResponse = page.waitForResponse(r => r.url().includes('/AdminWriting/Create') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click();
    const invalid = await invalidResponse; await page.waitForLoadState(); await editor.waitFor();
    check('400_retains_document_and_title_in_working_editor', invalid.status() === 400 && await title.inputValue() === 'F09 hatada korunacak' && (await editor.innerText()).includes('kategori hatasında'));
    check('400_enforces_strict_csp', (await invalid.allHeaders())['content-security-policy'].includes("style-src-attr 'none'"));
    await page.goto(url); await editor.waitFor();
    // Seed a complete server-validated basic document through the actual HTTP form, then edit and reopen in Chrome.
    const http = JSON.parse(fs.readFileSync(process.env.DEVCORE_WRITING_HTTP_REPORT, 'utf8'));
    await page.goto(base + '/AdminWriting/Edit/' + http.fixturePostId); await editor.waitFor();
    const complexBefore = JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document;
    await title.fill('F09 bütün temel biçimler');
    await page.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true }).click();
    await page.waitForLoadState(); await editor.waitFor();
    const complexAfter = JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document;
    check('all_basic_nodes_marks_attributes_round_trip_without_loss', JSON.stringify(canonical(complexBefore)) === JSON.stringify(canonical(complexAfter)));
    check('literal_script_text_stays_text', await page.locator('[data-editor-mount] script').count() === 0 && (await editor.innerText()).includes('<script> metin'));
    violations.push(...await page.evaluate(() => window.__writingViolations || []), ...await stale.evaluate(() => window.__writingViolations || []));
    await page.goto(base + '/AdminWriting/Create'); await editor.waitFor();
    const unsupported = JSON.stringify({ version: 1, document: { type: 'doc', content: [
        { type: 'image', attrs: { src: 'https://example.test/synthetic.png', alt: 'Korunan görsel' } }
    ] } });
    const unsupportedResponse = await context.request.post(base + '/AdminWriting/Create', { form: {
        Title: 'F09 henüz desteklenmeyen araç', DocumentJson: unsupported, CategoryId: '1001', IsActive: 'true',
        SaveAction: 'SaveDraft', PublishDate: await page.locator('[name=PublishDate]').inputValue(),
        __RequestVerificationToken: await page.locator('#postForm [name=__RequestVerificationToken]').inputValue()
    } });
    await page.goto(unsupportedResponse.url());
    await page.getByText('Editör güvenle açılamadı.', { exact: false }).waitFor();
    check('unsupported_stored_node_is_retained_and_saving_fails_closed', await page.locator('[name=DocumentJson]').inputValue() === unsupported &&
        await page.locator('[data-writing-save]:not([disabled])').count() === 0 && await page.locator('[data-editor-mount] .ProseMirror').count() === 0);
    await context.route('**/generated/tiptap/chunks/**', r => r.abort());
    await page.goto(base + '/AdminWriting/Create');
    await page.getByText('Editör güvenle açılamadı.', { exact: false }).waitFor();
    check('dependency_load_failure_keeps_document_and_disables_every_save', await page.locator('[data-document-fallback]').isVisible() && await page.locator('[data-writing-save]:not([disabled])').count() === 0 && (await page.locator('[name=DocumentJson]').inputValue()).includes('"version":1'));
    check('legacy_recovery_is_not_loaded_changed_or_deleted', await page.evaluate(key => localStorage.getItem(key), recoveryKey) === recoveryValue);
    await context.unroute('**/generated/tiptap/chunks/**');
    await page.goto(base + '/AdminPost/Create');
    const legacyEditor = page.locator('#editor .ProseMirror[contenteditable=true]').first();
    await legacyEditor.waitFor({ state: 'visible' });
    check('legacy_markdown_create_still_loads_its_existing_editor', await page.locator('#Content').count() === 1 &&
        await page.locator('[data-document-form]').count() === 0);
    await page.locator('#post-title-input').fill('F09 korunan legacy form');
    await page.locator('[name=CategoryId]').selectOption('1001');
    await legacyEditor.fill('F09 legacy Markdown hâlâ korunur.');
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await page.waitForURL('**/AdminPost');
    check('legacy_native_save_keeps_its_existing_endpoint', await page.getByRole('link', { name: 'F09 korunan legacy form', exact: true }).count() === 1);
    check('no_runtime_or_console_errors', errors.length === 0);
    check('no_csp_violations', violations.length === 0);
})().catch(e => { checks.completed = false; console.error(e.name + ': verification interrupted; inspect boolean checks.'); process.exitCode = 1; })
.finally(async () => {
    if (browser) await browser.close();
    fs.writeFileSync(path.join(report, 'writing-browser.json'), JSON.stringify({ count: Object.keys(checks).length, checks, errors, violations, scope: 'Real Chrome keyboard/toolbar/MVC round trip; disposable PostgreSQL. No media provider or JSON recovery claim.' }, null, 2) + '\n');
    console.log(JSON.stringify({ count: Object.keys(checks).length, failed: Object.keys(checks).filter(k => !checks[k]) }));
});
