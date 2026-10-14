#!/usr/bin/env python3
"""Explicit provider simulator acceptance; never exports credentials, bodies or personal mailboxes."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--env-file', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
if args.env_file.is_symlink() or args.env_file.stat().st_mode & 0o077:
    raise SystemExit('Credential file must be private and not a symbolic link.')
lines = args.env_file.read_text().splitlines()
values = [line.split('=', 1)[1].strip().strip('\"\'') for line in lines if line.startswith('RESEND_API_KEY=')]
if len(values) != 1 or not re.fullmatch(r're_[A-Za-z0-9_-]+', values[0]):
    raise SystemExit('Exactly one valid local Resend credential is required.')
env = {k: v for k, v in os.environ.items() if k in ('PATH', 'HOME', 'TMPDIR', 'DOTNET_ROOT', 'LANG', 'LC_ALL')}
env['RESEND_API_KEY'] = values[0]
command = ['dotnet', str(root / 'tools/DevCoreBlog.EmailProviderTool/bin/Debug/net10.0/DevCoreBlog.EmailProviderTool.dll'), '--live']
try:
    result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True, timeout=150)
    report = json.loads(result.stdout)
    if not isinstance(report.get('checks'), dict) or not report['checks'] or report['count'] != len(report['checks']):
        raise ValueError('Incomplete report')
    if values[0] in result.stdout or values[0] in result.stderr:
        raise ValueError('Sensitive output rejected')
    report['exit_code'] = result.returncode
    report['passed'] = result.returncode == 0 and report['passed'] is True and all(v is True for v in report['checks'].values())
except (ValueError, subprocess.TimeoutExpired):
    report = {'passed': False, 'reason': 'Provider test did not produce a complete safe report; no raw output exported.'}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report))
raise SystemExit(0 if report['passed'] else 1)
