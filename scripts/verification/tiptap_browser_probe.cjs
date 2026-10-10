// Exercise only a synthetic loopback application; never receive real admin settings.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_TIPTAP_BASE_URL;
const output = process.env.DEVCORE_TIPTAP_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
assert.ok(output && !fs.existsSync(path.join(output, 'tiptap-browser.json')));
fs.mkdirSync(output, { recursive: true });
const checks = {}, errors = [], violations = [], scriptOrigins = new Set();
let browser;
function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', event => {
        window.__tiptapViolations = [...(window.__tiptapViolations || []), event.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.name));
    page.on('console', message => {
        if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) errors.push('console-error');
    });
    page.on('request', request => {
        if (request.resourceType() === 'script') scriptOrigins.add(new URL(request.url()).origin);
    });
    async function audit() { violations.push(...await page.evaluate(() => window.__tiptapViolations || [])); }
    const denied = await context.request.get(base + '/AdminPost/EditorTrial');
    check('anonymous_trial_denied_private', denied.status() === 404 && denied.headers()['cache-control'].includes('no-store') && !denied.headers().location);
    await page.goto(base + '/fixture-admin/login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard');
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
        const response = await page.goto(base + '/AdminPost/EditorTrial');
        const headers = await response.allHeaders();
        check('authenticated_private_response_' + width, response.status() === 200 && headers['cache-control'].includes('no-store') && headers['x-robots-tag'].includes('noindex'));
        check('unchanged_strict_csp_' + width, headers['content-security-policy'].includes("script-src 'self'") && headers['content-security-policy'].includes("style-src-attr 'none'") && !/unsafe-inline|unsafe-eval|nonce-/.test(headers['content-security-policy']));
        await page.getByText('Editör hazır. Bu metin kaydedilmez.', { exact: true }).waitFor();
        const editor = page.getByRole('textbox', { name: 'Deneme metni' });
        check('accessible_editor_started_' + width, await editor.isVisible() && await editor.getAttribute('contenteditable') === 'true' && await editor.getAttribute('lang') === 'tr');
        const writes = [];
        const observe = request => { if (!['GET', 'HEAD'].includes(request.method())) writes.push(request.method()); };
        page.on('request', observe);
        await editor.focus();
        await page.keyboard.type('Türkçe deneme: ı İ ş ğ ü ö ç');
        check('turkish_keyboard_input_' + width, (await editor.innerText()).includes('ı İ ş ğ ü ö ç'));
        await page.keyboard.press('ControlOrMeta+a');
        await page.getByRole('button', { name: 'Kalın', exact: true }).click();
        check('bold_mark_and_pressed_state_' + width, await editor.locator('strong').count() > 0 && await page.getByRole('button', { name: 'Kalın', exact: true }).getAttribute('aria-pressed') === 'true');
        await page.getByRole('button', { name: 'Geri al', exact: true }).click();
        check('undo_format_' + width, await editor.locator('strong').count() === 0);
        await page.getByRole('button', { name: 'Yinele', exact: true }).click();
        check('redo_format_' + width, await editor.locator('strong').count() > 0);
        await page.getByRole('button', { name: 'Kalın', exact: true }).focus();
        await page.keyboard.press('Tab');
        check('keyboard_toolbar_focus_' + width, await page.getByRole('button', { name: 'Geri al', exact: true }).evaluate(element => element === document.activeElement));
        await editor.focus();
        await page.keyboard.press('ArrowRight');
        await page.keyboard.type(' uzunmetin'.repeat(35));
        check('no_mobile_or_long_text_overflow_' + width, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        check('no_injected_style_or_inline_script_' + width, await page.locator('style[data-tiptap-style]').count() === 0 && await page.locator('script:not([src]):not([type="application/ld+json"])').count() === 0 && await editor.getAttribute('style') === null);
        check('no_save_request_or_storage_' + width, writes.length === 0 && await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0));
        page.off('request', observe);
        await page.screenshot({ path: path.join(output, 'tiptap-' + width + '.png') });
        await audit();
        await page.reload();
        await page.getByText('Editör hazır. Bu metin kaydedilmez.', { exact: true }).waitFor();
        check('trial_does_not_persist_text_' + width, (await editor.innerText()).trim() === '');
        await audit();
    }
    check('all_scripts_local', [...scriptOrigins].every(origin => origin === new URL(base).origin));
    await page.goto(base + '/AdminPost/Create');
    await page.locator('#editor .ProseMirror[contenteditable=true]').first().waitFor({ state: 'visible' });
    check('toastui_create_remains_active', await page.locator('#Content').count() === 1 && await page.locator('[data-tiptap-trial]').count() === 0);
    await audit();
    await page.route('**/generated/tiptap/chunks/**', route => route.abort());
    await page.goto(base + '/AdminPost/EditorTrial');
    await page.getByText('Editör açılamadı. Sayfayı yeniden yükleyip tekrar deneyin.', { exact: true }).waitFor();
    check('chunk_load_failure_visible_and_controls_disabled', await page.locator('[data-editor-command]:enabled').count() === 0);
    await audit();
    check('no_runtime_or_unexplained_console_errors', errors.length === 0);
    check('no_csp_violations', violations.length === 0);
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => {
    fs.writeFileSync(path.join(output, 'tiptap-browser.json'), JSON.stringify({ checks, errors, violations,
        scope: 'Real Chrome desktop/mobile/keyboard; owned synthetic PostgreSQL; no real dotenv or credentials.' }, null, 2) + '\n');
    if (browser) await browser.close();
});
