#!/usr/bin/env python3
"""Compose existing HTTP/security probes only inside the owned fixture."""
import argparse,json,subprocess
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--source',type=Path,required=True)
p.add_argument('--database-port',type=int,required=True)
p.add_argument('--base-url',required=True)
p.add_argument('--report-dir',type=Path,required=True)
a=p.parse_args(); source=a.source.resolve()
assert source.name=='source' and source.parent.name.startswith('devcoreblog-f17.') and source.parent.parent in (Path('/tmp'),Path('/private/tmp')) and not (source/'.git').exists()
a.report_dir.mkdir(parents=True,exist_ok=True)
scripts=source/'scripts/verification'
subprocess.run(['python3',str(scripts/'document_web_preview_probe.py'),'--base-url',a.base_url,
    '--database-port',str(a.database_port),'--report',str(a.report_dir/'document-web.json')],check=True)
header=subprocess.run(['python3',str(scripts/'f52_header_probe.py'),'--base-url',a.base_url],check=True,capture_output=True,text=True)
data=json.loads(header.stdout); assert all(data['checks'].values())
(a.report_dir/'headers.json').write_text(json.dumps(data,indent=2)+'\n')
# Proxy checks keep PostgreSQL alive; diagnostics deliberately stop it last.
subprocess.run(['python3',str(scripts/'f50_proxy_probe.py'),'--source',str(source),'--database-port',str(a.database_port),'--report',str(a.report_dir/'proxy.json')],check=True)
# Remove only the deliberately poisoned F50 dotenv inside this owned fixture.
(source/'.env').write_text('')
subprocess.run(['python3',str(scripts/'f53_operations_probe.py'),'--source',str(source),'--database-port',str(a.database_port),'--report',str(a.report_dir/'operations.json')],check=True)
for name in ('document-web','headers','proxy','operations'):
 data=json.loads((a.report_dir/(name+'.json')).read_text()); assert all(data['checks'].values())
 print('f55_'+name+'_acceptance=true')
