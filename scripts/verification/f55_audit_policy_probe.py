#!/usr/bin/env python3
"""Check strict audit boundaries with explicitly synthetic negative responses."""
import argparse
import copy
import json
from pathlib import Path

from npm_audit_policy import evaluate_npm_audit


def check_policy(clean):
    """Require clean audit success and reject the retired graph and invalid responses."""
    checks = {}

    def check(name, data, code=0, expected=False):
        result = evaluate_npm_audit(data, code)
        checks[name] = result['passed'] is expected and result['accepted'] == []

    check('real_clean_audit_accepted_without_exception', clean, expected=True)
    for code in (1, 2, 124, -15, True, None):
        check('empty_audit_exit_' + str(code) + '_rejected', clean, code)
    for name, data in (
        ('non_object', []), ('invalid_json_text', '{'), ('null', None),
        ('missing_fields', {}), ('metadata_list', {**clean, 'metadata': []}),
        ('findings_list', {**clean, 'vulnerabilities': []}),
        ('network_error', {**clean, 'error': {'code': 'NETWORK'}}),
        ('empty_error', {**clean, 'error': {}}),
        ('unknown_version', {**clean, 'auditReportVersion': 3}),
        ('boolean_version', {**clean, 'auditReportVersion': True}),
    ):
        check(name + '_rejected', data)
    for field, value in (('total', 1), ('total', -1), ('total', True), ('total', '0'),
                         ('high', 1), ('high', -1), ('low', False), ('critical', None)):
        data = copy.deepcopy(clean)
        data['metadata']['vulnerabilities'][field] = value
        check(field + '_' + str(value) + '_rejected', data)
    data = copy.deepcopy(clean)
    del data['metadata']['vulnerabilities']['low']
    check('missing_severity_count_rejected', data)
    # The former dev-only recursive graph is now rejected regardless of lock scope/date.
    old = copy.deepcopy(clean)
    names = ('braces', 'chokidar', 'fast-glob', 'micromatch', 'tailwindcss')
    old['vulnerabilities'] = {
        name: {'name': name, 'severity': 'high', 'nodes': ['node_modules/' + name],
               'via': ([{'name': 'braces', 'dependency': 'braces', 'severity': 'high',
                         'range': '<=3.0.3', 'url': 'https://github.com/advisories/GHSA-vfj7-8cjw-p6xm'}]
                       if name == 'braces' else ['braces'])}
        for name in names
    }
    old['metadata']['vulnerabilities'].update(high=5, total=5)
    check('retired_braces_graph_rejected', old, 1)
    check('retired_graph_with_exit_zero_rejected', old)
    for severity in ('info', 'low', 'moderate', 'high', 'critical'):
        data = copy.deepcopy(clean)
        data['vulnerabilities'] = {'new-package': {'name': 'new-package', 'severity': severity}}
        data['metadata']['vulnerabilities'].update({severity: 1, 'total': 1})
        check('new_' + severity + '_finding_rejected', data)
    return checks


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--audit', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    clean = json.loads(args.audit.read_text())
    if not evaluate_npm_audit(clean, 0)['passed']:
        raise SystemExit('Probe requires a valid clean audit capture; it does not replace a real audit exit check.')
    checks = check_policy(clean)
    args.report.write_text(json.dumps({
        'scope': 'Captured clean response plus synthetic policy negatives; not provider/platform integration',
        'checks': checks, 'count': len(checks),
    }, indent=2) + '\n')
    print(json.dumps(checks))
    raise SystemExit(0 if all(checks.values()) else 1)


if __name__ == '__main__':
    main()
