#!/bin/sh
set -eu
# Compile the actual presentation sources in a disposable, package-free culture harness.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f42-dates.XXXXXX)
trap 'rm -rf -- "$task_tmp"' EXIT INT TERM
cat > "$task_tmp/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include="$task_repo/Models/Presentation/SiteDateFormatter.cs" Link="SiteDateFormatter.cs"/><Compile Include="$task_repo/DevCoreBlog.Services/Publishing/PublicationTimeZone.cs" Link="PublicationTimeZone.cs"/></ItemGroup></Project>
EOF
cat > "$task_tmp/Program.cs" <<'CS'
using System.Globalization;
using DevCoreBlog.Models.Presentation;
using DevCoreBlog.Services.Publishing;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
var utc = new DateTime(2026, 10, 3, 22, 30, 15, DateTimeKind.Utc);
var zone = new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
var dates = new SiteDateFormatter(zone);
void Check(string name, bool passed) { Console.WriteLine($"{name}={passed}"); if (!passed) throw new Exception(name); }
Check("baseline_implicit_culture_reproduces_Turkish_month", utc.ToString("MMM dd, yyyy") == "Eki 03, 2026");
Check("English_site_date_crosses_midnight", dates.Date(utc) == "Oct 04, 2026");
Check("all_twelve_months_are_English", Enumerable.Range(1,12).All(m => dates.Date(new DateTime(2026,m,15,12,0,0,DateTimeKind.Utc)) == new DateTime(2026,m,15).ToString("MMM dd, yyyy", CultureInfo.GetCultureInfo("en-US"))));
Check("omitted_UTC_kind_remains_supported", dates.Date(DateTime.SpecifyKind(utc,DateTimeKind.Unspecified)) == "Oct 04, 2026");
Check("site_form_to_UTC_roundtrip_keeps_seconds", zone.TryConvertToUtc(zone.ToSiteTime(utc),out var back,out _) && back == utc);
Check("Windows_timezone_maps_to_browser_IANA", new SiteDateFormatter(new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"))).BrowserTimeZoneId == "Europe/Istanbul");
Check("UTC_input_remains_unchanged", zone.TryConvertToUtc(utc,out back,out _) && back == utc);
var ny = new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
Check("DST_invalid_form_still_rejected", !ny.TryConvertToUtc(new DateTime(2026,3,8,2,30,0),out _,out _));
Check("DST_ambiguous_form_still_rejected", !ny.TryConvertToUtc(new DateTime(2026,11,1,1,30,0),out _,out _));
CS
dotnet run --project "$task_tmp/probe.csproj" -p:NuGetAudit=false
node --input-type=module - "$task_repo" <<'JS'
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
const source = readFileSync(process.argv[2]+'/wwwroot/js/site-date.js','utf8');
const {formatRecoveryDate:format} = await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
assert.equal(format('2026-10-03T22:30:15Z','Europe/Istanbul'),'Oct 04, 2026 01:30 (Europe/Istanbul)');
assert.equal(format('2026-10-03T22:30:15Z','America/New_York'),'Oct 03, 2026 18:30 (America/New_York)');
for (const value of [undefined,null,'','invalid']) assert.equal(format(value,'Europe/Istanbul'),'unknown time');
console.log('recovery_site_zone_English_and_invalid_values=true (6 assertions)');
JS
