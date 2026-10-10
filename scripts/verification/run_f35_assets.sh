#!/bin/sh
set -eu
# Verify a disposable checkout without installed dependencies or generated assets.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f35.XXXXXX)
trap 'rm -rf -- "$task_tmp"' EXIT INT TERM
rsync -a --exclude .git --exclude '.env*' --exclude bin --exclude obj \
    --exclude .local --exclude .codex --exclude .auth --exclude docs \
    --exclude node_modules --exclude wwwroot/generated --exclude .DS_Store \
    "$task_repo/" "$task_tmp/source/"
cd "$task_tmp/source"
npm ci --ignore-scripts --no-fund --no-audit
npm run build
python3 - <<'PY' > "$task_tmp/first.json"
import hashlib,json,pathlib
root=pathlib.Path('wwwroot/generated')
print(json.dumps({str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*')) if p.is_file()},sort_keys=True))
PY
npm run build
python3 - <<'PY' > "$task_tmp/second.json"
import hashlib,json,pathlib
root=pathlib.Path('wwwroot/generated')
print(json.dumps({str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*')) if p.is_file()},sort_keys=True))
PY
cmp "$task_tmp/first.json" "$task_tmp/second.json"
printf 'f35_clean_install_and_identical_rebuild=true\n'
# Remove only generated assets in this owned temporary checkout to test MSBuild.
rm -rf wwwroot/generated node_modules
dotnet publish DevCoreBlog.csproj --nologo -o "$task_tmp/publish"
python3 - "$task_tmp/publish" <<'PY'
import pathlib,sys
root=pathlib.Path(sys.argv[1])
for file in ['tailwind-public.css','tailwind-admin.css','prism/components/prism-core.min.js','prism/components/prism-csharp.min.js','prism/plugins/toolbar/prism-toolbar.min.js','prism/plugins/show-language/prism-show-language.min.js','prism/LICENSE','TAILWIND-LICENSE','toastui/toastui-editor-all.min.js','toastui/toastui-editor.min.css','toastui/LICENSE','toastui/DOMPURIFY-LICENSE','toastui/PROSEMIRROR-LICENSE']:
 assert (root/'wwwroot/generated'/file).is_file(),file
assert not list(root.rglob('node_modules')), 'node_modules must not be published'
print('f35_clean_publish_builds_and_includes_assets=true')
PY
python3 scripts/verification/tailwind_incremental_probe.py --source "$task_tmp/source"
