"""Accept only a valid, successful npm audit with no vulnerability findings."""


def evaluate_npm_audit(data, code):
    """Fail closed on findings, inconsistent reports and unsuccessful audit exits."""
    result = {
        'passed': False,
        'accepted': [],
        'unaccepted': [],
        'policy': 'Strict npm audit: no advisory exceptions',
        'response_valid': False,
    }
    if type(code) is not int or code not in (0, 1) or not isinstance(data, dict):
        result['reason'] = 'Audit response/exit is invalid'
        return result
    if 'error' in data or type(data.get('auditReportVersion')) is not int or data['auditReportVersion'] != 2:
        result['reason'] = 'Audit response/error/version is invalid'
        return result
    findings = data.get('vulnerabilities')
    metadata = data.get('metadata')
    counts = metadata.get('vulnerabilities') if isinstance(metadata, dict) else None
    if not isinstance(findings, dict) or not isinstance(counts, dict):
        result['reason'] = 'Audit finding/count structure is invalid'
        return result
    severity_keys = ('info', 'low', 'moderate', 'high', 'critical')
    if any(type(counts.get(key)) is not int or counts[key] < 0 for key in (*severity_keys, 'total')):
        result['reason'] = 'Audit counts must be non-negative integers'
        return result
    if counts['total'] != len(findings) or sum(counts[key] for key in severity_keys) != counts['total']:
        result['reason'] = 'Audit finding count mismatch'
        return result
    result['response_valid'] = True
    result['unaccepted'] = sorted(findings)
    if findings:
        result['reason'] = 'Vulnerability findings are not accepted'
        return result
    if code != 0:
        result['reason'] = 'Empty audit requires a successful exit'
        return result
    result['passed'] = True
    return result
