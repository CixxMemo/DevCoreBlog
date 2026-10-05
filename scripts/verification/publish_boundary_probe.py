#!/usr/bin/env python3
"""Verify private files cannot ship, using only the quality gate's disposable source."""
import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    args = parser.parse_args()
    source = args.source.resolve()
    owned = source.parent
    assert source.name == 'source' and owned.name.startswith('devcoreblog-f55-')
    assert owned.parent in (Path('/tmp'), Path('/private/tmp'))
    assert not (source / '.git').exists(), 'Never run against a real checkout'
    output = owned / 'publish-boundary'
    assert not output.exists(), 'Preserve previous evidence'
    canaries = [source / name for name in (
        '.env', '.env.boundary-canary',
        'docs/GELISTIRME_PLANI_2026-09-21/publish-canary.json',
        'docs/uygulama-kayitlari/publish-canary.json',
        'docs/kanitlar/publish-canary.json',
        'docs/arsiv/publish-canary.json')]
    assert not any(path.exists() for path in canaries), 'Do not overwrite source files'
    written = []
    checks = {}
    try:
        for path in canaries:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('{"synthetic_publish_boundary_canary":true}\n')
            written.append(path)
        # Neither SDK discovery nor publish may include personal files, regardless of Git.
        evaluated = subprocess.run(['dotnet', 'msbuild', 'DevCoreBlog.csproj',
            '-getItem:Content,None', '-nologo'], cwd=source, capture_output=True, text=True, check=True)
        items = json.loads(evaluated.stdout)['Items']
        names = [item['Identity'].replace('\\', '/') for group in items.values() for item in group]
        checks['private_files_excluded_from_sdk_items'] = not any(
            name.startswith(('docs/', '.env', '.agents/')) or name == 'AGENTS.md' for name in names)
        published = subprocess.run(['dotnet', 'publish', 'DevCoreBlog.csproj',
            '--configuration', 'Debug', '--no-build', '--no-restore',
            '-p:NuGetAudit=false', '-o', str(output)], cwd=source, capture_output=True, text=True, timeout=180)
        checks['publish_succeeds'] = published.returncode == 0
        checks['no_documentation_in_publish'] = not (output / 'docs').exists()
        checks['no_dotenv_in_publish'] = not any(path.name.startswith('.env') for path in output.rglob('*'))
        checks['no_agent_instructions_in_publish'] = not (output / 'AGENTS.md').exists() and not (output / '.agents').exists()
        checks['application_and_assets_preserved'] = all((output / name).is_file() for name in (
            'DevCoreBlog.dll', 'DevCoreBlog.Core.dll', 'DevCoreBlog.Data.dll',
            'DevCoreBlog.Services.dll', 'appsettings.json',
            'wwwroot/generated/tailwind-public.css', 'wwwroot/generated/tailwind-admin.css',
            'wwwroot/generated/toastui/toastui-editor-all.min.js',
            'wwwroot/generated/toastui/DOMPURIFY-LICENSE'))
    finally:
        for path in written:
            path.unlink()
    print(json.dumps({'checks': checks, 'scope': 'Synthetic canaries in an owned disposable source; real dotenv never read.'}, indent=2))
    return 0 if checks and all(checks.values()) else 1


if __name__ == '__main__':
    raise SystemExit(main())
