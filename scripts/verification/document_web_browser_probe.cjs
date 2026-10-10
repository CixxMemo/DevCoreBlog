// Real browser checks against an owned synthetic fixture; no real admin settings are accepted.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL;
const output = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
assert.ok(output && !fs.existsSync(path.join(output, 'document-browser.json')));
fs.mkdirSync(output, { recursive: true });
const checks = {}, errors = [], violations = [], scriptOrigins = new Set();
const text = (value, marks) => ({ type: 'text', text: value, ...(marks ? { marks } : {}) });
const node = (type, content, attrs) => ({ type, ...(content ? { content } : {}), ...(attrs ? { attrs } : {}) });
const para = value => node('paragraph', value ? [text(value)] : undefined);
const literal = '<script>window.__documentXss=1</script><img src=x onerror="window.__documentXss=2"> Türkçe 😀 &';
const mark = type => ({ type });
const cell = (value, header = false) => node(header ? 'tableHeader' : 'tableCell', [para(value)]);
const blocks = [
    node('heading', [text('Türkçe başlık 😀')], { level: 2, textAlign: 'center' }),
    para(literal),
    node('paragraph', [text('Biçimli bağlantı', [mark('bold'), mark('italic'), mark('underline'), mark('code'),
        { type: 'link', attrs: { href: 'https://example.test/?a=1&b=2', title: literal, target: '_blank' } }]), node('hardBreak'), text('Yeni satır')], { textAlign: 'right' }),
    node('blockquote', [node('heading', [text('Alt başlık')], { level: 3 }), para('Alıntı metni')]),
    node('bulletList', [node('listItem', [para('Birinci madde'), node('orderedList', [node('listItem', [para('İç madde')])], { start: 7 })])]),
    node('heading', [text('Kod ve tablo')], { level: 4 }),
    node('codeBlock', [text('const örnek = "<script>";\n\t' + 'uzunkod_'.repeat(180))], { language: 'javascript' }),
    node('table', [node('tableRow', Array.from({ length: 10 }, (_, i) => cell('Sütun ' + (i + 1), true))),
        ...Array.from({ length: 19 }, (_, r) => node('tableRow', Array.from({ length: 10 }, (_, c) => cell('Satır ' + (r + 1) + ' değer ' + c))))]),
    node('image', undefined, { src: 'https://example.test/fixture.gif', alt: literal, title: literal }),
    node('youtube', undefined, { src: 'https://youtu.be/abcdefghijk' }),
    node('heading', undefined, { level: 2 })
];
const fixture = JSON.stringify({ version: 1, document: node('doc', blocks) });
let browser;
function check(name, condition) { checks[name] = !!condition; assert.ok(condition, name); }
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    // Deterministic media responses verify browser DOM/CSP/layout, not YouTube availability or upload.
    await context.route('https://example.test/fixture.gif', route => route.fulfill({ contentType: 'image/gif', body: Buffer.from('R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==', 'base64') }));
    await context.route('https://www.youtube-nocookie.com/embed/abcdefghijk', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Sentetik video</title><p>Video alanı</p>' }));
    await context.route('https://fonts.googleapis.com/**', route => route.fulfill({ contentType: 'text/css', body: '' }));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', event => {
        window.__documentViolations = [...(window.__documentViolations || []), event.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.name));
    page.on('console', message => { if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) errors.push('console-error'); });
    page.on('request', request => { if (request.resourceType() === 'script') scriptOrigins.add(new URL(request.url()).origin); });
    async function audit() { violations.push(...await page.evaluate(() => window.__documentViolations || [])); }
    const denied = await context.request.get(base + '/AdminDocument/Preview');
    check('anonymous_preview_private_denial', denied.status() === 404 && denied.headers()['cache-control'].includes('no-store') && !denied.headers().location);
    await page.goto(base + '/fixture-admin/login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard');
    await audit();
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
        const get = await page.goto(base + '/AdminDocument/Preview');
        check('private_empty_get_' + width, get.status() === 200 && (await get.allHeaders())['cache-control'].includes('no-store') && await page.locator('.document-content').count() === 0);
        const field = page.getByRole('textbox', { name: 'İçerik belgesi (JSON v1)' });
        await field.fill(fixture);
        await field.focus();
        await page.keyboard.press('Tab');
        const button = page.getByRole('button', { name: 'Önizlemeyi göster', exact: true });
        check('native_keyboard_form_' + width, await button.evaluate(e => e === document.activeElement));
        const responsePromise = page.waitForResponse(r => r.url() === base + '/AdminDocument/Preview' && r.request().method() === 'POST');
        await page.keyboard.press('Enter');
        const response = await responsePromise;
        await page.locator('.document-content').waitFor();
        const headers = await response.allHeaders();
        check('private_post_200_' + width, response.status() === 200 && headers['cache-control'].includes('no-store') && headers['x-robots-tag'].includes('noindex'));
        check('unchanged_enforcing_csp_' + width, headers['content-security-policy'].includes("style-src-attr 'none'") && !/unsafe-inline|unsafe-eval|nonce-/.test(headers['content-security-policy']));
        const body = page.locator('.document-content');
        check('literal_text_visible_no_execution_' + width, (await body.innerText()).includes(literal) && await body.locator('script, img[onerror], [onclick], [style]').count() === 0 && await page.evaluate(() => window.__documentXss === undefined));
        check('all_marks_and_safe_attributes_' + width, await body.locator('strong em u code a[target="_blank"][rel="noopener noreferrer"]').count() === 1 && await body.locator('a').getAttribute('title') === literal);
        check('lists_quote_heading_levels_' + width, await body.locator('ul > li > ol[start="7"] > li').count() === 1 && await body.locator('blockquote h3').count() === 1 && await body.locator('h4').count() === 1);
        const headingIds = await body.locator('h2,h3,h4').evaluateAll(es => es.map(e => e.id));
        check('toc_targets_same_snapshot_' + width, JSON.stringify(headingIds) === JSON.stringify(['document-section-1', 'document-section-2', 'document-section-3', 'document-section-4'])
            && JSON.stringify(await page.getByRole('navigation', { name: 'İçindekiler' }).locator('a').evaluateAll(es => es.map(e => e.hash.slice(1)))) === JSON.stringify(headingIds));
        await page.getByRole('navigation', { name: 'İçindekiler' }).locator('a').first().click();
        check('toc_navigation_' + width, new URL(page.url()).hash === '#document-section-1');
        check('fixed_alignment_styles_' + width, await body.locator('h2').first().evaluate(e => getComputedStyle(e).textAlign) === 'center' && await body.locator('.document-align-right').evaluate(e => getComputedStyle(e).textAlign) === 'right');
        const code = body.getByRole('region', { name: 'Kod bloğu' });
        check('code_whitespace_and_long_scroll_' + width, (await code.innerText()).includes('\n\t') && await code.evaluate(e => getComputedStyle(e).whiteSpace === 'pre' && e.scrollWidth > e.clientWidth));
        const table = body.getByRole('region', { name: 'Yazı tablosu' });
        check('semantic_full_table_' + width, await table.locator('tr').count() === 20 && await table.locator('th').count() === 10 && await table.locator('td').count() === 190);
        for (const [label, scroll] of [['code', code], ['table', table]]) {
            await scroll.focus();
            await page.keyboard.press('Shift+Tab');
            await page.keyboard.press('Tab');
            check('keyboard_focus_visible_' + label + '_' + width, await scroll.evaluate(e => e === document.activeElement && getComputedStyle(e).outlineStyle !== 'none'));
            await page.keyboard.press('ArrowRight');
            await page.waitForTimeout(180);
            check('keyboard_horizontal_scroll_' + label + '_' + width, await scroll.evaluate(e => e.scrollWidth <= e.clientWidth || e.scrollLeft > 0));
        }
        if (width === 390) check('mobile_table_overflow_stays_inside_region', await table.evaluate(e => e.scrollWidth > e.clientWidth));
        check('no_page_or_main_horizontal_overflow_' + width, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.querySelector('#admin-page-content').scrollWidth <= document.querySelector('#admin-page-content').clientWidth));
        const image = body.locator('img');
        await image.scrollIntoViewIfNeeded();
        await image.evaluate(e => e.decode());
        check('gif_source_and_encoded_labels_' + width, await image.getAttribute('src') === 'https://example.test/fixture.gif' && await image.getAttribute('alt') === literal && await image.getAttribute('title') === literal && await image.evaluate(e => e.naturalWidth > 0));
        const video = body.locator('iframe');
        await video.scrollIntoViewIfNeeded();
        check('narrow_video_and_mobile_dimensions_' + width, await video.getAttribute('src') === 'https://www.youtube-nocookie.com/embed/abcdefghijk' && await video.getAttribute('title') === 'YouTube videosu'
            && await video.evaluate(e => e.clientWidth > 0 && e.clientWidth <= document.querySelector('.document-content').clientWidth));
        check('no_inline_executable_or_storage_' + width, await page.locator('script:not([src]):not([type="application/ld+json"])').count() === 0 && await page.evaluate(() => !localStorage.length && !sessionStorage.length));
        await body.locator('h2').first().scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, 'document-' + width + '.png') });
        await table.scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, 'table-' + width + '.png') });
        await audit();
    }
    await page.goto(base + '/AdminDocument/Preview');
    await page.getByRole('textbox', { name: 'İçerik belgesi (JSON v1)' }).fill('{"version":1,"document":{"type":"doc","content":[{"type":"html"}]}}');
    const rejectedPromise = page.waitForResponse(r => r.url() === base + '/AdminDocument/Preview' && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Önizlemeyi göster', exact: true }).click();
    const rejected = await rejectedPromise;
    await page.getByText('İçerik belgesi geçersiz. Sürüm, biçim ve bağlantıları kontrol edin.', { exact: true }).waitFor();
    check('rejection_400_turkish_error_no_partial', rejected.status() === 400 && await page.locator('.document-content').count() === 0 && (await page.getByRole('textbox', { name: 'İçerik belgesi (JSON v1)' }).inputValue()).includes('html'));
    await audit();
    await page.goto(base + '/AdminPost/EditorTrial');
    check('preview_reachable_from_editor_trial', await page.getByRole('link', { name: 'İçerik belgesi önizlemesi' }).getAttribute('href') === '/AdminDocument/Preview');
    await page.getByText('Editör hazır. Bu metin kaydedilmez.', { exact: true }).waitFor();
    await audit();
    check('all_executable_scripts_local', [...scriptOrigins].every(origin => origin === new URL(base).origin));
    check('no_runtime_console_errors', errors.length === 0);
    check('no_csp_violations', violations.length === 0);
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => {
    fs.writeFileSync(path.join(output, 'document-browser.json'), JSON.stringify({ count: Object.keys(checks).length, checks, errors, violations,
        scope: 'Real Chrome desktop/mobile/keyboard; owned synthetic PostgreSQL. Media/network responses stubbed; no real YouTube playback or upload/provider proof.' }, null, 2) + '\n');
    if (browser) await browser.close();
});
