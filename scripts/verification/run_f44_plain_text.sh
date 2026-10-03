#!/bin/sh
set -eu
# Verify the actual Markdig-backed description conversion without copying its implementation.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f44-text.XXXXXX)
trap 'rm -rf -- "$task_tmp"' EXIT INT TERM
cat > "$task_tmp/probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Reference Include="DevCoreBlog.Services"><HintPath>$task_repo/bin/Debug/net10.0/DevCoreBlog.Services.dll</HintPath></Reference><Reference Include="DevCoreBlog.Core"><HintPath>$task_repo/bin/Debug/net10.0/DevCoreBlog.Core.dll</HintPath></Reference><Reference Include="Markdig"><HintPath>$task_repo/bin/Debug/net10.0/Markdig.dll</HintPath></Reference></ItemGroup></Project>
EOF
cat > "$task_tmp/Program.cs" <<'CS'
using DevCoreBlog.Services.Rendering;
var renderer = new SafeMarkdownRenderer();
var cases = new (string Name, string? Input, string Expected)[] {
    ("missing",null,""), ("whitespace"," \n \t ",""),
    ("markdown_label_not_destination","**bold** [label](https://example.test)","bold label"),
    ("inline_HTML_tags_removed","A <b>safe</b> summary","A safe summary"),
    ("script_block_omitted","<script>window.injected=1</script>",""),
    ("heading_list_text","# Heading\n\n- first\n- second","Heading first second"),
    ("image_destination_omitted","Before ![hidden](https://example.test/a.png) after","Before after"),
    ("extended_emphasis","~~deleted~~ and **bold**","deleted and bold"),
    ("code_and_Unicode","`ğüş` İstanbul IĞDIR","ğüş İstanbul IĞDIR")
};
foreach (var item in cases) {
    var passed = renderer.ToPlainText(item.Input) == item.Expected;
    Console.WriteLine($"{item.Name}={passed}");
    if (!passed) throw new Exception(item.Name);
}
Console.WriteLine($"count={cases.Length}");
CS
dotnet run --project "$task_tmp/probe.csproj" -p:NuGetAudit=false
