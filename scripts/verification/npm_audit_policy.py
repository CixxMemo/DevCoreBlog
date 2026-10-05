"""Keep accepted build risk visible while rejecting new advisories and scope changes."""
from datetime import date

ADVISORY='https://github.com/advisories/GHSA-vfj7-8cjw-p6xm'
REVIEW_BY=date(2026,11,5)
VERSIONS={'braces':'3.0.3','chokidar':'3.6.0','fast-glob':'3.3.3','micromatch':'4.0.8','tailwindcss':'3.4.17'}


def evaluate_npm_audit(data,code,lock,manifest,today=None):
    """Apply the human-approved exception only to the exact dev-only advisory graph."""
    today=today or date.today()
    result={'passed':False,'accepted':[],'unaccepted':[],'policy':'GHSA-vfj7-8cjw-p6xm build-time risk accepted by user; review by 2026-11-05'}
    if code not in (0,1) or data.get('error') or not isinstance(data.get('vulnerabilities'),dict) or not isinstance(data.get('metadata',{}).get('vulnerabilities',{}).get('total'),int):
        result['reason']='Audit response/exit is invalid';return result
    findings=data['vulnerabilities']
    if data['metadata']['vulnerabilities']['total']!=len(findings):
        result['reason']='Audit finding count mismatch';return result
    if not findings:
        result['passed']=code==0;return result
    if today>REVIEW_BY or manifest.get('dependencies') or manifest.get('optionalDependencies'):
        result['reason']='Exception expired or production dependencies exist';return result
    packages=lock.get('packages',{})
    def accepted(name,seen):
        if name in seen or name not in VERSIONS:return False
        finding=findings.get(name)
        if not isinstance(finding,dict) or finding.get('name')!=name or finding.get('severity')!='high':return False
        nodes=finding.get('nodes',[])
        if nodes!=['node_modules/'+name]:return False
        entry=packages.get(nodes[0],{})
        if entry.get('version')!=VERSIONS[name] or entry.get('dev') is not True or entry.get('devOptional') or entry.get('optional'):return False
        via=finding.get('via',[])
        if not via:return False
        for item in via:
            if isinstance(item,str):
                if not accepted(item,seen|{name}):return False
            elif isinstance(item,dict):
                if name!='braces' or item.get('url')!=ADVISORY or item.get('name')!='braces' or item.get('dependency')!='braces' or item.get('range')!='<=3.0.3' or item.get('severity')!='high':return False
            else:return False
        return True
    for name in findings:
        if accepted(name,set()):result['accepted'].append({'package':name,'version':VERSIONS[name],'advisory':ADVISORY,'scope':'dev-only build dependency'})
        else:result['unaccepted'].append(name)
    result['passed']=not result['unaccepted'] and bool(result['accepted'])
    return result
