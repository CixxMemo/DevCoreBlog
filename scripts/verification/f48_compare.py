#!/usr/bin/env python3
"""Check recorded F48 evidence; result bytes are serialization size, not wire traffic."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]/'docs/uygulama-kayitlari/kanit/F48'
before = json.loads((root/'before.json').read_text())
after = json.loads((root/'after.json').read_text())
checks = {}
for name, count in [('home',9), ('detail',1), ('search',9), ('admin',25), ('related',3)]:
    checks[name+'_bounded_rows'] = before[name]['materializedRows'] == after[name]['materializedRows'] == count
    checks[name+'_query_count_preserved'] = before[name]['queryCount'] == after[name]['queryCount']
    checks[name+'_untracked'] = after[name]['trackedEntities'] == 0
    if name in ('home','admin','related'):
        checks[name+'_measured_improvement'] = after[name]['medianMs'] < before[name]['medianMs'] * .85
related = after['related']['plans'][0]['sql']
checks['related_body_not_selected'] = '"Content"' not in related and 'LIMIT' in related
checks['related_result_size_reduced'] = after['related']['serializedResultBytes'] < before['related']['serializedResultBytes'] * .1
for name, index in [('home','IX_Posts_PublishDate_Id'), ('admin','IX_Posts_CreatedDate_Id'), ('related','IX_Posts_PublishDate_Id')]:
    checks[name+'_index_actually_used'] = index in json.dumps(after[name]['plans'])
http_before = json.loads((root/'http-before.json').read_text())
http_after = json.loads((root/'http-after.json').read_text())
for name in http_after:
    checks[name+'_mvc_query_count_preserved'] = http_after[name]['queryCount'] == http_before[name]['queryCount']
    checks[name+'_mvc_row_count_preserved'] = all(http_after[name][key] == http_before[name][key] for key in ('cardRows','adminRows'))
print(json.dumps({'checks':checks}, indent=2))
assert all(checks.values()), checks
