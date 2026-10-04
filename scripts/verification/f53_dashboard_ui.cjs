// Verify safe operational labels and keyboard/mobile access on the owned MVC fixture.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_F53_BASE_URL || 'http://127.0.0.1:15195';
assert.equal(new URL(base).hostname, '127.0.0.1');
const output = process.env.DEVCORE_F53_REPORT_DIR || '/tmp/devcoreblog-f53-dashboard';
fs.mkdirSync(output, { recursive: true });
(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
        await context.addInitScript(() => {
            window.f53Violations = [];
            document.addEventListener('securitypolicyviolation', event => window.f53Violations.push(event.effectiveDirective));
        });
        const page = await context.newPage();
        const errors = []; page.on('pageerror', error => errors.push(error.name));
        await page.goto(base + '/Account/Login');
        await page.locator('#username').fill(process.env.DEVCORE_TEST_ADMIN_USERNAME || 'f53-admin');
        await page.locator('#password').fill(process.env.DEVCORE_TEST_ADMIN_PASSWORD || 'f53-isolated-password');
        await page.getByRole('button', { name: 'Sign In', exact: true }).click();
        await page.waitForURL('**/Admin/Dashboard');
        const status = page.locator('section[aria-labelledby="service-status-title"]');
        const checks = {
            database_verified: (await page.locator('#database-status').innerText()) === 'Verified (PostgreSQL)',
            credentials_are_not_availability: (await page.locator('#media-status').innerText()).includes('access not checked'),
            completed_upload_is_historical: (await status.innerText()).includes('this process only'),
            no_hardcoded_active_badge: !(await status.innerText()).includes('(Active)'),
        };
        await status.screenshot({ path: path.join(output, 'status-desktop.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#admin-menu-toggle').click(); await page.keyboard.press('Escape'); await page.waitForTimeout(250);
        checks.drawer_returns_focus = await page.locator('#admin-menu-toggle').evaluate(e => e === document.activeElement && e.getAttribute('aria-expanded') === 'false');
        checks.mobile_has_no_horizontal_overflow = await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
        await status.scrollIntoViewIfNeeded();
        await status.screenshot({ path: path.join(output, 'status-mobile.png') });
        checks.no_csp_violation = (await page.evaluate(() => window.f53Violations)).length === 0;
        checks.no_runtime_error = errors.length === 0;
        if (process.env.DEVCORE_F53_OUTAGE_MARKER) {
            console.log('F53_DASHBOARD_READY');
            const until = Date.now() + 120000;
            while (!fs.existsSync(process.env.DEVCORE_F53_OUTAGE_MARKER) && Date.now() < until)
                await new Promise(resolve => setTimeout(resolve, 250));
            assert.ok(fs.existsSync(process.env.DEVCORE_F53_OUTAGE_MARKER), 'Owned outage marker not received');
            const response = await page.goto(base + '/Admin/Dashboard');
            checks.actual_outage_page_keeps_503 = response.status() === 503;
            checks.outage_has_no_fabricated_statistics = (await page.locator('main').innerText()).includes('Statistics unavailable') && await page.getByText('Total Posts', { exact: true }).count() === 0;
            checks.outage_shows_unavailable_database = (await page.locator('#database-status').innerText()).includes('Unavailable');
            checks.outage_csp_and_runtime_remain_valid = (await page.evaluate(() => window.f53Violations)).length === 0 && errors.length === 0;
            await page.screenshot({ path: path.join(output, 'dashboard-outage.png'), fullPage: true });
        }
        fs.writeFileSync(path.join(output, 'dashboard.json'), JSON.stringify({ checks, count: Object.keys(checks).length, errors }, null, 2));
        assert.ok(Object.values(checks).every(Boolean));
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
