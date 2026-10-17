"""Upgrade only explicitly owned fixture databases before current runtime checks.
Historical migration Up/Down assertions still run at their original targets.
"""
import re,subprocess
LATEST='20261016185304_RetentionReduction'
def ensure_runtime_schema(root,env,connection):
    # Deliberately exclude real/default PostgreSQL ports and any non-loopback server.
    if not re.match(r'^Host=127\.0\.0\.1;Port=(?:554\d\d|555\d\d);Database=[A-Za-z0-9_]+;',connection):
        raise RuntimeError('Only an owned verification database may be upgraded.')
    result=subprocess.run(['dotnet','ef','database','update',LATEST,'--no-build','--project','DevCoreBlog.csproj','--context','ApplicationDbContext'],cwd=root,env={**env,'DB_CONNECTION_STRING':connection},capture_output=True,text=True,timeout=150)
    if result.returncode: raise RuntimeError('Current runtime fixture schema upgrade failed.')
