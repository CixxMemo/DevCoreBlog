// Verify the real legacy Edit409 response on an explicitly owned synthetic fixture.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL;
const report = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
fs.mkdirSync(report, { recursive: true });
assert.ok(!fs.existsSync(path.join(report, 'persistence-browser.json')));
const checks = {}, errors = [], violations = [];
let browser;
function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await context.route('https://fonts.googleapis.com/**', r => r.fulfill({ contentType: 'text/css', body: '' }));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => {
        window.__f08Violations = [...(window.__f08Violations || []), e.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', m => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource:')) errors.push('console-error'); });
    await page.goto(base + '/fixture-admin/login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard');
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 1000 });
        await page.goto(base + '/AdminPost/Edit/2003');
        await page.locator('#editor .ProseMirror[contenteditable=true]').first().waitFor({ state: 'visible' });
        const title = 'F08 korunacak başlık ' + width;
        const body = 'F08 korunacak Türkçe metin 👋 ' + width;
        await page.locator('#post-title-input').fill(title);
        await page.locator('#editor .ProseMirror').first().fill(body);
        const [response] = await Promise.all([
            page.waitForResponse(r => r.url().includes('/AdminPost/Edit/2003') && r.request().method() === 'POST'),
            page.locator('button[name="SaveAction"][value="Save"]').click()
        ]);
        await page.waitForLoadState();
        const headers = await response.allHeaders();
        check('real_409_private_' + width, response.status() === 409 && headers['cache-control'].includes('no-store'));
        check('title_and_body_retained_' + width, await page.locator('#post-title-input').inputValue() === title && (await page.locator('#Content').inputValue()).includes(body));
        check('turkish_reason_visible_' + width, (await page.locator('[data-valmsg-summary]').innerText()).includes('JSON belge'));
        check('enforcing_csp_preserved_' + width, headers['content-security-policy'].includes("style-src-attr 'none'") && !headers['content-security-policy'].includes('unsafe-eval'));
        const reload = page.getByRole('link', { name: 'Reload current post', exact: true });
        await reload.focus();
        await page.keyboard.press('Shift+Tab');
        await page.keyboard.press('Tab');
        check('keyboard_reload_has_focus_' + width, await reload.evaluate(e => e === document.activeElement && getComputedStyle(e).outlineStyle !== 'none'));
        check('no_page_horizontal_overflow_' + width, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        check('unsaved_recovery_is_retained_' + width, await page.evaluate(() => !!localStorage.getItem('devcore_editor_draft_edit_2003')));
        check('strict_error_uses_visible_textarea_' + width, await page.locator('#Content').isVisible()
            && await page.locator('#editor .ProseMirror').count() === 0);
        await page.waitForTimeout(200);
        violations.push(...await page.evaluate(() => window.__f08Violations || []));
        await page.screenshot({ path: path.join(report, 'legacy-conflict-' + width + '.png'), fullPage: true });
    }
    check('no_runtime_or_console_errors', errors.length === 0);
    check('no_csp_violations', violations.length === 0);
})().catch(e => { checks.completed = false; console.error(e.name + ': ' + e.message); process.exitCode = 1; })
.finally(async () => {
    if (browser) await browser.close();
    fs.writeFileSync(path.join(report, 'persistence-browser.json'), JSON.stringify({ count: Object.keys(checks).length, checks, errors, violations, scope: 'Owned synthetic fixture. Real legacy Edit409 only; no JSON editor or recovery implementation claimed.' }, null, 2) + '\n');
    console.log(JSON.stringify({ count: Object.keys(checks).length, failed: Object.keys(checks).filter(k => !checks[k]) }));
});
