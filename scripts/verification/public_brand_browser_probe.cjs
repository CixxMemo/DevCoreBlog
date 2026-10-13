// Real MVC/Chrome acceptance on the owned F43 fixture; authored content remains untouched.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const {chromium}=require(process.env.DEVCORE_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.DEVCORE_DOCUMENT_BASE_URL,report=process.env.DEVCORE_DOCUMENT_REPORT_DIR;
assert.equal(new URL(base).hostname,'127.0.0.1');assert.equal(process.env.DEVCORE_DOCUMENT_PG_PORT,'55485');
fs.mkdirSync(report,{recursive:true});const checks={},errors=[],violations=[];let browser;
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
const sql=q=>cp.execFileSync('psql',['-X','-h','127.0.0.1','-p','55485','-d','devcoreblog_f01_test','-At','-v','ON_ERROR_STOP=1','-c',q],{encoding:'utf8'}).trim();
const snapshot=()=>sql('SELECT to_jsonb(p)-\'ViewCount\' FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id"; SELECT to_jsonb(s) FROM "Subscribers" s ORDER BY "Id";');
(async()=>{
 check('synthetic_fixture_verified',sql('SELECT "Slug" FROM "Posts" WHERE "Id"=2001')==='f01-visible');const before=snapshot();
 browser=await chromium.launch({headless:true,executablePath:process.env.DEVCORE_BROWSER_EXECUTABLE});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.route('https://fonts.googleapis.com/**',r=>r.fulfill({contentType:'text/css',body:''}));
 await context.exposeBinding('__brandCsp',(_source,directive)=>violations.push(directive));
 await context.addInitScript(()=>document.addEventListener('securitypolicyviolation',e=>void window.__brandCsp(e.effectiveDirective)));
 const p=await context.newPage();p.on('pageerror',e=>errors.push(e.message));
 for(const route of ['/','/about','/contact','/sign-in','/ara?query=F43_NO_MATCH','/category/f01-active','/post/f01-visible']){
  const response=await p.goto(base+route);const body=await p.locator('body').innerText();
  check('brand_language_'+route,response.status()===200&&await p.locator('html').getAttribute('lang')==='tr'&&(await p.title()).includes('Aether Notes')&&!body.toLowerCase().includes('devcore')&&await p.locator('meta[property="og:site_name"]').getAttribute('content')==='Aether Notes');
  check('no_admin_navigation_'+route,await p.locator('a[href*="Account/Login"],a[href*="fixture-admin"],a[href*="/Admin"]').count()===0);
  check('canonical_origin_'+route,(await p.locator('link[rel="canonical"]').getAttribute('href')).startsWith('https://blog.example.test/'));
 }
 await p.goto(base+'/sign-in');check('reader_unavailability_honest',(await p.locator('main').innerText()).includes('henüz hazır değil')&&(await p.locator('main').innerText()).includes('henüz kullanıma açılmadı')&&await p.locator('form').count()===1);
 await p.goto(base+'/about');check('owner_identity_preserved',(await p.locator('main').innerText()).includes('Mehmet Can')&&await p.locator('a[href="https://github.com/CixxMemo"]').count()===1);
 await p.goto(base+'/contact');check('native_contact_preserved',await p.locator('a[href="mailto:mehmet.kocakurt.can@gmail.com"]').count()===1&&(await p.locator('main').innerText()).includes('mesaj toplanmaz'));
 await p.goto(base+'/');const search=p.getByRole('search',{name:'Yazı arama'});await search.getByRole('searchbox').fill('F43_NO_MATCH');await search.getByRole('button',{name:'Ara',exact:true}).click();await p.waitForURL('**/ara?query=F43_NO_MATCH');
 check('native_search_empty_turkish',(await p.locator('h1').innerText())==='Arama sonuçları'&&(await p.locator('.public-empty-state').innerText()).includes('yazı bulunamadı'));
 await p.getByRole('link',{name:'Ana sayfaya dön',exact:true}).click();await p.waitForURL(base+'/');
 check('home_count_is_actual',Number((await p.locator('main header p').innerText()).split(' ')[0])===Number(sql('SELECT count(*) FROM "Posts" p JOIN "Categories" c ON c."Id"=p."CategoryId" WHERE p."IsActive" AND p."IsPublished" AND p."PublishDate"<=CURRENT_TIMESTAMP AND c."IsActive" AND p."AccessScope"=0')));
 check('card_metadata_turkish',(await p.locator('.post-metadata').first().innerText()).includes('görüntüleme'));
 check('ranking_honest_label',(await p.locator('#most-read').innerText()).includes('botlar da dahildir'));
 const oldTitle=sql('SELECT "Title" FROM "Posts" WHERE "Id"=2001');await p.goto(base+'/post/f01-visible');check('authored_title_preserved',await p.locator('h1').innerText()===oldTitle);
 for(const route of ['/','/about','/contact','/sign-in','/ara?query=F43_NO_MATCH'])for(const width of [1440,390]){
  await p.setViewportSize({width,height:1000});await p.goto(base+route);
  check('no_overflow_'+route+'_'+width,await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 }
 await p.goto(base+'/');await p.getByRole('button',{name:'Gezinme menüsünü aç',exact:true}).click();
 check('mobile_drawer_trap_localized',await p.getByRole('dialog',{name:'Okuyucu gezinmesi'}).isVisible()&&await p.getByRole('button',{name:'Gezinme menüsünü kapat'}).evaluate(el=>el===document.activeElement));
 await p.keyboard.press('Shift+Tab');await p.keyboard.press('Shift+Tab');check('mobile_focus_wrap',await p.getByRole('button',{name:'Açık ve koyu tema arasında geçiş yap'}).evaluate(el=>el===document.activeElement));
 await p.keyboard.press('Escape');check('escape_restores_focus',await p.getByRole('button',{name:'Gezinme menüsünü aç'}).evaluate(el=>el===document.activeElement));
 await p.screenshot({path:path.join(report,'public-home-390.png'),fullPage:true});
 await p.setViewportSize({width:1440,height:1000});await p.goto(base+'/');await p.keyboard.press('Tab');const skip=p.getByRole('link',{name:'İçeriğe geç',exact:true});
 check('skip_keyboard_focus',await skip.evaluate(el=>el===document.activeElement&&getComputedStyle(el).outlineStyle!=='none'));await p.keyboard.press('Enter');check('skip_native_target',new URL(p.url()).hash==='#main-content'&&await p.locator('main').evaluate(el=>el===document.activeElement));
 const theme=p.getByRole('button',{name:'Açık ve koyu tema arasında geçiş yap'});const dark=await p.locator('html').evaluate(el=>el.classList.contains('dark'));await theme.click();check('theme_still_works',await p.locator('html').evaluate(el=>el.classList.contains('dark'))!==dark);
 await p.waitForFunction(dark=>getComputedStyle(document.body).color===(dark?'rgb(244, 244, 245)':'rgb(17, 24, 39)'),!dark);
 const contrast=await p.locator('#most-read').evaluate(el=>{const lum=value=>{const c=value.match(/\d+/g).slice(0,3).map(v=>{v=Number(v)/255;return v<=.04045?v/12.92:((v+.055)/1.055)**2.4;});return .2126*c[0]+.7152*c[1]+.0722*c[2];};const cs=getComputedStyle(el),a=lum(cs.color),b=lum(cs.backgroundColor);return (Math.max(a,b)+.05)/(Math.min(a,b)+.05);});
 check('settled_theme_text_contrast',contrast>=7);
 await p.screenshot({path:path.join(report,'public-home-1440.png'),fullPage:true});
 await p.emulateMedia({reducedMotion:'reduce'});check('reduced_motion_respected',await p.locator('body').evaluate(el=>getComputedStyle(el).transitionDuration.split(',').every(v=>v.trim()==='0s')));
 for(const route of ['/F43-missing','/Home/Error']){const r=await p.goto(base+route);check('real_error_status_turkish_'+route,r.status()===(route==='/Home/Error'?500:404)&&await p.locator('html').getAttribute('lang')==='tr'&&(await p.title()).includes('Aether Notes')&&(await p.locator('body').innerText()).includes('İstek kimliği:'));
 }
 const invalid=await context.request.get(base+'/ara?page=0');check('invalid_list_400_turkish',invalid.status()===400&&(await invalid.text()).includes('en çok 100 karakter'));
 const nojs=await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:1000}});const n=await nojs.newPage();await n.goto(base+'/');check('no_js_navigation_search_usable',await n.getByRole('link',{name:'Hakkında',exact:true}).isVisible()&&await n.getByRole('searchbox',{name:'Yazılarda ara',exact:true}).isVisible());await nojs.close();
 const reflected='<script>window.f43Injected=1</script>';await p.goto(base+'/ara?query='+encodeURIComponent(reflected));
 check('search_reflection_encoded',await p.getByRole('searchbox',{name:'Yazılarda ara',exact:true}).inputValue()===reflected&&await p.evaluate(()=>window.f43Injected===undefined));
 const activePosts=JSON.parse(sql(`SELECT coalesce(json_agg(json_build_object('id',"Id",'active',"IsActive")),'[]') FROM "Posts"`));
 const activeCategories=JSON.parse(sql(`SELECT coalesce(json_agg(json_build_object('id',"Id",'active',"IsActive")),'[]') FROM "Categories"`));
 const restore=(table,rows)=>{if(rows.length)sql(`UPDATE "${table}" t SET "IsActive"=v.active FROM (VALUES ${rows.map(r=>{assert.ok(Number.isInteger(r.id)&&typeof r.active==='boolean');return `(${r.id},${r.active})`;}).join(',')}) AS v(id,active) WHERE t."Id"=v.id`);};
 try {
  sql('UPDATE "Posts" SET "IsActive"=false');await p.goto(base+'/?f43=empty-posts');
  check('empty_home_honest',await p.locator('.post-card').count()===0&&(await p.locator('.public-empty-state').innerText())==='Henüz yayımlanmış yazı yok.'&&(await p.locator('#most-read').innerText()).includes('Henüz sıralanabilecek'));
  await p.goto(base+'/category/f01-active');check('empty_category_turkish',(await p.locator('.public-empty-state').innerText())==='Bu kategoride henüz yayımlanmış yazı yok.');
  sql('UPDATE "Categories" SET "IsActive"=false');await p.goto(base+'/?f43=empty-topics');check('empty_topics_turkish',(await p.locator('main').innerText()).includes('Henüz etkin konu yok.'));
 } finally {restore('Posts',activePosts);restore('Categories',activeCategories);}
 check('stored_content_metadata_unchanged',snapshot()===before);check('no_runtime_or_csp_errors',errors.length===0&&violations.length===0);
 fs.writeFileSync(path.join(report,'public-brand-browser.json'),JSON.stringify({passed:true,count:Object.keys(checks).length,checks,errors,violations,browserVersion:browser.version(),fontNetwork:'Stub CSS; no external font claim',databaseScope:'owned synthetic; detail counters excluded from content snapshot'},null,2));
})().catch(e=>{fs.writeFileSync(path.join(report,'failure.json'),JSON.stringify({passed:false,checks,errors,violations,message:e.message},null,2));console.error(e);process.exitCode=1;}).finally(async()=>{await browser?.close();});
