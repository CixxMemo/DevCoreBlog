"""Render observed operation timestamps in an owned auxiliary MVC process; no provider upload claim."""
import argparse,html,json,os,socket,subprocess,tempfile,time
from pathlib import Path
from http_probe_support import cookie_opener,submit_login,request,extract_antiforgery_token
p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
source=a.source.resolve();assert source.name=='source' and str(source).startswith('/private/tmp/devcoreblog-f17.') and not (source/'.git').exists()
assert (source/'.env').read_text()==''
port=15434
with socket.socket() as s:assert s.connect_ex(('127.0.0.1',port))!=0
fixture=source/'F45ObservedDateFixture.cs';assert not fixture.exists()
controller='''using DevCoreBlog.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
[Authorize]
public sealed class F45ObservedDateFixtureController : Controller
{
    [HttpPost("/f45-owned-media-date")]
    [ValidateAntiForgeryToken]
    public IActionResult Observe(bool success, [FromServices] MediaOperationStatus media)
    {
        media.Record(success);
        var snapshot = media.GetSnapshot();
        return Json(new { snapshot.LastCompleted, snapshot.LastSuccess });
    }
}
// Two distinct controlled days make last completion and last success independently observable.
public sealed class F45ObservationClock : TimeProvider
{
    private int _step;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2020,12,31,21,1,15,TimeSpan.Zero)
        .AddDays(Interlocked.Increment(ref _step)-1);
}
'''
program=source/'Program.cs';original_program=program.read_text()
registration='new DevCoreBlog.Services.Operations.MediaOperationStatus(mediaConfigured, TimeProvider.System)'
assert registration in original_program
proc=None;checks={};a.report.parent.mkdir(parents=True,exist_ok=True)
try:
    fixture.write_text(controller)
    program.write_text(original_program.replace(registration,'new DevCoreBlog.Services.Operations.MediaOperationStatus(mediaConfigured, new F45ObservationClock())'))
    with tempfile.TemporaryDirectory(prefix='devcore-f45-ops-',dir='/private/tmp') as temp:
        owned=Path(temp);binary=owned/'binary';keys=owned/'keys';keys.mkdir(mode=0o700)
        result=subprocess.run(['dotnet','build','DevCoreBlog.csproj','--no-restore','--disable-build-servers','-m:1','-nodeReuse:false','-p:NuGetAudit=false','-o',str(binary)],cwd=source,capture_output=True,text=True)
        if result.returncode:raise RuntimeError('Owned auxiliary MVC build failed')
        password='f45-owned-password'
        hashed=subprocess.check_output(['dotnet',str(source/'tools/DevCoreBlog.PasswordHashTool/bin/Debug/net10.0/DevCoreBlog.PasswordHashTool.dll'),'--stdin'],input=password+'\n',text=True).strip()
        env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ASPNETCORE_URLS':f'http://127.0.0.1:{port}','SITE_URL':'https://blog.example.test','SITE_TIME_ZONE':'Europe/Istanbul','DATA_PROTECTION_KEYS_PATH':str(keys),'DB_CONNECTION_STRING':f'Host=127.0.0.1;Port=55485;Database=devcoreblog_f01_test;Username={os.environ["USER"]}','ADMIN_LOGIN_PATH':'/fixture-admin/login','ADMIN_USERNAME':'f45-admin','ADMIN_PASSWORD_HASH':hashed,'ADMIN_SESSION_VERSION':'f45-owned','WEBHOOK_API_SECRET':'f45-fixture','CLOUDINARY_CLOUD_NAME':'','CLOUDINARY_API_KEY':'','CLOUDINARY_API_SECRET':''}
        with (owned/'application.log').open('w') as log:
            proc=subprocess.Popen(['dotnet',str(binary/'DevCoreBlog.dll')],cwd=source,env=env,stdout=log,stderr=subprocess.STDOUT)
            base=f'http://127.0.0.1:{port}';client,_=cookie_opener()
            for _ in range(120):
                if proc.poll() is not None:raise RuntimeError('Owned auxiliary MVC startup failed')
                try:
                    if request(client,base+'/fixture-admin/login')[0]==200:break
                except OSError:pass
                time.sleep(.1)
            else:raise RuntimeError('Owned auxiliary MVC not ready')
            _,_,status,url,_=submit_login(client,base,'f45-admin',password);assert status==200 and '/Admin/Dashboard' in url
            first_success=None
            for succeeded in [True,False]:
                _,_,body=request(client,base+'/Admin/Dashboard');token=extract_antiforgery_token(body)
                status,_,response=request(client,base+'/f45-owned-media-date',data={'success':str(succeeded).lower(),'__RequestVerificationToken':token});assert status==200
                snapshot=json.loads(response)
                if succeeded:first_success=snapshot['lastSuccess']
                else:checks['failure_preserves_distinct_last_success']=snapshot['lastSuccess']==first_success and snapshot['lastCompleted']!=first_success
                status,_,body=request(client,base+'/Admin/Dashboard');decoded=html.unescape(body)
                for name in ['lastCompleted','lastSuccess']:
                    instant=snapshot[name];assert instant
                    script="import {readFileSync} from 'node:fs'; const s=readFileSync(process.argv[1],'utf8');const m=await import('data:text/javascript;base64,'+Buffer.from(s).toString('base64'));console.log(m.formatRecoveryDate(process.argv[2],'Europe/Istanbul'));"
                    expected=subprocess.check_output(['node','--input-type=module','-e',script,str(source/'wwwroot/js/site-date.js'),instant],text=True).strip()
                    label=('Son yükleme: '+('Başarılı' if succeeded else 'Başarısız')+' — ') if name=='lastCompleted' else 'Son başarılı yükleme: '
                    checks[f'{succeeded}_{name}_same_site_time']=status==200 and label+expected in decoded and expected.endswith('(Europe/Istanbul)')
                checks[f'{succeeded}_observation_not_provider_health']='sağlayıcının şu an erişilebilir olduğunu kanıtlamaz' in decoded and 'Yapılandırılmadı' in decoded
            proc.terminate();proc.wait(timeout=10);proc=None
    fixture.unlink();program.write_text(original_program)
    checks['fixture_controller_removed']=not fixture.exists()
    checks['owned_program_restored']=program.read_text()==original_program
    assert all(checks.values()),checks
    a.report.write_text(json.dumps({'passed':True,'count':len(checks),'checks':checks,'scope':'Actual MVC/process observation; fixture clock and Record(true/false) are controlled, not a provider upload'},indent=2)+'\n')
finally:
    if proc is not None:proc.terminate();proc.wait(timeout=10)
    fixture.unlink(missing_ok=True)
    program.write_text(original_program)
