"""Exercise CSS source boundaries and changed chunks only in F35's owned source."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    args = parser.parse_args()
    source = args.source.resolve()
    owned = source.parent
    assert source.name == 'source' and owned.name.startswith('devcoreblog-f35.')
    assert owned.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
    generated = source / 'wwwroot/generated'
    first = {theme: digest(generated / f'tailwind-{theme}.css') for theme in ('public', 'admin')}
    old_chunks = {p.name for p in (generated / 'tiptap/chunks').glob('*.js')}
    shared = source / 'frontend/tiptap/images.js'
    original = shared.read_text()
    assert original.count("'Geçersiz görsel'") == 1
    files = {
        'Views/Shared/TailwindCanary.cshtml': '<div class="z-[987651]"></div>',
        'wwwroot/js/tailwind-canary.js': "const canary = 'z-[987652]';\n",
        'Views/Shared/TailwindCanary.txt': 'z-[987653]',
        'wwwroot/js/tailwind-canary.txt': 'z-[987654]',
        '.local/devcoreblog_vol1/tailwind-canary.html': 'z-[987655]',
        'docs/tailwind-canary.html': 'z-[987656]',
    }
    assert all(not (source / name).exists() for name in files)
    written = []
    checks = {}
    try:
        for name, text in files.items():
            target = source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text)
            written.append(target)
        shared.write_text(original.replace("'Geçersiz görsel'", "'Geçersiz görsel — incremental canary'"))
        subprocess.run(['dotnet', 'build', 'DevCoreBlog.csproj', '--no-restore',
                        '--nologo', '--disable-build-servers', '-m:1', '-nodeReuse:false'],
                       cwd=source, check=True, timeout=180)
        checks['changed_input_build_succeeds'] = True
        for theme in first:
            css = generated / f'tailwind-{theme}.css'
            text = css.read_text()
            checks[f'{theme}_css_changes'] = digest(css) != first[theme]
            checks[f'{theme}_only_allowed_extensions_scanned'] = all(
                str(n) in text for n in (987651, 987652)) and all(
                str(n) not in text for n in (987653, 987654, 987655, 987656))
        current_chunks = {p.name for p in (generated / 'tiptap/chunks').glob('*.js')}
        checks['changed_chunk_is_generated'] = bool(current_chunks - old_chunks)
        checks['obsolete_chunk_is_removed'] = bool(old_chunks - current_chunks)
        published = owned / 'publish-incremental'
        assert not published.exists()
        subprocess.run(['dotnet', 'publish', 'DevCoreBlog.csproj', '--no-restore',
                        '--nologo', '--disable-build-servers', '-m:1', '-nodeReuse:false',
                        '-o', str(published)], cwd=source, check=True, timeout=180)
        shipped = published / 'wwwroot/generated'
        checks['publish_css_matches_current_inputs'] = all(
            digest(shipped / f'tailwind-{theme}.css') == digest(generated / f'tailwind-{theme}.css')
            for theme in first)
        receipt = json.loads((generated / 'tiptap/build.json').read_text())
        checks['current_entries_and_chunks_are_published'] = all(
            digest(shipped / 'tiptap' / name) == metadata['sha256']
            for name, metadata in receipt['files'].items())
        checks['obsolete_chunks_absent_in_fresh_publish'] = not any(
            (shipped / 'tiptap/chunks' / name).exists() for name in old_chunks - current_chunks)
        checks['private_canaries_not_published'] = not (published / '.local').exists() and not (published / 'docs').exists()
    finally:
        shared.write_text(original)
        for target in written:
            target.unlink()
    print(json.dumps({'checks': checks,
                      'scope': 'Real build/publish on owned F35 source. New publish directory; no guarantee for reusing old deployment directories.'}, indent=2))
    return 0 if all(checks.values()) else 1


if __name__ == '__main__':
    raise SystemExit(main())
