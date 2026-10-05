import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { parse } from 'acorn';
import { replaceSanitizer } from '../frontend/editor-bundle.mjs';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_EDITOR_BASE_URL || 'http://127.0.0.1:15205';
assert.equal(new URL(base).hostname, '127.0.0.1');
const output = process.env.DEVCORE_EDITOR_REPORT_DIR;
assert.ok(output, 'A new evidence directory is required');
await mkdir(output); // Preserve existing evidence instead of overwriting it.
const root = new URL('../../', import.meta.url);
const upstream = await readFile(new URL('frontend/vendor/toastui/upstream/toastui-editor-all.js', root), 'utf8');
const purify = await readFile(new URL('node_modules/dompurify/dist/purify.cjs.js', root), 'utf8');

// Expose the actual webpack sanitizer only in a detached test document.
function instrument(code) {
    const modules = [];
    function visit(node) {
        if (!node || typeof node !== 'object') return;
        if (node.type === 'VariableDeclarator' && node.id.name === '__webpack_modules__') {
            modules.push(...node.init.properties.filter(p => p.key.value === 368));
        }
        for (const value of Object.values(node)) {
            if (Array.isArray(value)) value.forEach(visit);
            else if (value && typeof value === 'object') visit(value);
        }
    }
    visit(parse(code, { ecmaVersion: 'latest' }));
    assert.equal(modules.length, 1);
    const end = modules[0].value.body.end - 1;
    return code.slice(0, end) + '\nglobalThis.__testPurify = module.exports;\n' + code.slice(end);
}

const checks = {}, observations = {}, errors = [], consoleErrors = [];
const browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
try {
    for (const [label, code] of [['before', upstream], ['after', replaceSanitizer(upstream, purify)]]) {
        const page = await browser.newPage();
        await page.setContent('<!doctype html><html><body></body></html>');
        await page.addScriptTag({ content: instrument(code) });
        observations[label] = await page.evaluate(() => {
            const sanitizer = window.__testPurify;
            // Historical depth is an observation, not the modern sanitizer contract.
            const deep = '<div>'.repeat(502) + '</div>'.repeat(502) + '<img>';
            const clean = sanitizer.sanitize(deep);
            const hostile = sanitizer.sanitize('<p><strong>safe</strong><img src=x onerror="alert(1)"><a href="javascript:alert(1)">link</a></p>');
            // Producer GHSA-6688-9rhm-gjv2: force-removed rawtext root must not return.
            const root = document.createElement('style');
            root.setAttribute('onclick', 'alert(1)');
            root.textContent = '</style><img src=x onerror=alert(1)>';
            document.body.appendChild(root);
            let rejectedRawtextRoot = false, unsafeReparse = false;
            try {
                const returned = sanitizer.sanitize(root, { IN_PLACE: true });
                const reparsed = new DOMParser().parseFromString(returned.outerHTML, 'text/html');
                unsafeReparse = !!reparsed.querySelector('[onerror]');
            } catch { rejectedRawtextRoot = true; }
            root.remove();
            return { version: sanitizer.version, retainedDepth: (clean.match(/<div>/g) || []).length,
                rejectedRawtextRoot, unsafeReparse,
                hostileAttributesRemoved: !/onerror|javascript:/i.test(hostile),
                formattingKept: hostile.includes('<strong>safe</strong>') };
        });
        await page.close();
    }
    checks.old_module_returns_unsafe_rawtext_root = observations.before.version === '2.3.3' && observations.before.unsafeReparse;
    checks.new_module_rejects_unsafe_rawtext_root = observations.after.version === '3.4.16' && observations.after.rejectedRawtextRoot && !observations.after.unsafeReparse;
    checks.hostile_attributes_removed = observations.after.hostileAttributesRemoved;
    checks.normal_formatting_preserved = observations.after.formattingKept;

    const context = await browser.newContext({ permissions: ['clipboard-read', 'clipboard-write'] });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.name));
    page.on('console', message => {
        if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) consoleErrors.push(message.text().slice(0, 160));
    });
    await page.goto(base + '/Account/Login');
    await page.locator('#username').fill('f17-admin');
    await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
    await page.waitForURL('**/Admin/Dashboard');
    const response = await page.goto(base + '/AdminPost/Create');
    checks.enforcing_csp_unchanged = response.headers()['content-security-policy'].includes("script-src 'self'; script-src-attr 'none'");
    await page.locator('.toastui-editor-mode-switch').getByText('WYSIWYG', { exact: true }).click();
    const editor = page.locator('.toastui-editor-ww-container .ProseMirror');
    await editor.waitFor({ state: 'visible' });
    await page.evaluate(async () => {
        const html = '<p><strong>Security paste accepted</strong><a href="javascript:alert(1)">unsafe link</a><img onerror="alert(1)"></p>';
        await navigator.clipboard.write([new ClipboardItem({
            'text/html': new Blob([html], { type: 'text/html' }),
            'text/plain': new Blob(['Security paste accepted'], { type: 'text/plain' })
        })]);
    });
    await editor.click();
    await page.keyboard.press('ControlOrMeta+V');
    await page.waitForFunction(() => document.querySelector('.toastui-editor-ww-container .ProseMirror').textContent.includes('Security paste accepted'));
    const html = await editor.innerHTML();
    checks.real_wysiwyg_paste_strips_hostile_attributes = !/onerror|javascript:/i.test(html);
    checks.real_wysiwyg_paste_keeps_formatting = await editor.locator('strong').getByText('Security paste accepted', { exact: true }).count() === 1;
    await page.locator('.toastui-editor-mode-switch').getByText('Markdown', { exact: true }).click();
    await page.waitForFunction(() => document.getElementById('Content').value.includes('Security paste accepted'));
    checks.markdown_roundtrip_keeps_text = (await page.locator('#Content').inputValue()).includes('Security paste accepted');
    checks.no_runtime_or_console_errors = errors.length === 0 && consoleErrors.length === 0;
    await page.screenshot({ path: output + '/paste-editor.png' });
    assert.ok(Object.values(checks).every(Boolean), 'An editor security regression failed');
} finally {
    await browser.close();
    await writeFile(output + '/security.json', JSON.stringify({ checks, observations, errors, consoleErrors,
        scope: 'Producer rawtext-root regression in detached Chrome document (not application exploit); real MVC WYSIWYG paste under CSP. Synthetic localhost only.' }, null, 2) + '\n');
}
