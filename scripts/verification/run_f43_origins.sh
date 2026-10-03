#!/bin/sh
set -eu
# Exercise actual startup origin validation without real settings or new packages.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f43-origin.XXXXXX)
trap 'rm -rf -- "$task_tmp"' EXIT INT TERM
cat > "$task_tmp/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Compile Include="$task_repo/Configuration/SiteUrlOptions.cs" Link="SiteUrlOptions.cs"/><Compile Include="$task_repo/Configuration/HttpOrigin.cs" Link="HttpOrigin.cs"/></ItemGroup></Project>
EOF
cat > "$task_tmp/Program.cs" <<'CS'
using DevCoreBlog.Configuration;
var checks = 0;
void Check(string name,bool value) { Console.WriteLine($"{name}={value}"); if(!value)throw new Exception(name); checks++; }
Check("production_HTTPS_normalizes_root",new SiteUrlOptions("https://blog.example.test/",false).Origin=="https://blog.example.test");
Check("development_missing_uses_known_localhost",new SiteUrlOptions(null,true).Origin=="http://localhost:5000");
Check("development_localhost_HTTP",new SiteUrlOptions("http://localhost:15178",true).Origin=="http://localhost:15178");
Check("development_IPv4_HTTP",new SiteUrlOptions("http://127.0.0.1:15178",true).Origin=="http://127.0.0.1:15178");
Check("development_IPv6_HTTP",new SiteUrlOptions("http://[::1]:15178",true).Origin=="http://[::1]:15178");
foreach(var (name,value,dev) in new (string,string?,bool)[] {("production_missing",null,false),("production_HTTP","http://localhost:5000",false),("blank","",true),("remote_HTTP","http://blog.example.test",true),("credentials","https://fixture:fixture@blog.example.test",true),("path","https://blog.example.test/post/x",true),("query","https://blog.example.test/?x=1",true),("fragment","https://blog.example.test/#x",true),("wildcard","https://*.example.test",true),("control","https://blog.example.test\n",true),("scheme","javascript:alert(1)",true)}) {
    var rejected=false;try{_ =new SiteUrlOptions(value,dev);}catch(InvalidOperationException ex){rejected=ex.Message.StartsWith("SITE_URL must");}Check(name+"_rejected",rejected);
}
Console.WriteLine($"count={checks}");
CS
dotnet run --project "$task_tmp/probe.csproj" -p:NuGetAudit=false
