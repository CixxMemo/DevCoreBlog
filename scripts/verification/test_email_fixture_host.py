"""Owned source-copy host: real MVC/data, synthetic sender/clock only. Never installed in application code."""
import json,os,shutil,subprocess
from pathlib import Path

def build_fixture_host(root,fixture,env,patch=None):
 source=fixture/'source';source.mkdir()
 # A normal quality source has already been sanitized. A checkout uses only Git's source inventory.
 probe=subprocess.run(['git','rev-parse','--show-toplevel'],cwd=root,capture_output=True,text=True)
 if probe.returncode==0 and Path(probe.stdout.strip()).resolve()==root.resolve():
  files=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=root).decode().split('\0')
 else:
  files=[]
  for directory,names,entries in os.walk(root):
   names[:]=[n for n in names if n not in ('bin','obj','node_modules','.git','.local','docs','.agents','.codex','__pycache__')]
   files.extend(str((Path(directory)/n).relative_to(root)) for n in entries)
 for name in set(files):
  if not name:continue
  parts=Path(name).parts
  if any(p in ('bin','obj','node_modules','.git','.local','docs','.agents','.codex','__pycache__','.auth') or p.startswith('.env') or p.startswith('cookies') for p in parts) or name.startswith('wwwroot/generated/'):continue
  path=root/name
  if path.is_file() and not path.is_symlink():
   dest=source/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest)
 if not (root/'node_modules').is_dir():raise RuntimeError('Locked local frontend dependencies unavailable')
 (source/'node_modules').symlink_to(root/'node_modules',target_is_directory=True)
 packet=fixture/'packets.jsonl';control=fixture/'mode.txt';clock=fixture/'clock.txt';gate=fixture/'transport.block';control.write_text('accepted')
 marker='var app = builder.Build();';program=source/'Program.cs';text=program.read_text()
 if text.count(marker)!=1:raise RuntimeError('Owned fixture DI patch boundary changed')
 program.write_text(text.replace(marker,'builder.Services.AddSingleton<DevCoreBlog.Core.Email.IEmailSender, DevCoreBlog.Fixture.TestSender>();\nbuilder.Services.AddSingleton<TimeProvider, DevCoreBlog.Fixture.TestClock>();\n'+marker))
 code='''using DevCoreBlog.Core.Email;
namespace DevCoreBlog.Fixture;
internal sealed class TestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => File.Exists(CLOCK) ? DateTimeOffset.Parse(File.ReadAllText(CLOCK), null, global::System.Globalization.DateTimeStyles.RoundtripKind) : DateTimeOffset.UtcNow;
}
internal sealed class TestSender : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken)
    {
        var packet = System.Text.Json.JsonSerializer.Serialize(new { request.Recipient, request.Subject, request.Html, request.Text, request.IdempotencyKey, request.Purpose, unsubscribe=request.Unsubscribe?.ToString() });
        await File.AppendAllTextAsync(PACKET, packet+"\\n", cancellationToken);
        while(File.Exists(GATE)) await Task.Delay(25, cancellationToken);
        return File.ReadAllText(CONTROL).Trim() switch {
            "uncertain" => new(EmailSendOutcome.Uncertain,EmailSendReason.Timeout),
            "rejected" => new(EmailSendOutcome.Rejected,EmailSendReason.Validation,HttpStatus:422),
            _ => new(EmailSendOutcome.Accepted,EmailSendReason.None,Guid.NewGuid(),200)
        };
    }
}
'''
 for key,path in [('CLOCK',clock),('PACKET',packet),('CONTROL',control),('GATE',gate)]:code=code.replace(key,json.dumps(str(path)))
 (source/'F38Fixture.cs').write_text(code)
 if patch is not None:patch(source)
 built=subprocess.run(['dotnet','build','DevCoreBlog.csproj','--disable-build-servers'],cwd=source,env=env,capture_output=True,text=True,timeout=150)
 if built.returncode:
  errors=[line.strip().replace(str(fixture),'owned').replace(str(root),'source') for line in built.stdout.splitlines() if 'error ' in line.lower()]
  raise RuntimeError('Owned MVC fixture host build failed: '+' | '.join(errors[:3]))
 return source,packet,control,clock,gate
