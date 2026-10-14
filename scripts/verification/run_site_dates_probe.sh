#!/bin/sh
set -eu
# Link actual presentation sources into an owned package-free harness; no application DB is used.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcore-site-dates.XXXXXX)
trap 'rm -rf -- "$task_tmp"' EXIT INT TERM
task_report=${1:?A report directory is required}
mkdir -p "$task_report"
cat > "$task_tmp/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include="$task_repo/Models/Presentation/SiteDateFormatter.cs" Link="SiteDateFormatter.cs"/><Compile Include="$task_repo/DevCoreBlog.Services/Publishing/PublicationTimeZone.cs" Link="PublicationTimeZone.cs"/></ItemGroup></Project>
EOF
cat > "$task_tmp/Program.cs" <<'CS'
using System.Globalization;
using System.Text.Json;
using DevCoreBlog.Models.Presentation;
using DevCoreBlog.Services.Publishing;
var checks = new Dictionary<string,bool>();
void Check(string name, bool ok) { checks.Add(name,ok); if (!ok) throw new Exception(name); }
var cases = new[] {
    new { Iso="2026-09-30T22:30:15Z", Zone="Europe/Istanbul", Date="01 Eki 2026", Time="01:30" },
    new { Iso="2020-12-31T21:01:00Z", Zone="Europe/Istanbul", Date="01 Oca 2021", Time="00:01" },
    new { Iso="2015-01-01T21:30:00Z", Zone="Europe/Istanbul", Date="01 Oca 2015", Time="23:30" },
    new { Iso="2015-07-01T21:30:00Z", Zone="Europe/Istanbul", Date="02 Tem 2015", Time="00:30" },
    new { Iso="2026-03-08T06:59:00Z", Zone="America/New_York", Date="08 Mar 2026", Time="01:59" },
    new { Iso="2026-03-08T07:01:00Z", Zone="America/New_York", Date="08 Mar 2026", Time="03:01" },
    new { Iso="2026-11-01T05:30:00Z", Zone="America/New_York", Date="01 Kas 2026", Time="01:30" },
    new { Iso="2026-11-01T06:30:00Z", Zone="America/New_York", Date="01 Kas 2026", Time="01:30" },
    new { Iso="2000-02-29T00:00:00Z", Zone="Etc/UTC", Date="29 Şub 2000", Time="00:00" },
    new { Iso="0001-01-01T00:00:00Z", Zone="Etc/UTC", Date="01 Oca 0001", Time="00:00" }
};
foreach (var culture in new[] { "en-US", "fr-FR", "tr-TR" }) {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
    foreach (var item in cases) {
        var instant=DateTimeOffset.Parse(item.Iso,CultureInfo.InvariantCulture);
        var zone=new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById(item.Zone));
        var f=new SiteDateFormatter(zone);
        Check(culture+item.Iso+item.Zone+"_year",f.Year(instant.UtcDateTime)==item.Date[^4..]);
        Check(culture+item.Iso+item.Zone+"_date",f.Date(instant.UtcDateTime)==item.Date);
        Check(culture+item.Iso+item.Zone+"_time",f.DateAndTime(instant)==$"{item.Date} {item.Time} ({item.Zone})");
        Check(culture+item.Iso+item.Zone+"_legacy_kind",f.Date(DateTime.SpecifyKind(instant.UtcDateTime,DateTimeKind.Unspecified))==item.Date);
    }
}
var months=new[]{"Oca","Şub","Mar","Nis","May","Haz","Tem","Ağu","Eyl","Eki","Kas","Ara"};
var istanbul=new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
var dates=new SiteDateFormatter(istanbul);
for(var month=1;month<=12;month++) Check("month_"+month,dates.Date(new DateTime(2026,month,15,12,0,0,DateTimeKind.Utc))==$"15 {months[month-1]} 2026");
Check("windows_to_browser_IANA",new SiteDateFormatter(new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"))).BrowserTimeZoneId=="Europe/Istanbul");
var utc=new DateTime(2026,9,30,22,30,15,DateTimeKind.Utc);
Check("form_roundtrip_seconds_unchanged",istanbul.TryConvertToUtc(istanbul.ToSiteTime(utc),out var back,out _)&&back==utc);
Check("offset_input_same_instant",dates.DateAndTime(new DateTimeOffset(2026,10,1,1,30,15,TimeSpan.FromHours(3)))==dates.DateAndTime(utc));
var ny=new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
Check("invalid_DST_form_still_rejected",!ny.TryConvertToUtc(new DateTime(2026,3,8,2,30,0),out _,out _));
Check("ambiguous_DST_form_still_rejected",!ny.TryConvertToUtc(new DateTime(2026,11,1,1,30,0),out _,out _));
var options=new JsonSerializerOptions{WriteIndented=true};
File.WriteAllText(Path.Combine(args[0],"server-dates.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,cases},options));
Console.WriteLine($"Server date checks: {checks.Count} PASS");
CS
dotnet run --project "$task_tmp/probe.csproj" -p:NuGetAudit=false -- "$task_report"
node --input-type=module - "$task_repo" "$task_report" <<'JS'
import {readFileSync,writeFileSync} from 'node:fs';
import assert from 'node:assert/strict';
const root=process.argv[2],report=process.argv[3],source=readFileSync(root+'/wwwroot/js/site-date.js','utf8');
const {formatRecoveryDate:format,formatSiteDate:date}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
const {cases}=JSON.parse(readFileSync(report+'/server-dates.json','utf8'));const checks={};
function check(name,value){checks[name]=!!value;assert.ok(value,name);}
for(const item of cases){check(item.Iso+item.Zone+'_date',date(item.Iso,item.Zone)===item.Date);check(item.Iso+item.Zone+'_time',format(item.Iso,item.Zone)===`${item.Date} ${item.Time} (${item.Zone})`);}
for(const [index,value] of [undefined,null,'','invalid','2026-10-01T01:00:00'].entries())check('invalid_value_'+index,format(value,'Europe/Istanbul')==='Zaman gösterilemedi.');
for(const [index,zone] of [undefined,null,'','Wrong/Zone'].entries())check('invalid_zone_'+index,format(cases[0].Iso,zone)==='Zaman gösterilemedi.');
check('explicit_offset_same_instant',format('2026-10-01T01:30:15+03:00','Europe/Istanbul')===format(cases[0].Iso,'Europe/Istanbul'));
writeFileSync(report+'/browser-module-dates.json',JSON.stringify({passed:true,count:Object.keys(checks).length,checks},null,2));
console.log('Browser module checks: '+Object.keys(checks).length+' PASS');
JS
