// Actual Chrome / owned MVC: decorative animation, user control, static fallbacks and unchanged admission.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = 'http://127.0.0.1:15239', out = process.env.F46_REPORT_DIR;
const checks = {}, errors = [], csp = [], consoleErrors = [], measurements = {};
let browser, context;
function check(name, value) { checks[name] = !!value; console.log(name + '=' + String(!!value)); assert.ok(value, name); }
async function state(page, mode) { await page.waitForFunction(v => document.querySelector('[data-newsletter-demo]').dataset.mode === v, mode); }
async function times(page) { return page.locator('#newsletter-demo-visual').evaluate(e => e.getAnimations({ subtree: true }).map(a => Number(a.currentTime))); }
async function pauseIsFrozen(page) {
    const before = await times(page); await page.waitForTimeout(350); const after = await times(page);
    return before.length === 2 && after.length === 2 && before.every((v, i) => Math.abs(v - after[i]) < 2);
}
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'tr-TR' });
    context.setDefaultTimeout(12000);
    await context.exposeBinding('__f47Csp', (_s, v) => csp.push(v));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => void window.__f47Csp(e.effectiveDirective)));
    context.on('page', p => {
        p.on('pageerror', e => errors.push(e.name));
        p.on('console', m => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource:')) consoleErrors.push(m.text()); });
    });
    await context.route('https://fonts.googleapis.com/**', r => r.fulfill({ contentType: 'text/css', body: '' }));
    const page = await context.newPage(); let postCount = 0;
    page.on('request', r => { if (r.method() === 'POST') postCount++; });
    await page.goto(base + '/', { waitUntil: 'domcontentloaded' });
    const demo = page.locator('[data-newsletter-demo]'), toggle = demo.locator('[data-demo-toggle]');
    check('single_labeled_example_and_local_versioned_assets', await demo.count() === 1 && (await demo.textContent()).includes('Gerçek bir e-posta veya yayımlanmış sayı değildir.') && await page.locator('script[src^="/js/newsletter-demo.js?v="]').count() === 1 && await page.locator('link[href^="/css/newsletter-demo.css?v="]').count() === 1);
    check('decorative_visual_has_equivalent_static_caption', await page.locator('#newsletter-demo-visual').getAttribute('aria-hidden') === 'true' && (await demo.locator('figcaption').textContent()).includes('deneyin amacı, yöntemi, sonucu') && await toggle.getAttribute('aria-controls') === 'newsletter-demo-visual');
    await demo.scrollIntoViewIfNeeded(); await state(page, 'playing');
    const bounds = await page.locator('#newsletter-demo-visual').boundingBox();
    check('native_css_timeline_starts_when_visible', (await times(page)).length === 2);
    await toggle.focus(); await page.keyboard.press('Enter'); await state(page, 'paused');
    check('keyboard_pause_really_freezes_both_animations', await pauseIsFrozen(page) && await toggle.evaluate(e => e === document.activeElement && getComputedStyle(e).outlineStyle !== 'none') && (await toggle.textContent()) === 'Animasyonu devam ettir');
    await page.keyboard.press('Space'); await state(page, 'playing');
    await page.waitForFunction(() => Number(getComputedStyle(document.querySelector('.newsletter-demo-row')).opacity) > .95 && Number(getComputedStyle(document.querySelector('.newsletter-demo-message')).opacity) < .05);
    check('mail_arrives_before_message_opens', true);
    await page.screenshot({ path: path.join(out, 'inbox-arrival-desktop.png'), fullPage: true });
    await toggle.click(); await state(page, 'paused');
    await page.evaluate(() => scrollTo(0, document.body.scrollHeight));
    await demo.scrollIntoViewIfNeeded(); await page.waitForTimeout(200);
    check('manual_pause_survives_scroll_out_and_back', await demo.getAttribute('data-mode') === 'paused' && await pauseIsFrozen(page));
    await toggle.click(); await state(page, 'playing');
    await page.waitForFunction(() => Number(getComputedStyle(document.querySelector('.newsletter-demo-message')).opacity) > .95);
    const openedText = await demo.locator('.newsletter-demo-message').textContent();
    check('opened_example_has_method_result_and_next_step', ['Amaç', 'Yöntem', 'Sonuç', 'Sen de dene', 'İşe yarayanları ve sınırlamaları not etmek.'].every(v => openedText.includes(v)));
    const after = await page.locator('#newsletter-demo-visual').boundingBox();
    check('animation_does_not_move_layout_or_focus', Math.abs(bounds.height - after.height) < 1 && Math.abs(bounds.width - after.width) < 1 && await toggle.evaluate(e => e === document.activeElement));
    await page.screenshot({ path: path.join(out, 'inbox-open-desktop.png'), fullPage: true });
    await page.waitForFunction(() => document.querySelector('[data-demo-toggle]').textContent === 'Animasyonu yeniden oynat');
    check('one_pass_finishes_without_loop_or_network_post', postCount === 0 && await demo.getAttribute('data-mode') === 'paused');
    await page.keyboard.press('Enter'); await state(page, 'playing');
    check('replay_restarts_timeline_and_keeps_control_focus', (await times(page)).every(t => t < 800) && await toggle.evaluate(e => e === document.activeElement));
    // Actual scroll visibility pauses compositor timelines, without a per-frame JS loop.
    await page.evaluate(() => scrollTo(0, document.body.scrollHeight)); await state(page, 'paused');
    check('offscreen_timeline_is_frozen', await pauseIsFrozen(page));
    await demo.scrollIntoViewIfNeeded(); await state(page, 'playing');
    check('return_to_view_resumes_without_replay', (await times(page)).every(t => t > 0 && t < 4000));
    const session = await context.newCDPSession(page); await session.send('Performance.enable');
    const metric = async () => Object.fromEntries((await session.send('Performance.getMetrics')).metrics.map(m => [m.name, m.value]));
    const beforeMetrics = await metric(); await page.waitForTimeout(700); const afterMetrics = await metric();
    measurements.observation_seconds = .7; measurements.browser_task_seconds = afterMetrics.TaskDuration - beforeMetrics.TaskDuration;
    measurements.layout_count = afterMetrics.LayoutCount - beforeMetrics.LayoutCount;
    await page.emulateMedia({ reducedMotion: 'reduce' }); await state(page, 'static');
    check('live_reduced_motion_stops_animation_without_hiding_content', await demo.locator('.newsletter-demo-message').evaluate(e => getComputedStyle(e).animationName === 'none' && Number(getComputedStyle(e).opacity) === 1) && await toggle.getAttribute('aria-disabled') === 'true');
    await toggle.focus(); await page.keyboard.press('Enter');
    check('reduced_motion_control_does_not_force_motion_or_lose_focus', await demo.getAttribute('data-mode') === 'static' && await toggle.evaluate(e => e === document.activeElement));
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await page.waitForFunction(() => document.querySelector('[data-demo-toggle]').getAttribute('aria-disabled') === 'false');
    check('preference_return_waits_for_explicit_play', await demo.getAttribute('data-mode') === 'static' && await toggle.evaluate(e => e === document.activeElement));
    await page.keyboard.press('Enter'); await state(page, 'playing');
    check('explicit_play_after_static_preference_works', (await times(page)).length === 2);
    await page.emulateMedia({ reducedMotion: 'reduce' }); await state(page, 'static');
    for (const width of [390, 320]) {
        await page.setViewportSize({ width, height: 844 }); await demo.scrollIntoViewIfNeeded();
        check('mobile_' + width + '_no_overflow_and_control_fits', await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1) && await toggle.evaluate(e => { const r = e.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth; }));
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(out, 'inbox-static-mobile.png'), fullPage: true });
    // Still a normal POST form after adding the decorative component.
    await page.getByLabel('E-posta adresiniz', { exact: true }).fill('animation-browser@example.test');
    await Promise.all([page.waitForNavigation({ waitUntil: 'domcontentloaded' }), page.getByRole('button', { name: 'Abone ol', exact: true }).click()]);
    check('existing_real_subscription_form_still_works', (await page.getByRole('status').innerText()).includes('Adresiniz başvuruya uygunsa'));
    const reduced = await browser.newContext({ reducedMotion: 'reduce', viewport: { width: 390, height: 844 } });
    const r = await reduced.newPage(); await r.route('https://fonts.googleapis.com/**', t => t.fulfill({ contentType: 'text/css', body: '' }));
    await r.goto(base + '/', { waitUntil: 'domcontentloaded' }); await r.locator('[data-newsletter-demo]').scrollIntoViewIfNeeded();
    check('initial_reduced_motion_is_static_from_first_render', await r.locator('[data-newsletter-demo]').getAttribute('data-mode') === 'static' && await r.locator('.newsletter-demo-message').evaluate(e => getComputedStyle(e).animationName === 'none' && Number(getComputedStyle(e).opacity) === 1));
    await reduced.close();
    const nojs = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 390, height: 844 } });
    const n = await nojs.newPage(); await n.route('https://fonts.googleapis.com/**', t => t.fulfill({ contentType: 'text/css', body: '' }));
    await n.goto(base + '/', { waitUntil: 'domcontentloaded' });
    check('javascript_disabled_shows_complete_static_example_without_dead_controls', await n.locator('[data-demo-controls]').isHidden() && await n.locator('.newsletter-demo-message').evaluate(e => Number(getComputedStyle(e).opacity) === 1 && getComputedStyle(e).animationName === 'none'));
    await n.getByLabel('E-posta adresiniz', { exact: true }).fill('animation-nojs@example.test');
    await Promise.all([n.waitForNavigation({ waitUntil: 'domcontentloaded' }), n.getByRole('button', { name: 'Abone ol', exact: true }).click()]);
    check('javascript_disabled_subscription_still_works', (await n.getByRole('status').innerText()).includes('Adresiniz başvuruya uygunsa')); await nojs.close();
    const blocked = await browser.newContext(); const b = await blocked.newPage();
    await b.route('https://fonts.googleapis.com/**', t => t.fulfill({ contentType: 'text/css', body: '' }));
    await b.route('**/css/newsletter-demo.css*', t => t.abort()); await b.goto(base + '/', { waitUntil: 'domcontentloaded' }); await b.locator('[data-newsletter-demo]').scrollIntoViewIfNeeded(); await state(b, 'static');
    check('missing_stylesheet_leaves_static_readable_content', await b.locator('[data-newsletter-demo]').getAttribute('data-mode') === 'static' && await b.locator('.newsletter-demo-message').isVisible()); await blocked.close();
    const dark = await browser.newContext({ reducedMotion: 'reduce', viewport: { width: 390, height: 844 } });
    const d = await dark.newPage(); await d.route('https://fonts.googleapis.com/**', t => t.fulfill({ contentType: 'text/css', body: '' })); await d.goto(base + '/', { waitUntil: 'domcontentloaded' }); await d.evaluate(() => document.documentElement.classList.add('dark'));
    await d.locator('[data-newsletter-demo]').scrollIntoViewIfNeeded();
    await d.screenshot({ path: path.join(out, 'inbox-dark-mobile.png'), fullPage: true });
    check('dark_theme_is_readable_and_not_overflowing', await d.locator('.newsletter-demo-window').evaluate(e => { const s = getComputedStyle(e); return s.backgroundColor !== s.color && document.documentElement.scrollWidth <= innerWidth + 1; })); await dark.close();
    check('no_duplicate_ids_or_inline_scripts', await page.goto(base + '/', { waitUntil: 'domcontentloaded' }).then(() => page.evaluate(() => { const ids = [...document.querySelectorAll('[id]')].map(e => e.id); return ids.length === new Set(ids).size && !document.querySelector('script:not([src]):not([type="application/ld+json"])'); })));
    check('no_runtime_console_or_CSP_errors', !errors.length && !consoleErrors.length && !csp.length);
})().catch(e => { checks.completed = false; console.log(e.name + ': ' + e.message); process.exitCode = 1; }).finally(async () => {
    if (context) await context.close(); if (browser) await browser.close();
    const passed = !!Object.keys(checks).length && Object.values(checks).every(Boolean); fs.mkdirSync(out, { recursive: true });
    fs.writeFileSync(path.join(out, 'home-browser.json'), JSON.stringify({ passed, count: Object.keys(checks).length, checks, measurements, page_errors: errors, csp_violations: csp, console_error_count: consoleErrors.length }, null, 2) + '\n');
    if (!passed) process.exitCode = 1;
});
