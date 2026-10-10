// Final real Chrome acceptance against already uploaded/saved synthetic media; no new upload.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR,id=process.env.DEVCORE_MEDIA_DOCUMENT_ID;
assert.equal(new URL(base).hostname,'127.0.0.1'); assert.match(id,/^[1-9][0-9]*$/);
const pgPort=process.env.DEVCORE_MEDIA_DATABASE_PORT; assert.match(pgPort,/^[1-9][0-9]*$/);
const sql=query=>require('node:child_process').execFileSync('psql',['-X','-h','127.0.0.1','-p',pgPort,'-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',query],{encoding:'utf8'}).trim();
fs.mkdirSync(report,{recursive:true});
const checks={},errors=[],violations=[];let browser;
function check(key,value){checks[key]=!!value;assert.ok(value,key);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 const context=await browser.newContext({viewport:{width:1440,height:1100}});
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>window.__mediaViolations=[...(window.__mediaViolations||[]),e.effectiveDirective]));
 const page=await context.newPage();page.on('dialog',d=>d.accept());page.on('pageerror',e=>errors.push(e.name));
 page.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))errors.push(m.text().slice(0,200));});
 await page.goto(base+'/fixture-admin/login');await page.locator('#username').fill('f17-admin');await page.locator('#password').fill('f17-isolated-password');
 await page.getByRole('button',{name:'Sign In',exact:true}).click();await page.waitForURL('**/Admin/Dashboard');
 await page.goto(base+'/AdminWriting/Edit/'+id);
 const editor=page.getByRole('textbox',{name:'Yazı metni',exact:true});await editor.waitFor();
 const image=editor.locator('img');await image.first().evaluate(el=>el.decode());await image.last().evaluate(el=>el.decode());
 check('actual_saved_png_and_gif_reopen_without_dimensions_or_style',await image.count()===2 && await image.first().evaluate(el=>el.naturalWidth===64&&el.naturalHeight===32) && await image.last().evaluate(el=>el.naturalWidth===32&&el.naturalHeight===32) && await editor.locator('[style]').count()===0);
 check('saved_three_hundred_rune_alt_and_decorative_gif_reopen',[...(await image.first().getAttribute('alt'))].length===300 && await image.last().getAttribute('alt')==='');
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:1100});
  if(width<768)await page.waitForFunction(()=>getComputedStyle(document.querySelector('#admin-sidebar')).visibility==='hidden');
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const geometry=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,styles:document.querySelectorAll('.writing-document [style]').length}));
  check('viewport_'+width+'_has_no_page_overflow_or_dynamic_styles',geometry.scroll<=geometry.width&&geometry.styles===0);
  fs.writeFileSync(path.join(report,'geometry-'+width+'.json'),JSON.stringify(geometry,null,2)+'\n');
  await page.locator('[data-image-file]').focus();check('keyboard_focus_'+width,await page.locator('[data-image-file]').evaluate(el=>document.activeElement===el));
  await page.screenshot({path:path.join(report,'images-'+width+'.png'),fullPage:true});
  const styles=await editor.locator('img[style]').evaluateAll(elements=>elements.map(el=>el.getAttribute('style')));
  check('screenshot_instrumentation_adds_no_css_'+width,styles.every(style=>style===''));
  await editor.locator('img[style=""]').evaluateAll(elements=>elements.forEach(el=>el.removeAttribute('style')));
 }
 const frames=new Set();for(let sample=0;sample<6;sample++){
  const bytes=await image.last().screenshot({animations:'allow'});frames.add(require('node:crypto').createHash('sha256').update(bytes).digest('hex'));await page.waitForTimeout(220);
 }
 check('real_gif_visibly_animates_in_chrome',frames.size>1);
 const before=await page.locator('[name=DocumentJson]').inputValue();
 const revision=await page.locator('[name=EditVersion]').inputValue();
 const storedBefore=sql(`SELECT to_jsonb(p) FROM "Posts" p WHERE "Id"=${id};`);
 sql(`CREATE FUNCTION f12_browser_refuse() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic browser write refusal'; END $$;
 CREATE TRIGGER f12_browser_refuse BEFORE UPDATE ON "Posts" FOR EACH ROW WHEN (NEW."Id"=${id}) EXECUTE FUNCTION f12_browser_refuse();`);
 try {
  await page.locator('#post-title-input').fill('F12 korunacak gerçek medya');
  const failure=page.waitForResponse(r=>r.request().method()==='POST'&&r.url().includes('/AdminWriting/'));
  await page.locator('[data-writing-save]').first().click();check('native_real_database_write_failure_is_503',(await failure).status()===503);
  await page.waitForLoadState();await editor.waitFor();
  check('native_503_keeps_exact_media_document_and_revision',await page.locator('[name=DocumentJson]').inputValue()===before && await page.locator('[name=EditVersion]').inputValue()===revision && await page.locator('#post-title-input').inputValue()==='F12 korunacak gerçek medya');
  check('real_write_failure_keeps_original_database_row',sql(`SELECT to_jsonb(p) FROM "Posts" p WHERE "Id"=${id};`)===storedBefore);
 } finally {sql('DROP TRIGGER f12_browser_refuse ON "Posts"; DROP FUNCTION f12_browser_refuse();');}
 await page.locator('#post-title-input').fill('F12 tekrar gerçek kayıt');
 const saved=page.waitForResponse(r=>r.request().method()==='POST'&&r.url().includes('/AdminWriting/'));
 await page.locator('[data-writing-save]').first().click();check('native_mobile_save_302',(await saved).status()===302);await page.waitForLoadState();await editor.waitFor();
 check('mobile_native_round_trip_keeps_exact_image_document',await page.locator('[name=DocumentJson]').inputValue()===before);
 violations.push(...await page.evaluate(()=>window.__mediaViolations||[]));
 check('no_runtime_or_console_errors',errors.length===0);check('no_csp_violations',violations.length===0);
})().catch(e=>{checks.completed=false;console.error(e.name+': '+e.message.slice(0,160));process.exitCode=1;})
.finally(async()=>{fs.writeFileSync(path.join(report,'images-final-browser.json'),JSON.stringify({count:Object.keys(checks).length,checks,errors,violations,scope:'Real existing Cloudinary media, owned PostgreSQL and Chrome. No provider replay or new upload.'},null,2)+'\n');if(browser)await browser.close();console.log(JSON.stringify({count:Object.keys(checks).length,failed:Object.keys(checks).filter(k=>!checks[k])}));});
