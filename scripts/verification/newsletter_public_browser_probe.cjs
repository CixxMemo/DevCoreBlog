// AY03 real Chrome acceptance: an anonymous visitor reads a published newsletter; drafts stay hidden; old addresses land on one page.
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');

const base = process.env.DEVCORE_PUBLIC_BASE_URL || 'http://127.0.0.1:15205';
const port = process.env.DEVCORE_PUBLIC_PG_PORT || '55465';
assert.equal(new URL(base).hostname, '127.0.0.1');
const output = process.env.DEVCORE_PUBLIC_REPORT_DIR;
assert.ok(output, 'An evidence directory is required');
fs.mkdirSync(output, { recursive: true });

const checks = {}, errors = [], consoleErrors = [], violations = [];
function check(name, passed) {
    checks[name] = !!passed;
    assert.ok(passed, name);
}
function sql(query) {
    return execFileSync('psql', ['-X', '-h', '127.0.0.1', '-p', port, '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1', '-c', query], { encoding: 'utf8' }).trim();
}
const category = sql('SELECT "Id" FROM "Categories" WHERE "IsActive" ORDER BY "Id" LIMIT 1');
function seed(id, slug, title, published) {
    const body = 'AY03_BODY_' + id;
    const document = JSON.stringify({ version: 1, document: { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: body }] }] } });
    sql(`INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","Excerpt","ThumbnailUrl","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","IsPublished","ContentKind","AccessScope","DocumentVersion","DocumentJson","DocumentPlainText","DocumentWordCount","DocumentReadingMinutes","EditVersion")
        VALUES (${id},'${title}','${slug}','Kısa özet.','${body}','','',${category},'2020-01-01',true,0,'2020-01-01',${published},4,1,1,'${document}','${body}',1,1,1)`);
}

(async () => {
    seed(933001, 'ay03-acik-bulten', 'AY03 açık bülten', true);
    seed(933002, 'ay03-taslak-bulten', 'AY03 taslak bülten', false);
    const browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    for (const [label, viewport] of Object.entries({ desktop: { width: 1440, height: 1000 }, mobile: { width: 390, height: 844 } })) {
        const context = await browser.newContext({ viewport });
        await context.addInitScript(() => document.addEventListener('securitypolicyviolation', event =>
            window.__violations = [...(window.__violations || []), event.effectiveDirective]));
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.name));
        page.on('console', message => {
            // The expected 404 for the draft is logged by Chrome as a failed document.
            if (message.type() === 'error' && !message.text().includes('Failed to load resource:')) consoleErrors.push(message.text().slice(0, 160));
        });
        async function audit(name) {
            violations.push(...await page.evaluate(() => window.__violations || []));
            check(`${label}_no_overflow_${name}`, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
        }

        const response = await page.goto(base + '/post/ay03-acik-bulten', { waitUntil: 'domcontentloaded' });
        check(`${label}_anonymous_reads_newsletter`, response.status() === 200 && await page.getByText('AY03_BODY_933001').isVisible());
        check(`${label}_article_has_no_subscription_trace`, !/abone-ol|\/sign-in|Abonelere özel|Bültene abone/.test(await page.content()));
        check(`${label}_no_sign_in_required`, new URL(page.url()).pathname === '/post/ay03-acik-bulten' && await page.locator('input[type="email"], input[type="password"]').count() === 0);
        await page.screenshot({ path: path.join(output, `${label}-bulten.png`) });
        await audit('article');

        await page.goto(base + '/bulten/ay03-acik-bulten', { waitUntil: 'domcontentloaded' });
        check(`${label}_old_address_lands_on_the_post`, new URL(page.url()).pathname === '/post/ay03-acik-bulten');
        for (const old of ['/bulten', '/bultenler']) {
            await page.goto(base + old, { waitUntil: 'domcontentloaded' });
            check(`${label}_old_list_lands_home_${old}`, new URL(page.url()).pathname === '/' && await page.locator('#home-heading').isVisible());
        }

        const search = await page.goto(base + '/ara?query=' + encodeURIComponent('AY03'), { waitUntil: 'domcontentloaded' });
        const results = await page.content();
        check(`${label}_search_finds_only_the_published_issue`, search.status() === 200 && results.includes('AY03 açık bülten') && !results.includes('AY03 taslak bülten'));
        await audit('search');

        await page.goto(base + '/?pageSize=27', { waitUntil: 'domcontentloaded' });
        const home = await page.content();
        check(`${label}_home_lists_only_the_published_issue`, home.includes('AY03 açık bülten') && !home.includes('AY03 taslak bülten'));
        await page.getByRole('link', { name: 'AY03 açık bülten' }).first().focus();
        await page.keyboard.press('Enter');
        await page.waitForURL('**/post/ay03-acik-bulten');
        check(`${label}_keyboard_opens_the_issue_from_home`, await page.getByText('AY03_BODY_933001').isVisible());

        const draft = await page.goto(base + '/post/ay03-taslak-bulten', { waitUntil: 'domcontentloaded' });
        check(`${label}_draft_stays_404`, draft.status() === 404 && !(await page.content()).includes('AY03_BODY_933002'));
        const oldDraft = await page.goto(base + '/bulten/ay03-taslak-bulten', { waitUntil: 'domcontentloaded' });
        check(`${label}_old_address_of_draft_stays_404`, oldDraft.status() === 404);
        await context.close();
    }
    check('no_runtime_errors', errors.length === 0);
    check('no_console_errors', consoleErrors.length === 0);
    check('no_csp_violations', violations.length === 0);
    await browser.close();
})().catch(error => { console.error(error.message.slice(0, 300)); process.exitCode = 1; }).finally(() => {
    fs.writeFileSync(path.join(output, 'browser.json'), JSON.stringify({ passed: Object.values(checks).every(Boolean) && process.exitCode !== 1, count: Object.keys(checks).length, checks, errors, consoleErrors, violations,
        scope: 'Real Chrome against the unmodified build on a disposable fixture; synthetic newsletter rows.' }, null, 2) + '\n');
    process.exit();
});
