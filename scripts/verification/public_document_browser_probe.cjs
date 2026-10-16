// Real public browser navigation, native filters/paging and safe document output on owned fixture data.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR,fixture=JSON.parse(fs.readFileSync(process.env.DEVCORE_PUBLIC_HTTP_REPORT,'utf8'));
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(process.env.DEVCORE_DOCUMENT_PG_PORT,'55488');fs.mkdirSync(report,{recursive:true});assert.ok(!fs.existsSync(path.join(report,'public-browser.json')));
const checks={},errors=[],consoleErrors=[],violations=[];let browser;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p','55488','-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'});
const cards=p=>p.locator('a.post-card');
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});const context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.exposeBinding('__publicDocCsp',(_source,directive)=>violations.push(directive));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__publicDocCsp(e.effectiveDirective)));
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 // Renderer URL/reference assertions only; no provider delivery/playback claim.
 await context.route('https://example.test/**',r=>r.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lSUAAAAASUVORK5CYII=','base64')}));
 await context.route('https://www.youtube-nocookie.com/**',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><title>Reference fixture</title>'}));
 const p=await context.newPage();p.on('pageerror',e=>errors.push(e.name));p.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))consoleErrors.push(m.text());});
 const before=Number(sql('SELECT "ViewCount" FROM "Posts" WHERE "Id"='+fixture.publicId));
 const response=await p.goto(base+'/post/'+fixture.publicSlug);check('real_public_get_200',response.status()===200);
 check('public_document_has_text_table_image_video',await p.locator('.document-content table').count()===1&&await p.locator('.document-content img').count()===1&&await p.locator('.document-content iframe').count()===1&&(await p.locator('.document-content').innerText()).includes('F16_BODY_NEEDLE'));
 check('literal_script_text_cannot_execute',await p.locator('.document-content script').count()===0&&(await p.locator('.document-content').innerText()).includes('<script>'));
 const schema=JSON.parse(await p.locator('script[type="application/ld+json"]').textContent());check('seo_and_reading_from_same_document',schema.wordCount===Number(sql('SELECT "DocumentWordCount" FROM "Posts" WHERE "Id"='+fixture.publicId))&&schema.description===fixture.plainText.slice(0,160)&&await p.locator('.public-detail-meta').getByText(/dk okuma/).count()===1);
 check('one_successful_anonymous_get_counts',Number(sql('SELECT "ViewCount" FROM "Posts" WHERE "Id"='+fixture.publicId))===before+1);
 const toc=p.getByRole('navigation',{name:'İçindekiler',exact:true});const link=toc.getByRole('link').first();const target=await link.getAttribute('href');check('heading_target_exists',await p.locator(target).count()===1);await link.click();check('toc_native_anchor_navigates',new URL(p.url()).hash===target);
 for(const width of [1440,390]){
  await p.setViewportSize({width,height:1000});const tableRegion=p.getByRole('region',{name:'Yazı tablosu',exact:true});await tableRegion.focus();await p.keyboard.press('Tab');await p.keyboard.press('Shift+Tab');
  check('document_page_no_horizontal_overflow_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  check('table_region_keyboard_focus_'+width,await tableRegion.evaluate(el=>el.tabIndex===0&&el===document.activeElement));
  await p.screenshot({path:path.join(report,'public-document-'+width+'.png'),fullPage:true});
 }
 await p.setViewportSize({width:1440,height:1000});await p.goto(base+'/?kind=philosophy');const kind=p.getByLabel('İçerik türü',{exact:true});check('native_kind_form_selects_finite_public_choices',await kind.inputValue()==='philosophy'&&await kind.locator('option').count()===4);
 check('philosophy_cards_have_correct_label',await cards(p).count()>0&&await cards(p).filter({hasText:'Felsefe'}).count()===await cards(p).count());
 await kind.selectOption('experience');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:'Türü uygula',exact:true}).click()]);check('native_kind_submit_uses_get_and_preserves_selection',new URL(p.url()).searchParams.get('kind')==='experience'&&await kind.inputValue()==='experience'&&await cards(p).filter({hasText:'Deney ve deneyim'}).count()===await cards(p).count());
 await p.goto(base+'/ara?query=F16_PAGE_BODY&kind=news');const firstHrefs=await cards(p).evaluateAll(es=>es.map(e=>e.getAttribute('href')));check('first_filtered_search_page_is_bounded',firstHrefs.length===9);
 const next=p.getByRole('navigation',{name:'Yazı sayfaları',exact:true}).getByRole('link',{name:'Sonraki >',exact:true});await next.focus();check('paging_visible_keyboard_focus',await next.evaluate(el=>document.activeElement===el&&getComputedStyle(el).outlineStyle!=='none'));await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.keyboard.press('Enter')]);
 const secondHrefs=await cards(p).evaluateAll(es=>es.map(e=>e.getAttribute('href')));check('native_paging_preserves_query_kind_without_duplicates',new URL(p.url()).searchParams.get('kind')==='news'&&new URL(p.url()).searchParams.get('query')==='F16_PAGE_BODY'&&new URL(p.url()).searchParams.get('page')==='2'&&secondHrefs.length>0&&secondHrefs.every(h=>!firstHrefs.includes(h)));
 await p.getByLabel('İçerik türü',{exact:true}).selectOption('philosophy');await Promise.all([p.waitForNavigation({waitUntil:'domcontentloaded'}),p.getByRole('button',{name:'Filtreleri uygula',exact:true}).click()]);check('changing_kind_resets_page_and_keeps_query',!new URL(p.url()).searchParams.has('page')&&new URL(p.url()).searchParams.get('query')==='F16_PAGE_BODY');
 await p.goto(base+'/ara?query=F16_BODY_NEEDLE&kind=news');check('search_body_opens_public_json_result',await cards(p).filter({hasText:'F16_public_article'}).count()>0);
 for(const width of [1440,390]){await p.setViewportSize({width,height:1000});const select=p.getByLabel('İçerik türü',{exact:true});await select.focus();await p.keyboard.press('Tab');await p.keyboard.press('Shift+Tab');check('kind_filter_accessible_without_overflow_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)&&await select.evaluate(el=>document.activeElement===el));await p.screenshot({path:path.join(report,'public-search-'+width+'.png'),fullPage:true});}
 const privateFixture=Object.entries(fixture.fixture).find(([name])=>name==='F16_PRIVATE_subscribers')[1];const denied=await p.goto(base+'/post/'+privateFixture.slug);check('private_public_route_404_without_document',denied.status()===404&&await p.locator('.document-content').count()===0&&!(await p.locator('body').innerText()).includes('F16_PRIVATE_SENTINEL'));
 const invalid=await p.goto(base+'/?kind=newsletter');check('newsletter_kind_is_400',invalid.status()===400);
 // Native MVC filters remain usable when JavaScript is disabled.
 const plain=await browser.newContext({javaScriptEnabled:false,viewport:{width:1440,height:1000}});const nojs=await plain.newPage();await nojs.goto(base+'/?kind=news');await nojs.getByLabel('İçerik türü',{exact:true}).selectOption('philosophy');await Promise.all([nojs.waitForNavigation({waitUntil:'domcontentloaded'}),nojs.getByRole('button',{name:'Türü uygula',exact:true}).click()]);check('kind_discovery_works_without_javascript',new URL(nojs.url()).searchParams.get('kind')==='philosophy'&&await cards(nojs).filter({hasText:'Felsefe'}).count()===await cards(nojs).count());await plain.close();
 check('no_application_console_or_runtime_errors',errors.length===0&&consoleErrors.length===0);check('no_csp_violations',violations.length===0);
 fs.writeFileSync(path.join(report,'public-browser.json'),JSON.stringify({count:Object.keys(checks).length,checks,errors,consoleErrors,violations,scope:'Real Chrome/native MVC/owned PostgreSQL; image/video references stubbed, no playback or upload claim.'},null,2)+'\n');console.log(JSON.stringify({count:Object.keys(checks).length,failed:[]}));
})().catch(e=>{fs.writeFileSync(path.join(report,'public-browser-failure.json'),JSON.stringify({checks,errors,consoleErrors,violations,error:e.stack},null,2));console.error(e.stack);process.exitCode=1;}).finally(async()=>await browser?.close());
