"""Check instruction and plan documents without building or mutating the app."""
from pathlib import Path
import argparse
import hashlib
import json
import re

parser = argparse.ArgumentParser()
parser.add_argument('--source-baseline', type=Path)
parser.add_argument('--archive-manifest', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
plan = root / 'docs/GELISTIRME_PLANI_2026-09-21'
archive = root / 'docs/arsiv/agent-talimatlari-2026-09-21'
errors = []

def check(condition, description):
    if not condition:
        errors.append(description)

docs = [root / 'AGENTS.md', root / '.agents/AGENTS.md', root / 'README.md',
        archive / 'README.md']
docs += sorted(plan.glob('*.md'))
docs += sorted((root / 'docs/uygulama-kayitlari').glob('F*.md'))
phase_ids = []
for path in docs:
    if not path.exists():
        errors.append(f'Missing document: {path.relative_to(root)}')
        continue
    text = path.read_text()
    check(text.count('```') % 2 == 0, f'Unbalanced code fence: {path.name}')
    for target in re.findall(r'\]\(([^)]+)\)', text):
        if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*://', target) or target.startswith('#'):
            continue
        local = re.sub(r':\d+$', '', target.split('#')[0]).strip('<>')
        check((path.parent / local).exists(), f'Broken link: {path.name}: {target}')
    if path.parent == plan and path.name[:2].isdigit():
        cards = re.split(r'(?=^## F\d{2} —)', text, flags=re.M)[1:]
        for card in cards:
            phase = re.search(r'^## (F\d{2}) —', card).group(1)
            phase_ids.append(phase)
            for label in ['Neden', 'Ön koşul', 'Dokunulacak alanlar',
                          "Agent'ın uygulayacağı sıra", 'Kabul ve kanıt',
                          'Bu fazın dışında', 'Geri dönüş', 'İzlenebilirlik', 'Durma noktası']:
                check(f'**{label}:**' in card, f'{phase}: missing {label}')
            if phase != 'F00':
                previous = f'F{int(phase[1:]) - 1:02}'
                prerequisite = re.search(r'\*\*Ön koşul:\*\* (.+)', card).group(1)
                check(previous in prerequisite, f'{phase}: predecessor missing')

check(len(phase_ids) == 58 and len(set(phase_ids)) == 58, 'Expected 58 unique phase cards')
check(set(phase_ids) == {f'F{i:02}' for i in range(58)}, 'Phase IDs do not cover F00–F57')
status = (plan / 'DURUM.md').read_text()
completed = re.findall(r'^\| \[x\] \| (F\d{2})', status, re.M)
check(bool(completed) and completed[0] == 'F00', 'F00 should remain complete')
check(completed == [f'F{i:02}' for i in range(len(completed))], 'Completed phases must be contiguous')
check(len(re.findall(r'^\| \[ \] \| F\d{2}', status, re.M)) == 58 - len(completed),
      'Unstarted phase count does not match completed phases')
active_text = '\n'.join(p.read_text() for p in plan.glob('*.md'))
check(not re.search(r'D01=[AB]|F00-A|\bK0[1-4]\b', active_text), 'Obsolete branch in active plan')
check(not (plan / '10_KOSULLU_TEK_PROJE.md').exists(), 'Old branch remains active')
check(sorted(p.name for p in (root / '.agents').glob('*.md')) == ['AGENTS.md'], 'Legacy .agents plans remain')
check('../AGENTS.md' in (root / '.agents/AGENTS.md').read_text(), 'Bridge must point to root')
check(not (root / 'AGENTS.override.md').exists(), 'Root override would shadow instructions')
check((root / 'AGENTS.md').stat().st_size + (root / '.agents/AGENTS.md').stat().st_size < 32768,
      'Repository instruction chain exceeds default discovery budget')

source_count = None
if args.source_baseline:
    baseline = json.loads(args.source_baseline.read_text())
    source_count = len(baseline)
    for name, digest in baseline.items():
        path = root / name
        check(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == digest,
              f'Application baseline changed: {name}')

archive_count = None
if args.archive_manifest:
    manifest = json.loads(args.archive_manifest.read_text())
    archive_count = len(manifest)
    for item in manifest:
        path = Path(item['archive'])
        check(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == item['archive_sha256'],
              f'Archive changed: {item["name"]}')

print(json.dumps({'checked_documents': len(docs), 'phase_cards': len(phase_ids),
                  'completed': completed, 'unchanged_source_files': source_count,
                  'verified_legacy_archives': archive_count, 'errors': errors}, ensure_ascii=False, indent=2))
raise SystemExit(bool(errors))
