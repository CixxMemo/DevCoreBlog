// Anonymous real Chrome acceptance on the owned F32 MVC/PG fixture; no provider or account is used.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(process.env.DEVCORE_DOCUMENT_PG_PORT,'55485');fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],violations=[],responses=[];let browser,ids=[],active=[];
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p','55485','-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'}).trim();
const literal=v=>"'"+v.replaceAll("'","''")+"'";
const snapshot=()=>sql('SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id"; SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";');
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
function restore(){if(ids.length)sql('DELETE FROM "Posts" WHERE "Id" IN ('+ids.join(',')+')');if(active.length)sql('UPDATE "Posts" SET "IsActive"=true WHERE "Id" IN ('+active.join(',')+')');ids=[];active=[];}
(async()=>{
 check('owned_fixture_verified',sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')==='f01-visible');const before=snapshot();
 active=JSON.parse(sql('SELECT coalesce(json_agg("Id"),\'[]\') FROM "Posts" WHERE "IsActive" AND "ContentKind"=4 AND "AccessScope"=1'));
 sql('UPDATE "Posts" SET "IsActive"=false WHERE "ContentKind"=4 AND "AccessScope"=1');const cat=Number(sql('SELECT "Id" FROM "Categories" WHERE "IsActive" ORDER BY "Id" LIMIT 1'));
 const secret='F32_BROWSER_PRIVATE_BODY',title='<script>window.f32Injected=1</script> & Başlık',summary='😀'.repeat(320)+'F32_BROWSER_PRIVATE_TAIL';
 for(let i=0;i<11;i++){const id=942000+i;ids.push(id);const t=i===0?title:i===1?'T'.repeat(200):'Tanıtım '+i;const s=i===0?summary:i===1?'U'.repeat(500):i===2?'':'Kısa özet '+i;
  sql(`INSERT INTO "Posts" ("Id","Title","Slug","Summary","Content","Excerpt","ThumbnailUrl","CategoryId","CreatedDate","IsActive","ViewCount","PublishDate","IsPublished","ContentKind","AccessScope","EditVersion") VALUES (${id},${literal(t)},'f32-browser-${i}',${literal(s)},${literal(secret)},'F32_BROWSER_PRIVATE_EXCERPT','https://example.test/F32_BROWSER_PRIVATE_COVER.png',${cat},'2020-01-01',true,111,'2020-01-01',true,4,1,1)`);
 }
 const seeded=snapshot();browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});const c=await browser.newContext({viewport:{width:1440,height:800}});
 await c.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 await c.exposeBinding('__teaserCsp',(_s,d)=>violations.push(d));await c.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__teaserCsp(e.effectiveDirective)));
 const p=await c.newPage();p.on('pageerror',e=>errors.push(e.message));p.on('response',r=>{if(r.url().startsWith(base+'/bulten'))responses.push(r.text());});
 await p.goto(base+'/');await p.getByRole('link',{name:'Bülten',exact:true}).click();await p.waitForURL(base+'/bulten');
 check('native_navigation_and_200',await p.locator('#newsletter-teaser-heading').innerText()==='Bülten'&&await p.locator('.newsletter-teaser').count()===9&&await p.locator('html').getAttribute('lang')==='tr');
 check('exact_allowed_summary',(await p.locator('.newsletter-teaser').first().locator('p').innerText())==='😀'.repeat(320));
 check('only_title_summary_DOM',await p.locator('.newsletter-teaser img,.newsletter-teaser time,.newsletter-teaser script,.newsletter-teaser iframe,.newsletter-teaser a,.newsletter-teaser input').count()===0&&await p.locator('.newsletter-teaser').first().locator('h2').innerText()===title);
 check('literal_markup_does_not_execute',await p.evaluate(()=>window.f32Injected===undefined)&&await p.locator('.newsletter-teaser script').count()===0);
 check('no_body_media_or_tail_in_HTML',(await p.content()).includes(secret)===false&&!(await p.content()).includes('F32_BROWSER_PRIVATE_TAIL')&&!(await p.content()).includes('F32_BROWSER_PRIVATE_COVER'));
 check('empty_summary_has_no_fallback',await p.locator('.newsletter-teaser').nth(2).locator('p').count()===0);
 check('reader_entry_without_private_body_links',await p.locator('.newsletter-teasers a[href="/sign-in"]').count()===1&&await p.locator('.newsletter-teasers a[href="/bultenler"]').count()===1&&await p.locator('.newsletter-teaser a').count()===0);
 check('static_metadata_and_no_article_schema',(await p.title())==='Bülten — Aether Letter'&&await p.locator('script[type="application/ld+json"]').count()===0&&await p.locator('link[rel="canonical"]').getAttribute('href')==='https://blog.example.test/bulten');
 check('no_admin_or_archive_body_link',await p.locator('a[href*="/Admin"],a[href*="fixture-admin"]').count()===0&&await p.locator('.newsletter-teaser a').count()===0);
 const first=await p.locator('.newsletter-teaser h2').allTextContents();const next=p.getByRole('link',{name:'Sonraki',exact:true});await next.focus();check('keyboard_next_focus',await next.evaluate(el=>el===document.activeElement&&getComputedStyle(el).outlineStyle!=='none'));await p.keyboard.press('Enter');await p.waitForURL('**/bulten?page=2');
 const second=await p.locator('.newsletter-teaser h2').allTextContents();check('native_disjoint_paging',second.length===2&&!second.some(t=>first.includes(t))&&await p.locator('link[rel="canonical"]').getAttribute('href')==='https://blog.example.test/bulten?page=2');
 await p.getByRole('link',{name:'Önceki',exact:true}).click();await p.waitForURL('**/bulten?page=1');
 for(const width of [1440,390]){await p.setViewportSize({width,height:800});check('long_metadata_no_overflow_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth&&document.querySelector('.newsletter-teasers').scrollWidth<=document.querySelector('.newsletter-teasers').clientWidth));await p.evaluate(async()=>{scrollTo(0,0);await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));});await p.screenshot({path:path.join(report,'teasers-'+width+'.png')});await p.getByRole('navigation',{name:'Bülten tanıtımı sayfaları'}).scrollIntoViewIfNeeded();await p.screenshot({path:path.join(report,'teasers-paging-'+width+'.png')});}
 await p.setViewportSize({width:1440,height:800});await p.goto(base+'/bulten');await p.locator('#theme-toggle-btn').click();await p.waitForFunction(()=>getComputedStyle(document.body).color==='rgb(244, 244, 245)');
 check('dark_text_readable',await p.locator('.newsletter-teaser').first().evaluate(el=>getComputedStyle(el).color==='rgb(244, 244, 245)'));await p.screenshot({path:path.join(report,'teasers-dark.png')});
 const nojs=await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:800}}),n=await nojs.newPage();await n.goto(base+'/bulten');check('no_JS_teasers_and_paging',await n.locator('.newsletter-teaser').count()===9&&await n.getByRole('link',{name:'Sonraki',exact:true}).isVisible());await nojs.close();
 const bodies=await Promise.all(responses);check('network_responses_contain_no_private_bytes',bodies.length>=3&&bodies.every(b=>!b.includes(secret)&&!b.includes('F32_BROWSER_PRIVATE_EXCERPT')&&!b.includes('F32_BROWSER_PRIVATE_COVER')&&!b.includes('F32_BROWSER_PRIVATE_TAIL')));
 check('anonymous_reads_write_nothing',snapshot()===seeded);check('no_runtime_or_CSP_errors',errors.length===0&&violations.length===0);restore();check('owned_data_restored',snapshot()===before);
 fs.writeFileSync(path.join(report,'teasers-browser.json'),JSON.stringify({passed:true,count:Object.keys(checks).length,checks,errors,violations,browserVersion:browser.version(),scope:'real anonymous Chrome/MVC/owned PG; fonts stub'},null,2));
})().catch(e=>{fs.writeFileSync(path.join(report,'failure.json'),JSON.stringify({passed:false,checks,errors,violations,message:e.message},null,2));console.error(e);process.exitCode=1;}).finally(async()=>{restore();await browser?.close();});
