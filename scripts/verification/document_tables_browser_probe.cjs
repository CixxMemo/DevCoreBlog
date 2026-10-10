// Table editing and paste admission through actual DOM events, MVC forms and owned PostgreSQL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DEVCORE_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.DEVCORE_DOCUMENT_BASE_URL, report = process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname, '127.0.0.1');
fs.mkdirSync(report, { recursive: true });
assert.ok(!fs.existsSync(path.join(report, 'tables-browser.json')));
const checks = {}, errors = [], violations = [];
let browser;
function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
function canonical(node) {
    const result = { type: node.type };
    if (node.text !== undefined) result.text = node.text;
    const attrs = Object.fromEntries(Object.entries(node.attrs || {}).filter(([k,v]) => v !== null && !(['colspan','rowspan','start'].includes(k) && v === 1)).sort(([a],[b]) => a.localeCompare(b)));
    if (Object.keys(attrs).length) result.attrs = attrs;
    if (node.content?.length) result.content = node.content.map(canonical);
    if (node.marks?.length) result.marks = node.marks.map(canonical).sort((a,b) => a.type.localeCompare(b.type));
    return result;
}
(async () => {
    browser = await chromium.launch({ headless: true, executablePath: process.env.DEVCORE_BROWSER_EXECUTABLE });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await context.route('https://fonts.googleapis.com/**', r => r.fulfill({ contentType: 'text/css', body: '' }));
    await context.addInitScript(() => document.addEventListener('securitypolicyviolation', e => {
        window.__tableViolations = [...(window.__tableViolations || []), e.effectiveDirective];
    }));
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(e.name));
    page.on('console', m => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource:')) errors.push(m.text().slice(0,400)); });
    page.on('dialog', dialog => dialog.type() === 'beforeunload' ? dialog.accept() : dialog.dismiss());
    const editor = page.getByRole('textbox', { name: 'Yazı metni', exact: true });
    const tableDOM = page.locator('.writing-document table');
    const command = key => page.locator(`[data-writing-command=${key}]`);
    const act = async key => { await command(key).click(); await page.waitForFunction(()=>document.activeElement?.classList.contains('writing-document')); };
    const json = async () => JSON.parse(await page.locator('[name=DocumentJson]').inputValue()).document;
    const shape = async () => ({ rows: await tableDOM.locator('tr').count(), columns: await tableDOM.locator('tr').first().locator('th,td').count() });
    async function save() {
        const response = page.waitForResponse(r => r.url().includes('/AdminWriting/') && r.request().method() === 'POST');
        await page.getByRole('button', { name: /^(Taslağı kaydet|Değişiklikleri kaydet)$/ }).first().click();
        check('native_table_save_'+Object.keys(checks).length, (await response).status() === 302);
        await page.waitForLoadState(); await editor.waitFor();
    }
    async function paste(html) {
        await editor.evaluate((el, value) => {
            const data = new DataTransfer(); data.setData('text/html', value); data.setData('text/plain','Yapıştırılan tablo');
            el.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: data }));
        },html);
    }
    async function seeded(document) {
        await page.goto(base+'/AdminWriting/Create'); await editor.waitFor();
        const result = await context.request.post(base+'/AdminWriting/Create', { form: {
            Title:'F10 tarayıcı sınır belgesi', CategoryId:'1001', IsActive:'true', SaveAction:'SaveDraft',
            PublishDate:await page.locator('[name=PublishDate]').inputValue(), DocumentJson:JSON.stringify({version:1,document}),
            __RequestVerificationToken:await page.locator('#postForm [name=__RequestVerificationToken]').inputValue()
        }});
        check('server_accepts_seed_'+Object.keys(checks).length, result.status() === 200);
        await page.goto(result.url()); await editor.waitFor();
    }
    const makeTable = (rows,cols) => ({ type:'table', content:Array.from({length:rows},(_,r)=>({type:'tableRow',content:Array.from({length:cols},(_,c)=>({type:r===0?'tableHeader':'tableCell',attrs:{colspan:1,rowspan:1,colwidth:null,align:c%2?'center':null},content:[{type:'paragraph',content:[{type:'text',text:`Türkçe ${r}:${c} 👋`}]}]}))}))});
    await page.goto(base+'/fixture-admin/login');
    await page.locator('#username').fill('f17-admin'); await page.locator('#password').fill('f17-isolated-password');
    await page.getByRole('button',{name:'Sign In',exact:true}).click(); await page.waitForURL('**/Admin/Dashboard');
    await page.goto(base+'/AdminWriting/Create'); await editor.waitFor();
    check('turkish_table_tools_initial_selection_state', await command('tableInsert').isEnabled() && await command('tableRowAfter').isDisabled() && await command('tableHeader').isDisabled());
    await page.locator('#post-title-input').fill('F10 gerçek tablo yazımı'); await page.locator('[name=CategoryId]').selectOption('1001');
    await editor.click(); await act('tableInsert');
    check('insert_creates_three_by_three_with_header_row', (await shape()).rows===3 && (await shape()).columns===3 && await tableDOM.locator('th').count()===3);
    check('nested_insert_is_disabled_in_table', await command('tableInsert').isDisabled());
    await page.keyboard.type('Korunan Türkçe hücre 👋');
    await act('tableHeader');
    check('toggle_header_changes_only_selected_cell', await tableDOM.locator('th').count()===2 && (await tableDOM.innerText()).includes('Korunan Türkçe'));
    await act('tableHeader');
    await page.waitForFunction(()=>document.activeElement?.classList.contains('writing-document'));
    await page.keyboard.press('Tab');
    check('tab_moves_to_next_cell', await page.evaluate(()=>document.getSelection().anchorNode.parentElement.closest('th,td')?.cellIndex===1));
    await page.keyboard.press('Shift+Tab');
    check('shift_tab_moves_to_previous_cell', await page.evaluate(()=>document.getSelection().anchorNode.parentElement.closest('th,td')?.cellIndex===0));
    await tableDOM.locator('th,td').nth(1).click({modifiers:['Shift']});
    check('shift_click_selects_two_cells_without_merging',await tableDOM.locator('.selectedCell').count()===2);
    check('selected_cells_keep_unmerged_unresized_v1_attributes',JSON.stringify(await json()).includes('\"colspan\":1') && !JSON.stringify(await json()).includes('\"colspan\":2'));
    await tableDOM.locator('th,td').first().locator('p').click();
    for (const key of ['tableRowBefore','tableRowAfter','tableColumnBefore','tableColumnAfter']) await act(key);
    check('before_after_row_and_column_commands_work', (await shape()).rows===5 && (await shape()).columns===5);
    await tableDOM.locator('td,th').first().click();
    await act('tableRowDelete');
    await tableDOM.locator('td,th').first().click(); await act('tableColumnDelete');
    check('row_column_deletion_keeps_other_cell_text', (await shape()).rows===4 && (await shape()).columns===4 && (await tableDOM.innerText()).includes('Korunan Türkçe'));
    await page.getByRole('button',{name:'Geri al',exact:true}).click();
    check('undo_restores_deleted_column', (await shape()).columns===5);
    await page.getByRole('button',{name:'Yinele',exact:true}).click();
    check('redo_deletes_same_column', (await shape()).columns===4);
    const before = await json(); await save();
    check('create_save_reopen_preserves_table_structure_text_and_header', JSON.stringify(canonical(await json()))===JSON.stringify(canonical(before)));
    await tableDOM.locator('td,th').first().locator('p').first().click();
    const untouched = await page.locator('[name=DocumentJson]').inputValue();
    const invalidPastes = {
        nested:'<table><tr><td><blockquote><table><tr><td>İç tablo</td></tr></table></blockquote></td></tr></table>',
        merged:'<table><tr><td colspan="2">Birleşmiş</td></tr></table>',
        resized:'<table><tr><td data-colwidth="120">Boyut</td></tr></table>',
        rows:'<table>'+ '<tr><td>Satır</td></tr>'.repeat(21)+'</table>',
        columns:'<table><tr>'+ '<td>Sütun</td>'.repeat(11)+'</tr></table>',
        rectangle:'<table><tr><td>A</td><td>B</td></tr><tr><td>C</td></tr></table>',
    };
    for (const [name,html] of Object.entries(invalidPastes)) {
        await paste(html);
        check('paste_'+name+'_rejected_without_document_loss', await page.locator('[name=DocumentJson]').inputValue()===untouched && (await page.locator('[data-writing-status]').innerText()).includes('Mevcut metniniz korunuyor'));
    }
    // A valid pasted table is admitted outside a cell, then saved through the same server validator.
    await page.goto(base+'/AdminWriting/Create'); await editor.waitFor(); await editor.click();
    await paste('<table><tr><th>Başlık 👋</th><th>Değer</th></tr><tr><td>Türkçe</td><td align="right">42</td></tr></table>');
    check('valid_table_paste_retains_cells_and_alignment', (await shape()).rows===2 && (await shape()).columns===2 && (await tableDOM.innerText()).includes('Türkçe') && JSON.stringify(await json()).includes('"align":"right"'));
    await page.locator('#post-title-input').fill('F10 yapıştırılan tablo'); await page.locator('[name=CategoryId]').selectOption('1001');
    await save();
    check('valid_pasted_table_survives_server_round_trip', (await shape()).rows===2 && (await tableDOM.innerText()).includes('Başlık 👋'));
    await tableDOM.locator('td,th').first().click(); await act('tableDelete');
    check('delete_table_and_undo_restore_entire_content', await tableDOM.count()===0);
    await page.getByRole('button',{name:'Geri al',exact:true}).click();
    check('undo_restores_whole_deleted_table', (await shape()).rows===2 && (await tableDOM.innerText()).includes('Türkçe'));
    await seeded({type:'doc',content:[makeTable(20,10)]});
    await tableDOM.locator('td,th').last().locator('p').click();
    check('row20_column10_commands_are_disabled', await command('tableRowAfter').isDisabled() && await command('tableRowBefore').isDisabled() && await command('tableColumnAfter').isDisabled() && await command('tableColumnBefore').isDisabled());
    const limitBefore=await page.locator('[name=DocumentJson]').inputValue(); await page.keyboard.press('Tab');
    check('last_cell_tab_cannot_create_row21', await page.locator('[name=DocumentJson]').inputValue()===limitBefore && (await shape()).rows===20);
    for (const width of [1440,390]) {
        await page.setViewportSize({width,height:1000});
        if(width<768) await page.waitForFunction(()=>getComputedStyle(document.querySelector('#admin-sidebar')).visibility==='hidden');
        await tableDOM.scrollIntoViewIfNeeded();
        check('table_scroll_contains_overflow_'+width, await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth) && await page.locator('.writing-table-scroll').evaluate(el=>el.scrollWidth>el.clientWidth));
        await command('tableHeader').focus();
        check('keyboard_header_control_visible_focus_'+width, await command('tableHeader').evaluate(el=>el===document.activeElement && getComputedStyle(el).outlineStyle!=='none'));
        await page.keyboard.press('Space');
        await page.waitForFunction(()=>document.activeElement?.classList.contains('writing-document'));
        await page.screenshot({path:path.join(report,'table-'+width+'.png'),fullPage:true});
    }
    const mobileDoc=await json(); await save();
    check('mobile_table_save_reopens_same_document', JSON.stringify(canonical(await json()))===JSON.stringify(canonical(mobileDoc)));
    await seeded({type:'doc',content:[makeTable(20,10),makeTable(20,10),makeTable(20,10),makeTable(20,10),makeTable(19,10),makeTable(1,10),{type:'paragraph',content:[{type:'text',text:'Tablodan sonra'}]}]});
    const lastTable=tableDOM.last(); await lastTable.locator('td,th').first().click();
    check('total_1000_cells_disables_growth_in_small_table', await command('tableRowAfter').isDisabled() && await command('tableColumnAfter').isDisabled());
    await editor.locator(':scope > p').last().click();
    check('total_1000_cells_disables_new_table_outside_cell',await command('tableInsert').isDisabled());
    const totalBefore=await page.locator('[name=DocumentJson]').inputValue();
    await paste('<table><tr><td>Yeni hücre</td></tr></table>');
    check('paste_cannot_grow_full_document_above_1000_cells', await page.locator('[name=DocumentJson]').inputValue()===totalBefore);
    // Stored align/defaults and every cell survive an actual resave at the total bound.
    const full=await json(); await save();
    check('full_1000_cell_document_round_trip_has_no_loss', JSON.stringify(canonical(await json()))===JSON.stringify(canonical(full)));
    check('table_dom_has_no_inline_styles_or_resize_handles', await page.locator('.writing-document [style], .writing-document .column-resize-handle').count()===0);
    violations.push(...await page.evaluate(()=>window.__tableViolations||[]));
    check('no_runtime_or_console_errors',errors.length===0);check('no_csp_violations',violations.length===0);
})().catch(e=>{checks.completed=false;console.error(e.name+': verification interrupted; inspect boolean checks.');process.exitCode=1;})
.finally(async()=>{
    if(browser) await browser.close();
    fs.writeFileSync(path.join(report,'tables-browser.json'),JSON.stringify({count:Object.keys(checks).length,checks,errors,violations,scope:'Real Chrome table commands/keyboard/paste/MVC; owned PostgreSQL. No actual user data.'},null,2)+'\n');
    console.log(JSON.stringify({count:Object.keys(checks).length,failed:Object.keys(checks).filter(k=>!checks[k])}));
});
