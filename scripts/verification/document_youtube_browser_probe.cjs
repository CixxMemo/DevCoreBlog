// Actual Chrome/MVC/PostgreSQL editing journey and real YouTube playback.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL, report = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
fs.mkdirSync(report, { recursive: true });
assert.ok(!fs.existsSync(path.join(report, 'youtube-browser.json')));
const checks = {}, controlled = {}, errors = [], externalErrors = [], playback = {}, requests = [], diagnostics = {};
let browser;
function check(name, value, target = checks) { target[name] = !!value; assert.ok(value, name); }
const id = 'jNQXAC9IVRw';
const sources = [`https://youtu.be/${id}`, `https://www.youtube.com/watch?v=${id}`, `https://youtube.com/watch?v=${id}`,
    `https://www.youtube.com/embed/${id}`, `https://youtube.com/embed/${id}`, `https://www.youtube-nocookie.com/embed/${id}`];
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    // Only the retained synthetic image is controlled. YouTube is never intercepted.
    await context.route('https://example.test/retained.png', r => r.fulfill({ contentType: 'image/png', body:
        Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZfoAAAAASUVORK5CYII=', 'base64') }));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => {
        window.__youtubeViolations = [...(window.__youtubeViolations || []), e.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', m => {
        if (m.type() !== 'error' || m.text().startsWith('Failed to load resource:')) return;
        (m.location().url.startsWith(base) ? errors : externalErrors).push(m.text().slice(0, 200));
    });
    page.on('request', r => {
        if (r.isNavigationRequest() && r.url() === sources[5]) requests.push({
            origin: new URL(r.url()).origin, refererIsOnlySiteOrigin: r.headers().referer === new URL(base).origin + '/' });
    });
    page.on('dialog', d => d.type() === 'beforeunload' ? d.accept() : d.dismiss());
    const editor = page.getByRole('textbox', { name: 'Yazı metni', exact: true });
    const input = page.locator('[data-youtube-url]');
    const json = async () => JSON.parse(await page.locator('[name=DocumentJson]').inputValue());
    const countNodes = node => (node.type === 'youtube' ? 1 : 0) + (node.content || []).reduce((sum, child) => sum + countNodes(child), 0);
    const count = async () => countNodes((await json()).document);
    const fixture = JSON.parse(fs.readFileSync(process.env.DEVCORE_YOUTUBE_HTTP_REPORT, 'utf8'));
    await page.goto(base + '/fixture-admin/login');
    await page.locator('#username').fill('f17-admin'); await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Giriş yap', exact: true }).click(); await page.waitForURL('**/Admin/Dashboard');
    const loaded = await page.goto(base + '/AdminWriting/Edit/' + fixture.fixturePostId); await editor.waitFor();
    check('stored_video_text_table_image_all_open', await count() === 1 && (await editor.innerText()).includes('Korunan Türkçe') &&
        await editor.locator('td').count() === 1 && await editor.locator('img').count() === 1);
    check('writing_csp_remains_strict', loaded.headers()['content-security-policy'].includes("style-src-attr 'none'") &&
        loaded.headers()['content-security-policy'].includes('frame-src https://www.youtube-nocookie.com') &&
        !loaded.headers()['content-security-policy'].includes('unsafe-eval'));
    const initial = JSON.stringify(await json());
    const invalid = [sources[0] + '?t=1', sources[0] + '#x', sources[0] + '%0a', sources[0].replace('https', 'HTTPS'),
        sources[0].replace('youtu.be', 'youtu.be.evil.test'), sources[0].replace('youtu.be', 'user:pass@youtu.be'),
        sources[0].replace('youtu.be', 'youtu.be:443'), sources[0].replace(id, '%6a' + id.slice(1)),
        sources[0].replace(id, '../' + id), sources[0].slice(0, -1), sources[1] + '&x=1',
        '<iframe src="' + sources[5] + '" onload="alert(1)"></iframe>'];
    for (let i = 0; i < invalid.length; i++) {
        await input.fill(invalid[i]); await input.press('Enter');
        check('invalid_url_preserves_document_' + i, JSON.stringify(await json()) === initial && await input.getAttribute('aria-invalid') === 'true' &&
            await input.evaluate(e => e === document.activeElement));
    }
    // Dispatching clipboard data is a controlled admission test, not an OS clipboard claim.
    await editor.evaluate(e => {
        const data = new DataTransfer(); data.setData('text/html', '<iframe src="https://evil.test" onload="alert(1)"></iframe>');
        e.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
    });
    check('controlled_raw_iframe_paste_keeps_document', JSON.stringify(await json()) === initial && await editor.locator('iframe').count() === 1, controlled);
    await editor.locator('p').first().click(); await page.keyboard.press('Home');
    await page.keyboard.press('Shift+End');
    for (let i = 0; i < sources.length; i++) {
        await input.fill(sources[i]); await input.press('Enter');
        check('allowed_url_inserts_node_' + i, await count() === i + 2 && !(await input.inputValue()) &&
            (await editor.innerText()).includes('Korunan Türkçe metin 👋'));
    }
    for (let i = 0; i < 3; i++) { await input.fill(sources[0]); await page.locator('[data-youtube-add]').click(); }
    const ten = JSON.stringify(await json());
    await input.fill(sources[0]); await input.press('Enter');
    diagnostics.limit = { count: await count(), unchanged: JSON.stringify(await json()) === ten,
        statusMentionsLimit: (await page.locator('[data-youtube-status]').innerText()).includes('10 video') };
    check('eleventh_video_keeps_all_ten_and_text', diagnostics.limit.count === 10 && diagnostics.limit.unchanged && diagnostics.limit.statusMentionsLimit);
    await page.locator('[data-writing-command=undo]').click();
    check('undo_removes_last_video_without_text_loss', await count() < 10 && (await editor.innerText()).includes('Korunan Türkçe'));
    await page.locator('[data-writing-command=redo]').click(); check('redo_restores_ten_videos', await count() === 10);
    const beforeSave = JSON.stringify(await json());
    const save = page.waitForResponse(r => r.request().method() === 'POST' && r.url().includes('/AdminWriting/Edit/'));
    await page.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true }).click();
    check('native_video_save_commits', (await save).status() === 302);
    await page.waitForLoadState(); await editor.waitFor();
    check('video_round_trip_preserves_exact_editor_document', JSON.stringify(await json()) === beforeSave &&
        await editor.locator('td').count() === 1 && await editor.locator('img').count() === 1);
    // Narrow viewport is real Chrome layout, not a physical mobile device.
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 1000 });
        if (width === 390) await page.waitForFunction(() => getComputedStyle(document.querySelector('#admin-sidebar')).visibility === 'hidden');
        await input.focus(); await page.keyboard.press('Tab');
        check('keyboard_focus_visible_' + width, await page.locator('[data-youtube-add]').evaluate(e =>
            e === document.activeElement && getComputedStyle(e).outlineStyle !== 'none'));
        await editor.locator('iframe').first().scrollIntoViewIfNeeded();
        check('responsive_video_no_page_overflow_' + width, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth) &&
            await editor.locator('iframe').first().evaluate(e => { const r = e.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth; }));
        await page.screenshot({ path: path.join(report, 'youtube-writing-' + width + '.png'), fullPage: true });
    }
    check('writing_has_no_csp_violations', (await page.evaluate(() => window.__youtubeViolations || [])).length === 0);
    const document = await json();
    await page.goto(base + '/AdminDocument/Preview');
    await page.locator('[name="Input.DocumentJson"]').fill(JSON.stringify(document));
    const preview = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/AdminDocument/Preview'));
    await page.getByRole('button', { name: 'Önizlemeyi göster', exact: true }).click();
    check('server_preview_succeeds_privately', (await preview).status() === 200 && (await preview).headers()['cache-control'].includes('no-store'));
    await page.waitForLoadState();
    const iframe = page.locator('.document-video iframe').first(); await iframe.scrollIntoViewIfNeeded();
    check('preview_matches_safe_editor_iframe_policy', await iframe.getAttribute('src') === sources[5] &&
        await iframe.getAttribute('referrerpolicy') === 'strict-origin-when-cross-origin' && await page.locator('.document-video').count() === 10);
    const frame = await (await iframe.elementHandle()).contentFrame();
    await frame.locator('#movie_player').waitFor({ timeout: 30000 });
    const playerText = await frame.locator('body').innerText();
    playback.error153 = playerText.includes('153');
    check('real_youtube_player_has_no_153_configuration_error', !playback.error153);
    // YouTube serves desktop or compact controls according to the iframe width.
    await frame.getByRole('button', { name: /^(Videoyu oynat|Play|Play video)$/iu }).click({ timeout: 30000 });
    await frame.waitForFunction(() => { const v = document.querySelector('video'); return v && v.currentTime > 0 && !v.paused; }, undefined, { timeout: 30000 });
    Object.assign(playback, await frame.locator('video').evaluate(v => ({ currentTime: v.currentTime, duration: v.duration, paused: v.paused })));
    check('real_youtube_video_actually_plays', playback.currentTime > 0 && playback.duration > 0 && !playback.paused);
    await page.screenshot({ path: path.join(report, 'youtube-preview-playback.png'), fullPage: true });
    check('real_embed_sends_only_origin_referer', requests.length > 0 && requests.every(r => r.refererIsOnlySiteOrigin));
    check('preview_has_no_csp_violations', (await page.evaluate(() => window.__youtubeViolations || [])).length === 0);
    check('no_application_runtime_errors', errors.length === 0);
})().catch(e => { checks.completed = false; console.error(e.name + ': ' + e.message.split('\n')[0]); process.exitCode = 1; })
.finally(async () => {
    if (browser) await browser.close();
    fs.writeFileSync(path.join(report, 'youtube-browser.json'), JSON.stringify({ checks, controlled, errors, externalErrors, playback, requests, diagnostics,
        scope: 'Real Chrome/MVC/owned PostgreSQL editing and actual external YouTube playback. Retained image response and dispatched clipboard event are controlled tests; no Cloudinary upload or physical mobile claim.' }, null, 2) + '\n');
    console.log(JSON.stringify({ count: Object.keys(checks).length, failed: Object.keys(checks).filter(k => !checks[k]) }));
});
