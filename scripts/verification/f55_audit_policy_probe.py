#!/usr/bin/env python3
"""Regress acceptance boundaries against the real captured npm advisory report."""
import argparse,copy,json
from datetime import date
from pathlib import Path
from npm_audit_policy import evaluate_npm_audit
p=argparse.ArgumentParser();p.add_argument('--audit',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[2]
data=json.loads(a.audit.read_text());lock=json.loads((root/'package-lock.json').read_text());manifest=json.loads((root/'package.json').read_text());checks={}
def test(name,change,expected=False,code=1,today=date(2026,10,5)):
 d,l,m=copy.deepcopy(data),copy.deepcopy(lock),copy.deepcopy(manifest);change(d,l,m)
 checks[name]=evaluate_npm_audit(d,code,l,m,today)['passed'] is expected
noop=lambda d,l,m:None
test('exact_known_graph_accepted',noop,True)
test('network_error_rejected',lambda d,l,m:d.update(error={'code':'NETWORK'}))
test('timeout_rejected',noop,code=124)
test('exit_two_rejected',noop,code=2)
test('new_advisory_rejected',lambda d,l,m:d['vulnerabilities']['braces']['via'].append({'url':'https://github.com/advisories/GHSA-new-advisory'}))
test('new_package_rejected',lambda d,l,m:(d['vulnerabilities'].update(other={'name':'other'}),d['metadata']['vulnerabilities'].update(total=6)))
test('changed_version_rejected',lambda d,l,m:l['packages']['node_modules/braces'].update(version='3.0.4'))
test('runtime_scope_rejected',lambda d,l,m:l['packages']['node_modules/braces'].update(dev=False))
test('runtime_dependency_rejected',lambda d,l,m:m.update(dependencies={'braces':'3.0.3'}))
test('nested_unknown_node_rejected',lambda d,l,m:d['vulnerabilities']['braces'].update(nodes=['node_modules/other/node_modules/braces']))
test('expired_exception_rejected',noop,today=date(2026,11,6))
test('cycle_rejected',lambda d,l,m:d['vulnerabilities']['braces'].update(via=['tailwindcss']))
test('malformed_count_rejected',lambda d,l,m:d['metadata']['vulnerabilities'].update(total=0))
test('clean_scan_needs_no_exception',lambda d,l,m:(d.update(vulnerabilities={}),d['metadata']['vulnerabilities'].update(total=0)),True,code=0)
a.report.write_text(json.dumps({'checks':checks,'count':len(checks)},indent=2)+'\n');print(json.dumps(checks));assert all(checks.values())
