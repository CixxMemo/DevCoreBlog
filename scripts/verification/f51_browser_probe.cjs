// Real Chrome acceptance against the disposable F17/F30 fixture; no browser state is saved.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_F51_BASE_URL || 'http://127.0.0.1:15192';
assert.equal(new URL(base).hostname, '127.0.0.1', 'Only the owned loopback fixture is supported');
const output = process.env.DEVCORE_F51_REPORT_DIR || '/tmp/devcoreblog-f51-browser';
fs.mkdirSync(output, { recursive: true });
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
const checks = {}, visits = [], externalResources = new Set(), errors = [], consoleErrors = [], networkFailures = [], failedAssets = [];
let browser;
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'] });
    await context.addInitScript(() => {
        window.f51Violations = [];
        document.addEventListener('securitypolicyviolation', event => {
            let blocked = event.blockedURI;
            try { blocked = new URL(blocked).origin; } catch { /* inline/eval are already safe labels */ }
            window.f51Violations.push({ directive: event.effectiveDirective, blocked, disposition: event.disposition });
        });
    });
    // The fixture provider emits a synthetic HTTPS image URL; this is not a Cloudinary account test.
    await context.route('https://images.example.test/**', route => route.fulfill({ status: 200, contentType: 'image/png', body: png }));
    context.on('request', request => {
        const url = new URL(request.url());
        if (url.origin !== new URL(base).origin && request.frame().parentFrame() === null)
            externalResources.add(`${request.resourceType()} ${url.origin}`);
    });
    context.on('requestfailed', request => {
        const url = new URL(request.url());
        networkFailures.push({ origin: url.origin, type: request.resourceType(), failure: request.failure()?.errorText });
    });
    context.on('response', response => {
        if (response.status() >= 400 && response.request().resourceType() !== 'document')
            failedAssets.push({ origin: new URL(response.url()).origin, path: new URL(response.url()).pathname, status: response.status(), type: response.request().resourceType() });
    });
    context.on('page', page => {
        page.on('pageerror', error => errors.push(error.name));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().includes('Failed to load resource:'))
                consoleErrors.push(message.text().slice(0, 180));
        });
    });
    const page = await context.newPage();
    async function audit(target = page, label = new URL(target.url()).pathname) {
        await target.waitForTimeout(250);
        const state = await target.evaluate(() => ({
            violations: window.f51Violations,
            inlineExecutableScripts: [...document.scripts].filter(s => !s.src && (!s.type || s.type === 'module')).length,
            inlineHandlers: [...document.querySelectorAll('*')].filter(e => [...e.attributes].some(a => /^on[a-z]+$/i.test(a.name))).length,
            inlineStyleElements: document.querySelectorAll('style').length,
            xssExecuted: !!(window.__f03ToastXss || window.__f04RawHtmlXss || window.__f04AttributeXss || window.__f51Xss),
        }));
        visits.push({ label, ...state });
        assert.deepEqual(state.violations, [], `CSP violations on ${label}`);
        assert.equal(state.inlineExecutableScripts, 0, label);
        assert.equal(state.inlineHandlers, 0, label);
        assert.equal(state.inlineStyleElements, 0, label);
        assert.equal(state.xssExecuted, false, label);
    }
    async function visit(route, status = 200) {
        const response = await page.goto(base + route, { waitUntil: 'domcontentloaded' });
        assert.equal(response.status(), status, route);
        const policy = response.headers()['content-security-policy-report-only'];
        assert.ok(policy?.includes("script-src 'self'; script-src-attr 'none'"), route);
        assert.equal(response.headers()['content-security-policy'], undefined, 'F51 must not enforce');
        assert.equal(policy.includes('unsafe-eval'), false);
        assert.equal(policy.includes('*'), false);
        const editor = /^\/AdminPost\/(Create|Edit)/.test(route);
        assert.ok(policy.includes(editor ? "style-src-attr 'unsafe-inline'" : "style-src-attr 'none'"));
        await audit(page, route);
        return response;
    }
    for (const route of ['/', '/category/f01-active', '/ara?query=F01', '/about', '/contact', '/Account/Login']) await visit(route);
    await visit('/does-not-exist', 404);
    await visit('/');
    const darkBefore = await page.locator('html').evaluate(e => e.classList.contains('dark'));
    await page.locator('#theme-toggle-btn').click();
    checks.theme_toggle = await page.locator('html').evaluate(e => e.classList.contains('dark')) !== darkBefore;
    await page.reload({ waitUntil: 'domcontentloaded' });
    checks.theme_survives_reload = await page.locator('html').evaluate(e => e.classList.contains('dark')) !== darkBefore;
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#mobile-menu-btn').click();
    await page.keyboard.press('Escape');
    checks.public_mobile_keyboard_focus = await page.locator('#mobile-menu-btn').evaluate(e => e === document.activeElement && e.getAttribute('aria-expanded') === 'false');
    checks.public_mobile_no_overflow = await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
    await audit(page, 'public mobile theme/drawer');
    await page.screenshot({ path: path.join(output, 'public-mobile.png') });
    await page.setViewportSize({ width: 1440, height: 1000 });
    await visit('/post/f01-markdown-xss');
    checks.json_ld_is_inert_and_valid = await page.locator('script[type="application/ld+json"]').evaluate(e => JSON.parse(e.textContent)['@type'] === 'BlogPosting');
    checks.prism_highlights_code = await page.locator('.markdown-content code .token').count() > 0;
    await page.getByRole('button', { name: 'Copy code block 1', exact: true }).click();
    await page.getByText('Copied.', { exact: true }).waitFor();
    checks.code_copy = await page.getByText('Copied.', { exact: true }).count() === 1;
    const iframe = page.locator('iframe[src^="https://www.youtube-nocookie.com/embed/"]');
    checks.youtube_embed_origin = await iframe.count() === 1;
    await iframe.scrollIntoViewIfNeeded();
    await page.waitForTimeout(1200);
    checks.article_no_script_execution = !await page.evaluate(() => window.__f04RawHtmlXss || window.__f04AttributeXss);
    await audit(page, 'article code/YouTube');
    await page.screenshot({ path: path.join(output, 'article-desktop.png') });
    await visit('/Account/Login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill(process.env.DEVCORE_TEST_ADMIN_PASSWORD || 'f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard', { waitUntil: 'domcontentloaded' });
    await audit(page, 'login/dashboard');
    await page.locator('#dashboard-scratchpad').fill('F51 synthetic scratchpad');
    await page.reload({ waitUntil: 'domcontentloaded' });
    checks.scratchpad_survives_reload = await page.locator('#dashboard-scratchpad').inputValue() === 'F51 synthetic scratchpad';
    await page.locator('#scratchpad-create-draft-btn').click();
    await page.waitForURL('**/AdminPost/Create', { waitUntil: 'domcontentloaded' });
    await page.locator('#editor .ProseMirror[contenteditable=true]').first().waitFor({ state: 'visible' });
    await page.locator('#btn-restore-draft').waitFor({ state: 'visible' });
    checks.scratchpad_does_not_overwrite_without_restore = await page.locator('#Content').inputValue() === '';
    await page.locator('#btn-restore-draft').click();
    await page.waitForFunction(() => document.getElementById('Content').value === 'F51 synthetic scratchpad');
    checks.scratchpad_transfer = await page.locator('#Content').inputValue() === 'F51 synthetic scratchpad';
    await audit(page, 'scratchpad transfer/editor');
    await visit('/Admin/Automations');
    await page.locator('[data-copy-snippet]').first().click();
    await page.getByText('COPIED!', { exact: true }).waitFor();
    checks.automation_copy = await page.getByText('COPIED!', { exact: true }).count() === 1;
    await audit(page, 'automation clipboard');
    await visit('/AdminCategory/Create');
    const categoryName = 'F51 \' "</script><img src=x onerror=window.__f51Xss=true>';
    await page.locator('#category-name-input').fill(categoryName);
    checks.category_slug_preview = (await page.locator('#slug-preview').textContent()).includes('f51');
    await page.getByRole('button', { name: /save category/i }).click();
    await page.waitForURL('**/AdminCategory', { waitUntil: 'domcontentloaded' });
    await audit(page, 'category saved/toast');
    checks.category_name_is_inert_data = !await page.evaluate(() => window.__f51Xss);
    const formIndex = await page.locator('form[data-confirm-category]').evaluateAll((forms, name) => forms.findIndex(f => f.dataset.confirmCategory === name), categoryName);
    assert.ok(formIndex >= 0);
    const ownDelete = page.locator('form[data-confirm-category]').nth(formIndex).getByRole('button', { name: 'DEL', exact: true });
    let dialogMessage;
    page.once('dialog', async dialog => { dialogMessage = dialog.message(); await dialog.dismiss(); });
    await ownDelete.click();
    checks.category_cancel_keeps_encoded_name = dialogMessage === `Are you sure you want to delete category: '${categoryName}'?` && await ownDelete.count() === 1;
    page.once('dialog', dialog => dialog.accept());
    await ownDelete.click();
    await page.waitForLoadState('domcontentloaded');
    checks.category_confirm_deletes_only_fixture = !await page.locator('form[data-confirm-category]').evaluateAll((forms, name) => forms.some(f => f.dataset.confirmCategory === name), categoryName);
    await audit(page, 'category cancel/confirm');
    // Existing linked category deletion is blocked by the real service and produces TempData.
    page.once('dialog', dialog => dialog.accept());
    await page.locator('form[data-confirm-category]').first().getByRole('button', { name: 'DEL', exact: true }).click();
    await page.waitForLoadState('domcontentloaded');
    checks.real_tempdata_notification = (await page.locator('#admin-toast-container').innerText()).includes('This category contains posts.');
    await page.evaluate(message => window.showToast(message, 'error'), categoryName);
    checks.toast_renders_html_as_text = await page.locator('#admin-toast-container img, #admin-toast-container script').count() === 0 && !await page.evaluate(() => window.__f51Xss);
    await audit(page, 'real TempData and hostile toast text');

    await visit('/AdminPost/Create');
    await page.locator('#editor .ProseMirror[contenteditable=true]').first().waitFor({ state: 'visible' });
    await page.locator('#post-title-input').fill('F51 synthetic editor');
    await page.locator('#CategoryId').selectOption('1001');
    await page.locator('#editor .ProseMirror').first().fill('# F51 browser content\n\nSafe text.');
    await page.getByRole('button', { name: 'Embed Video', exact: false }).click({ trial: true });
    page.once('dialog', dialog => dialog.accept('https://www.youtube.com/watch?v=dQw4w9WgXcQ'));
    await page.locator('#btn-toolbar-video').click();
    checks.editor_video_insertion = (await page.locator('#Content').inputValue()).includes('[video](https://www.youtube.com/watch?v=dQw4w9WgXcQ)');
    await page.locator('#editor-image-picker').setInputFiles({ name: 'f51.png', mimeType: 'image/png', buffer: png });
    await page.getByText('Image uploaded and inserted.', { exact: true }).waitFor();
    checks.editor_real_upload_request_fixture_storage = (await page.locator('#Content').inputValue()).includes('https://images.example.test/');
    const popupPromise = context.waitForEvent('page');
    await page.getByRole('button', { name: 'Preview in new tab', exact: true }).click();
    const preview = await popupPromise;
    await preview.waitForLoadState('domcontentloaded');
    checks.preview_is_private_unsaved = await preview.getByText('Private preview — Not saved', { exact: true }).count() === 1;
    await audit(preview, 'native POST preview/image/video');
    await preview.close();
    await audit(page, 'editor image/video insertion');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#admin-menu-toggle').click();
    await page.keyboard.press('Escape');
    checks.admin_mobile_keyboard_focus = await page.locator('#admin-menu-toggle').evaluate(e => e === document.activeElement && e.getAttribute('aria-expanded') === 'false');
    checks.admin_mobile_no_overflow = await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
    await audit(page, 'mobile editor/drawer');
    await page.screenshot({ path: path.join(output, 'editor-mobile.png') });
    await page.setViewportSize({ width: 1440, height: 1000 });
    await visit('/AdminPost/Edit/2005');
    await page.locator('#editor .ProseMirror[contenteditable=true]').first().waitFor({ state: 'visible' });
    checks.edit_rich_editor_works = await page.locator('#editor').isVisible() && await page.locator('#content-fallback').isHidden();
    await page.screenshot({ path: path.join(output, 'editor-desktop.png') });
    await visit('/AdminPost');
    await page.getByRole('button', { name: 'Sign Out', exact: true }).click();
    await page.waitForURL(base + '/', { waitUntil: 'domcontentloaded' });
    await audit(page, 'antiforgery logout');
    checks.logout_returns_home = new URL(page.url()).pathname === '/';
    const probe = await context.newPage();
    await probe.goto(base + '/', { waitUntil: 'domcontentloaded' });
    await probe.evaluate(() => { const script = document.createElement('script'); script.textContent = 'window.f51ReportOnlyProbeRan = true'; document.body.append(script); });
    await probe.waitForTimeout(200);
    const observation = await probe.evaluate(() => ({ ran: window.f51ReportOnlyProbeRan === true, violations: window.f51Violations }));
    checks.report_only_observes_but_does_not_block = observation.ran && observation.violations.some(v => v.directive === 'script-src-elem' && v.disposition === 'report');
    await probe.close();
    checks.no_browser_runtime_errors = errors.length === 0;
    checks.no_unexplained_console_errors = consoleErrors.length === 0;
    const missingXssProbeImage = asset => asset.origin === base && asset.path === '/AdminPost/Edit/x' && asset.status === 404 && asset.type === 'image';
    checks.expected_xss_probe_image_is_missing = failedAssets.some(missingXssProbeImage);
    checks.no_unexpected_asset_failures = failedAssets.every(missingXssProbeImage);
    checks.no_local_asset_network_failures = !networkFailures.some(f => f.origin === base && f.type !== 'document' && f.failure !== 'net::ERR_ABORTED');
    const result = { checks, visits, externalResources: [...externalResources], errors, consoleErrors, networkFailures, failedAssets, deliberateReportOnlyObservation: observation,
        scope: 'Real Chrome, disposable PostgreSQL, synthetic storage/HTTPS image response; no Cloudinary upload or public deployment' };
    fs.writeFileSync(path.join(output, 'browser.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify({ checks, normalVisits: visits.length, externalResources: [...externalResources], errors, consoleErrors, networkFailures, failedAssets }, null, 2));
    assert.ok(Object.values(checks).every(Boolean));
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => { if (browser) await browser.close(); });
