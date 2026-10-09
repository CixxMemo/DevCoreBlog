// Exercise public navigation and keyboard admin login on the owned synthetic fixture.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_F01_BASE_URL;
const output = process.env.DEVCORE_F01_BROWSER_REPORT_DIR;
const login = process.env.DEVCORE_TEST_ADMIN_LOGIN_PATH || '/fixture-admin/login';
assert.equal(new URL(base).hostname, '127.0.0.1');
assert.equal(login, '/fixture-admin/login', 'Use only the explicit synthetic fixture');
assert.ok(output && !fs.existsSync(path.join(output, 'admin-browser.json')));
fs.mkdirSync(output, { recursive: true });
const checks = {}, errors = [], violations = [];
let browser;
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e =>
        window.__f01Violations = [...(window.__f01Violations || []), e.effectiveDirective]));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', message => {
        if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) errors.push('console-error');
    });
    function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
    async function audit() { violations.push(...await page.evaluate(() => window.__f01Violations || [])); }
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
        await page.goto(base + '/');
        const reader = page.locator('#reader-sign-in-link');
        check('reader_link_visible_' + width, await reader.isVisible() && await reader.getAttribute('href') === '/sign-in');
        await reader.click();
        check('reader_navigation_is_separate_' + width, new URL(page.url()).pathname === '/sign-in'
            && !await page.locator('#username').count() && !(await page.content()).includes(login));
        await audit();
        await page.goto(base + login);
        check('configured_form_' + width, await page.locator('form[method=post]').getAttribute('action') === login);
        check('login_no_overflow_' + width, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        await page.screenshot({ path: path.join(output, 'admin-login-' + width + '.png') });
        await audit();
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.locator('#username').focus();
    await page.keyboard.type('f17-admin');
    await page.keyboard.press('Tab');
    check('keyboard_password_focus', await page.locator('#password').evaluate(e => e === document.activeElement));
    await page.keyboard.type('f17-isolated-password');
    await page.keyboard.press('Tab');
    check('keyboard_submit_focus', await page.getByRole('button', { name: 'Sign In', exact: true }).evaluate(e => e === document.activeElement));
    await page.keyboard.press('Enter');
    await page.waitForURL('**/Admin/Dashboard');
    check('keyboard_login_reaches_dashboard', await page.locator('#dashboard-scratchpad').isVisible());
    await audit();
    await page.getByRole('button', { name: 'Sign Out', exact: true }).click();
    await page.waitForURL(base + '/');
    const denied = await page.goto(base + '/Admin/Dashboard');
    check('logout_denies_without_disclosing_login', denied.status() === 404
        && (await denied.allHeaders())['cache-control'].includes('no-store')
        && !(await denied.allHeaders()).location && !(await page.content()).includes(login));
    await audit();
    check('no_runtime_or_console_errors', errors.length === 0);
    check('no_csp_violations', violations.length === 0);
})().catch(e => { console.error(e.message); process.exitCode = 1; }).finally(async () => {
    fs.writeFileSync(path.join(output, 'admin-browser.json'), JSON.stringify({ checks, errors, violations,
        scope: 'Real Chrome desktop/mobile/keyboard, synthetic admin settings and disposable PostgreSQL.' }, null, 2) + '\n');
    if (browser) await browser.close();
});
