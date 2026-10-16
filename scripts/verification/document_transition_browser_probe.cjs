// Real Chrome checks migrated canonical facts on an explicitly owned, presentation-only clone.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const evidence=process.env.DEVCORE_TRANSITION_EVIDENCE,base=process.env.DEVCORE_DOCUMENT_BASE_URL;
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(new URL(base).port,'15431');
const snapshot=JSON.parse(fs.readFileSync(path.join(evidence,'clone-after.json'),'utf8')),posts=JSON.parse(snapshot.posts);
const checks={},errors=[],violations=[];const normal=t=>t.replace(/\s+/gu,' ').trim();let browser;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
(async()=>{
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 const publicContext=await browser.newContext({viewport:{width:1440,height:1000}});
 await publicContext.exposeBinding('__transitionCsp',(_s,d)=>violations.push(d));
 await publicContext.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>window.__transitionCsp(e.effectiveDirective)));
 await publicContext.route('**/*',route=>{
  const request=route.request();if(new URL(request.url()).origin!==new URL(base).origin && request.resourceType()==='image') return route.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC0lEQVR42mP8/x8AAwMCAO+/lSUAAAAASUVORK5CYII=','base64')});
  return route.continue();
 });
 const page=await publicContext.newPage();page.on('pageerror',e=>errors.push(e.name));page.on('console',m=>{if(m.type()==='error'&&!m.text().startsWith('Failed to load resource:'))errors.push('console-error')});
 for(const post of posts){
  const response=await page.goto(base+'/post/'+post.Slug,{waitUntil:'domcontentloaded'});
  check('public_migrated_'+post.Id,response.status()===200&&normal(await page.locator('.document-content').innerText())===normal(post.DocumentPlainText));
  const schema=JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());check('facts_and_seo_'+post.Id,schema.wordCount===post.DocumentWordCount&&await page.locator('.document-content script').count()===0);
 }
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:1000});check('no_horizontal_overflow_'+width,await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  const screenshot=path.join(evidence,'browser-'+width+'.png');await page.screenshot({path:screenshot,fullPage:true});fs.chmodSync(screenshot,0o600);
 }
 const admin=await browser.newContext();const edit=await admin.newPage();edit.on('pageerror',e=>errors.push(e.name));
 await edit.goto(base+'/fixture-admin/login');await edit.locator('#username').fill('f18-admin');await edit.locator('#password').fill('f18-isolated-password');await edit.getByRole('button',{name:'Giriş yap',exact:true}).click();await edit.waitForURL('**/Admin/Dashboard');
 for(const post of posts){
  const response=await edit.goto(base+'/AdminWriting/Edit/'+post.Id,{waitUntil:'domcontentloaded'});
  await edit.locator('.tiptap').waitFor({state:'visible'});check('editor_migrated_'+post.Id,response.status()===200&&normal(await edit.locator('.tiptap').innerText())===normal(post.DocumentPlainText)&&await edit.locator('[name="DocumentJson"]').inputValue()===post.DocumentJson);
 }
 check('no_runtime_or_csp_error',!errors.length&&!violations.length);
})().catch(e=>{errors.push(e.message);process.exitCode=1}).finally(async()=>{
 fs.writeFileSync(path.join(evidence,'transition-browser.json'),JSON.stringify({passed:!errors.length,count:Object.keys(checks).length,checks,errors,violations,scope:'Real Chrome/MVC on restored clone; media image responses stubbed; no provider upload/readability claim'},null,2));
 if(browser)await browser.close();console.log('Migration browser checks:',Object.keys(checks).length,'errors:',errors.length);
});
