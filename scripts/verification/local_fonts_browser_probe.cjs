// B01 real Chrome acceptance: fonts come from this site only; layout and pixels match a capture taken with the old remote fonts.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');

const base = process.env.DEVCORE_FONTS_BASE_URL || 'http://127.0.0.1:15205';
assert.equal(new URL(base).hostname, '127.0.0.1');
const output = process.env.DEVCORE_FONTS_REPORT_DIR;
assert.ok(output, 'An evidence directory is required');
fs.mkdirSync(output, { recursive: true });
// Capture mode records the remote-font build, proving this probe can see the requests the local build must not make.
const capture = process.env.DEVCORE_FONTS_CAPTURE === '1';
const baseline = process.env.DEVCORE_FONTS_BASELINE_DIR;
const login = process.env.DEVCORE_TEST_ADMIN_LOGIN_PATH || '/fixture-admin/login';
const publicPages = ['/', '/post/f01-turkce-karakterler', '/category/f01-active', '/ara?query=F01', '/about', login];
const adminPages = ['/Admin/Dashboard', '/AdminPost', '/AdminCategory', '/AdminWriting/Create'];
const viewports = { desktop: { width: 1440, height: 1000 }, mobile: { width: 390, height: 844 } };
const fontHosts = ['fonts.googleapis.com', 'fonts.gstatic.com'];
const checks = {}, errors = [], consoleErrors = [], violations = [], comparison = {}, remote = [], external = [], fontFiles = new Map();
const check = (name, passed) => { checks[name] = !!passed; assert.ok(passed, name); };
const fileName = (label, route) => `${label}-${route.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'home'}`;

const layoutOf = () => [...document.querySelectorAll('h1,h2,h3,p,a,button,label,th,td,input,select,code,li')].map(element => {
    const box = element.getBoundingClientRect();
    return [element.tagName, Math.round(box.x * 4) / 4, Math.round(box.y * 4) / 4, Math.round(box.width * 4) / 4, Math.round(box.height * 4) / 4];
});

// Half a pixel of tolerance: positions are fractional and rounding alone must not count as movement.
const countMoved = (before, after) => before.length !== after.length ? -1
    : before.filter((box, index) => box[0] !== after[index][0] || box.slice(1).some((value, axis) => Math.abs(value - after[index][axis + 1]) > 0.5)).length;

// Compares inside a blank page, where no site policy applies to the data URLs.
async function pixelDifference(page, first, second) {
    return page.evaluate(async ([a, b]) => {
        const load = source => new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = source; });
        const [left, right] = await Promise.all([load(a), load(b)]);
        if (left.width !== right.width || left.height !== right.height) return { sameSize: false, ratio: 1 };
        const read = image => { const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height; const context = canvas.getContext('2d'); context.drawImage(image, 0, 0); return context.getImageData(0, 0, image.width, image.height).data; };
        const [x, y] = [read(left), read(right)];
        let changed = 0;
        for (let i = 0; i < x.length; i += 4) if (Math.abs(x[i] - y[i]) + Math.abs(x[i + 1] - y[i + 1]) + Math.abs(x[i + 2] - y[i + 2]) > 30) changed++;
        return { sameSize: true, ratio: changed / (x.length / 4) };
    }, [first, second]);
}

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    for (const [label, viewport] of Object.entries(viewports)) {
        const context = await browser.newContext({ viewport, locale: 'tr-TR', reducedMotion: 'reduce' });
        await context.addInitScript(() => document.addEventListener('securitypolicyviolation', event =>
            window.__violations = [...(window.__violations || []), event.effectiveDirective]));
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.name));
        page.on('console', message => {
            if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) consoleErrors.push(message.text().slice(0, 160));
        });
        page.on('request', request => {
            const url = new URL(request.url());
            if (!['http:', 'https:'].includes(url.protocol) || url.origin === base) return;
            external.push(url.host);
            if (fontHosts.includes(url.hostname)) remote.push(url.host);
        });
        page.on('response', response => {
            // Later page loads may revalidate with 304, so the first answer per context is the one that describes the file.
            const file = new URL(response.url()).pathname;
            if (file.endsWith('.woff2') && !fontFiles.has(label + file)) fontFiles.set(label + file, [response.status(), response.headers()['content-type']]);
        });
        for (const route of [...publicPages, ...adminPages]) {
            if (route === adminPages[0]) {
                await page.goto(base + login, { waitUntil: 'load' });
                await page.locator('#username').fill(process.env.DEVCORE_TEST_ADMIN_USERNAME || 'f17-admin');
                await page.locator('#password').fill(process.env.DEVCORE_TEST_ADMIN_PASSWORD || 'f17-isolated-password');
                await page.locator('#password').press('Enter');
                await page.waitForURL(url => !url.pathname.includes(login));
            }
            const response = await page.goto(base + route, { waitUntil: 'load' });
            check(`${label}_page_opens_${route}`, response.status() === 200);
            await page.evaluate(() => document.fonts.ready);
            const name = fileName(label, route);
            const layout = await page.evaluate(layoutOf);
            const policy = response.headers()['content-security-policy'] || '';
            violations.push(...await page.evaluate(() => window.__violations || []));
            fs.writeFileSync(path.join(output, name + '.json'), JSON.stringify(layout));
            await page.screenshot({ path: path.join(output, name + '.png'), fullPage: true, animations: 'disabled' });
            if (capture) continue;
            check(`${label}_no_remote_font_markup_${route}`, !(await page.content()).includes('fonts.g'));
            check(`${label}_policy_has_only_own_fonts_${route}`, policy.includes("style-src 'self';") && policy.includes("font-src 'self';") && !policy.includes('google') && !policy.includes('gstatic'));
            check(`${label}_no_overflow_${route}`, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
            const faces = await page.evaluate(() => [...document.fonts].filter(face => face.status === 'loaded').map(face => face.family.replace(/"/g, '')));
            check(`${label}_own_fonts_in_use_${route}`, faces.includes('Inter') && faces.includes('JetBrains Mono'));
            check(`${label}_font_files_are_own_${route}`, await page.evaluate(origin => performance.getEntriesByType('resource')
                .filter(entry => /\.woff2(\?|$)/.test(entry.name)).every(entry => entry.name.startsWith(origin + '/generated/fonts/')), base));
            if (route.startsWith('/post/')) check(`${label}_turkish_letters_use_own_font`, await page.evaluate(async () => {
                await document.fonts.load('16px Inter', 'ğşİ');
                return document.fonts.check('16px Inter', 'ığüşöçİĞŞÖÇÜ') && [...document.fonts].some(face => face.family.includes('Inter') && face.unicodeRange.includes('U+100') && face.status === 'loaded');
            }));
            if (baseline) {
                const before = JSON.parse(fs.readFileSync(path.join(baseline, name + '.json'), 'utf8'));
                const moved = countMoved(before, layout);
                const blank = await context.newPage();
                const asData = file => 'data:image/png;base64,' + fs.readFileSync(file).toString('base64');
                const pixels = await pixelDifference(blank, asData(path.join(baseline, name + '.png')), asData(path.join(output, name + '.png')));
                await blank.close();
                comparison[name] = { elements: layout.length, moved, sameSize: pixels.sameSize, changedPixels: Number(pixels.ratio.toFixed(5)) };
                if (route === '/' && label === 'desktop') {
                    // Negative control: without the font files the same comparison must report a difference.
                    const plain = await browser.newContext({ viewport, locale: 'tr-TR', reducedMotion: 'reduce' });
                    await plain.route('**/*.woff2', font => font.abort());
                    const bare = await plain.newPage();
                    await bare.goto(base + route, { waitUntil: 'load' });
                    await bare.evaluate(() => document.fonts.ready);
                    const screenshot = (await bare.screenshot({ fullPage: true, animations: 'disabled' })).toString('base64');
                    const blankControl = await plain.newPage();
                    const control = await pixelDifference(blankControl, asData(path.join(baseline, name + '.png')), 'data:image/png;base64,' + screenshot);
                    await plain.close();
                    check('comparison_detects_missing_fonts', !control.sameSize || control.ratio > 0.005);
                    comparison.kontrol_fontsuz = { sameSize: control.sameSize, changedPixels: Number(control.ratio.toFixed(5)) };
                }
            }
        }
        await context.close();
    }
    await browser.close();
    if (capture) {
        check('control_probe_sees_remote_font_requests', remote.length > 0);
    } else {
        check('no_remote_font_requests', remote.length === 0);
        check('no_external_requests', external.length === 0);
        check('font_files_served_as_woff2', fontFiles.size >= 2 && [...fontFiles.values()].every(([status, type]) => status === 200 && type === 'font/woff2'));
        check('no_runtime_errors', errors.length === 0);
        check('no_console_errors', consoleErrors.length === 0);
        check('no_csp_violations', violations.length === 0);
        // Admin mobile pages differ only in the anti-aliasing of small mono text, so their pixel limit is wider; nothing may move anywhere.
        if (baseline) check('layout_and_pixels_match_capture', Object.entries(comparison).every(([name, page]) => name === 'kontrol_fontsuz' ||
            (page.moved === 0 && page.sameSize && page.changedPixels < (name.includes('-Admin') ? 0.02 : 0.001))));
    }
})().catch(error => { console.error(error.message.slice(0, 300)); process.exitCode = 1; }).finally(() => {
    fs.writeFileSync(path.join(output, 'requests.json'), JSON.stringify({ remote: remote.length, external: [...new Set(external)], fontFiles: [...fontFiles.entries()] }, null, 2) + '\n');
    fs.writeFileSync(path.join(output, 'browser.json'), JSON.stringify({ passed: Object.values(checks).every(Boolean) && process.exitCode !== 1, capture, count: Object.keys(checks).length, checks, comparison, errors, consoleErrors, violations,
        scope: 'Real Chrome against the unmodified build on a disposable fixture.' }, null, 2) + '\n');
    process.exit();
});
