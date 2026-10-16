// Real browser/MVC date acceptance on owned PostgreSQL; restore every seeded timestamp.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR,server=JSON.parse(fs.readFileSync(process.env.DEVCORE_DATE_SERVER_REPORT,'utf8'));
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(process.env.DEVCORE_DOCUMENT_PG_PORT,'55485');fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],violations=[];let browser,original=[];
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p','55485','-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'}).trim();
const snapshot=()=>sql('SELECT to_jsonb(p)-\'ViewCount\' FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id"; SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";');
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const literal=v=>v===null?'NULL':"'"+v.replaceAll("'","''")+"'";
const restoreDates=()=>{for(const r of original)sql(`UPDATE "Posts" SET "PublishDate"=${literal(r.PublishDate)},"CreatedDate"=${literal(r.CreatedDate)},"UpdatedDate"=${literal(r.UpdatedDate)} WHERE "Id"=${r.Id}`);};
(async()=>{
 check('owned_fixture_verified',sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')==='f01-visible');const before=snapshot();
 const privateId=Number(sql('SELECT "Id" FROM "Posts" WHERE "ContentKind"=4 AND "AccessScope"=1 ORDER BY "Id" LIMIT 1'));assert.ok(privateId>0&&privateId!==2001);
 original=JSON.parse(sql(`SELECT json_agg(json_build_object('Id',"Id",'PublishDate',"PublishDate",'CreatedDate',"CreatedDate",'UpdatedDate',"UpdatedDate")) FROM "Posts" WHERE "Id" IN (2001,${privateId})`));
 sql(`UPDATE "Posts" SET "PublishDate"='2020-12-31T21:01:15Z',"CreatedDate"='2020-12-31T21:01:15Z',"UpdatedDate"='2026-09-30T22:30:15Z' WHERE "Id" IN (2001,${privateId})`);
 const seeded=snapshot(),storedTitle=sql('SELECT "Title" FROM "Posts" WHERE "Id"=2001');
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 for(const [index,locale,timezoneId] of [[0,'en-US','America/Los_Angeles'],[1,'fr-FR','Asia/Tokyo']]){
  const context=await browser.newContext({locale,timezoneId,viewport:{width:1440,height:1000}});
  await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
  await context.exposeBinding('__dateCsp',(_source,directive)=>violations.push(directive));await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__dateCsp(e.effectiveDirective)));
  const p=await context.newPage();p.on('pageerror',e=>errors.push(e.message));p.on('dialog',d=>d.dismiss());await p.goto(base+'/about');
  const outputs=await p.evaluate(async cases=>{const m=await import('/js/site-date.js');return cases.map(c=>({date:m.formatSiteDate(c.Iso,c.Zone),time:m.formatRecoveryDate(c.Iso,c.Zone)}));},server.cases);
  for(const [i,c] of server.cases.entries())check('real_Intl_matches_server_'+index+'_'+i,outputs[i].date===c.Date&&outputs[i].time===`${c.Date} ${c.Time} (${c.Zone})`);
  await p.goto(base+'/ara?query='+encodeURIComponent(storedTitle.slice(0,100)));const card=p.locator('a.post-card[href="/post/f01-visible"]');
  check('server_card_date_'+index,(await card.locator('time').innerText())==='01 Oca 2021'&&Date.parse(await card.locator('time').getAttribute('datetime'))===Date.parse('2020-12-31T21:01:15Z'));
  await p.goto(base+'/post/f01-visible');const times=await p.locator('.public-detail-meta time').allTextContents();check('server_detail_cross_midnight_dates_'+index,times[0]==='01 Oca 2021'&&times[1]==='01 Eki 2026');
  const schema=JSON.parse(await p.locator('script[type="application/ld+json"]').textContent());check('machine_UTC_dates_preserved_'+index,Date.parse(schema.datePublished)===Date.parse('2020-12-31T21:01:15Z')&&Date.parse(schema.dateModified)===Date.parse('2026-09-30T22:30:15Z'));
  for(const width of [1440,390]){await p.setViewportSize({width,height:1000});check('public_date_layout_'+index+'_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));}
  await p.goto(base+'/fixture-admin/login');await p.locator('#username').fill('f17-admin');await p.locator('#password').fill('f17-isolated-password');await p.getByRole('button',{name:'Giriş yap',exact:true}).click();await p.waitForURL('**/Admin/Dashboard');
  await p.goto(base+'/AdminPost?query='+encodeURIComponent(storedTitle.slice(0,100)));check('admin_created_date_'+index,(await p.locator('main').innerText()).includes('01 Oca 2021'));
  await p.goto(base+'/AdminNewsletter');check('newsletter_time_with_zone_'+index,(await p.locator('.newsletter-inventory').innerText()).includes('01 Oca 2021 00:01 (Europe/Istanbul)'));
  await p.goto(base+'/AdminWriting/Edit/2001');check('form_ISO_seconds_and_zone_'+index,await p.locator('[name="PublishDate"]').inputValue()==='2021-01-01T00:01:15'&&(await p.getByText('Site yayın zamanı (Europe/Istanbul)',{exact:true}).count())===1);
  // Both legacy and JSON recovery envelopes are real localStorage copies on the fixture origin.
  await p.goto(base+'/AdminWriting/Create');await p.getByRole('textbox',{name:'Yazı metni',exact:true}).waitFor();
  await p.evaluate(()=>{const form=document.querySelector('[data-document-form]')||document.querySelector('form[data-recovery-id]');const keys=['DocumentJson','Title','CategoryId','Summary','Excerpt','ThumbnailAlt','PublishDate','IsActive','EditVersion'];const fields=Object.fromEntries(keys.map(k=>[k,k==='IsActive'?form.querySelector('[name="IsActive"][type="checkbox"]').checked:form.elements.namedItem(k).value]));fields.Title='F45 tarihli kurtarma';localStorage.setItem('devcore_document_draft_v1_create',JSON.stringify({kind:'aether-notes-json-draft',version:1,documentVersion:1,postId:0,revision:crypto.randomUUID(),savedAt:'2026-09-30T22:30:15.000Z',fields}));});
  const jsonRaw=await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_create'));await p.reload();await p.locator('[data-recovery-panel]').waitFor({state:'visible'});
  check('bundled_JSON_recovery_date_'+index,(await p.locator('[data-recovery-date]').innerText())==='01 Eki 2026 01:30 (Europe/Istanbul)'&&await p.evaluate(()=>localStorage.getItem('devcore_document_draft_v1_create'))===jsonRaw);
  for(const width of [1440,390]){await p.setViewportSize({width,height:1000});await p.locator('[data-recovery-restore]').focus();check('recovery_layout_focus_'+index+'_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)&&await p.locator('[data-recovery-restore]').evaluate(el=>el===document.activeElement&&getComputedStyle(el).outlineStyle!=='none'));if(index===0)await p.screenshot({path:path.join(report,'date-recovery-'+width+'.png')});}
  await p.goto(base+'/AdminPost/Create');await p.locator('#Content').waitFor({state:'attached'});
  await p.evaluate(()=>{const f=document.querySelector('#postForm');const keys=['Title','Content','CategoryId','Summary','Excerpt','ThumbnailAlt','PublishDate','IsPublished','IsActive'];const fields=Object.fromEntries(keys.map(k=>[k,['IsPublished','IsActive'].includes(k)?(f.querySelector(`[name="${k}"][type="checkbox"]`)?.checked??f.elements.namedItem(k).value==='true'):f.elements.namedItem(k).value]));fields.Title='F45 eski kurtarma';localStorage.setItem('devcore_editor_draft_create',JSON.stringify({version:1,revision:crypto.randomUUID(),savedAt:'2026-09-30T22:30:15.000Z',fields}));});
  const legacyRaw=await p.evaluate(()=>localStorage.getItem('devcore_editor_draft_create'));await p.reload();await p.locator('#draft-recovery-banner').waitFor({state:'visible'});check('legacy_recovery_same_date_'+index,(await p.locator('#draft-timestamp').innerText())==='01 Eki 2026 01:30 (Europe/Istanbul)'&&await p.evaluate(()=>localStorage.getItem('devcore_editor_draft_create'))===legacyRaw);
  check('display_does_not_change_rows_'+index,snapshot()===seeded);await context.close();
 }
 restoreDates();original=[];check('all_seeded_dates_restored',snapshot()===before);check('no_runtime_or_CSP_errors',errors.length===0&&violations.length===0);
 fs.writeFileSync(path.join(report,'site-dates-browser.json'),JSON.stringify({passed:true,count:Object.keys(checks).length,checks,errors,violations,browserVersion:browser.version(),scope:'real Intl/MVC/owned PG; fonts stub; view counters excluded'},null,2));
})().catch(e=>{fs.writeFileSync(path.join(report,'failure.json'),JSON.stringify({passed:false,checks,errors,violations,message:e.message},null,2));console.error(e);process.exitCode=1;}).finally(async()=>{restoreDates();await browser?.close();});
