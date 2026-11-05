// AY02 real Chrome acceptance on the unmodified build: no subscription trace, working keyboard, clean console, no overflow.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');

const base = process.env.DEVCORE_SUSPENSION_BASE_URL || 'http://127.0.0.1:15205';
assert.equal(new URL(base).hostname, '127.0.0.1');
const output = process.env.DEVCORE_SUSPENSION_REPORT_DIR;
assert.ok(output, 'An evidence directory is required');
fs.mkdirSync(output, { recursive: true });

const publicPages = ['/', '/?kind=news', '/about', '/contact', '/post/f01-visible'];
const closedPages = ['/abone-ol', '/sign-in', '/abonelik/dogrula', '/abonelik/giris'];
const traces = ['abone-ol', '/sign-in', 'registration-form', 'newsletter-demo', 'Bültene abone', 'Okuyucu girişi'];
const viewports = { desktop: { width: 1440, height: 1000 }, mobile: { width: 390, height: 844 } };
const checks = {}, errors = [], consoleErrors = [], violations = [];

function check(name, passed) {
    checks[name] = !!passed;
    assert.ok(passed, name);
}

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    for (const [label, viewport] of Object.entries(viewports)) {
        const context = await browser.newContext({ viewport });
        await context.addInitScript(() => document.addEventListener('securitypolicyviolation', event =>
            window.__violations = [...(window.__violations || []), event.effectiveDirective]));
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.name));
        page.on('console', message => {
            // A 410 document is the intended answer; Chrome logs every 4xx document as a failed resource.
            if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) consoleErrors.push(message.text().slice(0, 160));
        });
        async function audit(route) {
            violations.push(...await page.evaluate(() => window.__violations || []));
            check(`${label}_no_overflow_${route}`, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        }

        for (const route of publicPages) {
            const response = await page.goto(base + route, { waitUntil: 'domcontentloaded' });
            check(`${label}_public_ok_${route}`, response.status() === 200);
            const html = await page.content();
            check(`${label}_no_trace_${route}`, traces.every(trace => !html.includes(trace)));
            await audit(route);
        }

        const home = await page.goto(base + '/', { waitUntil: 'domcontentloaded' });
        check(`${label}_home_has_content_discovery`, home.status() === 200 && await page.locator('#home-heading').isVisible() && await page.locator('nav[aria-labelledby="discovery-heading"] a').count() === 3);
        check(`${label}_home_has_no_form_posting`, await page.locator('form[method="post"], input[type="email"]').count() === 0);
        await page.screenshot({ path: path.join(output, `${label}-home.png`) });

        if (label === 'mobile') {
            await page.locator('#mobile-menu-btn').click();
            const links = await page.locator('nav[aria-label="Ana gezinme"] a').allInnerTexts();
            check('mobile_menu_has_no_newsletter_entry', links.length > 0 && links.every(text => !/bülten|abone|okuyucu/i.test(text)));
            await page.screenshot({ path: path.join(output, 'mobile-menu.png') });
            await page.keyboard.press('Escape');
            check('mobile_menu_escape_returns_focus', await page.locator('#mobile-menu-btn').evaluate(element => element === document.activeElement && element.getAttribute('aria-expanded') === 'false'));
        } else {
            const links = await page.locator('nav[aria-label="Ana gezinme"] a').allInnerTexts();
            check('desktop_menu_has_no_newsletter_entry', links.length > 0 && links.every(text => !/bülten|abone|okuyucu/i.test(text)));
            // Forward Tab reaches real controls, each with a visible focus style.
            await page.goto(base + '/', { waitUntil: 'domcontentloaded' });
            const seen = [];
            for (let index = 0; index < 12; index++) {
                await page.keyboard.press('Tab');
                seen.push(await page.evaluate(() => {
                    const element = document.activeElement, style = getComputedStyle(element);
                    return { tag: element.tagName, visible: style.outlineStyle !== 'none' || style.boxShadow !== 'none' || style.textDecorationLine !== 'none' || style.borderStyle !== 'none' };
                }));
            }
            check('desktop_keyboard_reaches_controls_with_visible_focus', seen.every(entry => entry.tag !== 'BODY' && entry.visible));
        }

        for (const route of closedPages) {
            const response = await page.goto(base + route, { waitUntil: 'domcontentloaded' });
            check(`${label}_closed_410_${route}`, response.status() === 410);
            check(`${label}_closed_is_clear_${route}`, await page.getByRole('heading', { name: 'Bülten ve abonelik kapatıldı' }).isVisible());
            check(`${label}_closed_collects_nothing_${route}`, await page.locator('form, input, textarea').count() === 0);
            await audit(route);
        }
        await page.screenshot({ path: path.join(output, `${label}-closed.png`) });
        await page.keyboard.press('Tab');
        check(`${label}_closed_keyboard_reaches_home_link`, await page.evaluate(() => document.activeElement.tagName === 'A' && document.activeElement.getAttribute('href') === '/'));
        await context.close();
    }
    check('no_runtime_errors', errors.length === 0);
    check('no_console_errors', consoleErrors.length === 0);
    check('no_csp_violations', violations.length === 0);
    await browser.close();
})().catch(error => { console.error(error.message.slice(0, 300)); process.exitCode = 1; }).finally(() => {
    fs.writeFileSync(path.join(output, 'browser.json'), JSON.stringify({ passed: Object.values(checks).every(Boolean) && process.exitCode !== 1, count: Object.keys(checks).length, checks, errors, consoleErrors, violations,
        scope: 'Real Chrome against the unmodified build on a disposable fixture.' }, null, 2) + '\n');
    process.exit();
});
