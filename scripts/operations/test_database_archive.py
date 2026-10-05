#!/usr/bin/env python3
"""Back up or restore only an owned F17 disposable cluster; never target a live database."""
import argparse,getpass,hashlib,os,re,subprocess
from pathlib import Path


def execute(args):
    """Verify cluster ownership and destination before running PostgreSQL archive tools."""
    root=args.fixture_root.resolve()
    if root.parent not in (Path('/tmp'),Path('/private/tmp')) or not root.name.startswith('devcoreblog-f17.') or not (root/'postgres/PG_VERSION').is_file():
        raise ValueError('Expected owned disposable fixture cluster')
    if not 1024<args.port<65535:raise ValueError('Invalid fixture port')
    if args.database!='devcoreblog_f01_test' and not re.fullmatch(r'f56_[a-z0-9_]+',args.database):raise ValueError('Refusing non-test database name')
    archive=args.archive.resolve()
    if archive.parent!=root/'backups' or args.archive.is_symlink():raise ValueError('Archive must stay inside owned backups directory')
    env={k:v for k,v in os.environ.items() if k in ('PATH','HOME','LANG','LC_ALL','DOTNET_ROOT')}
    env['PGPASSFILE']='/dev/null';env['PGCONNECT_TIMEOUT']='5'
    connection=['-h','127.0.0.1','-p',str(args.port),'-U',getpass.getuser()]
    def command(argv):return subprocess.run(argv,env=env,check=True,capture_output=True,text=True,timeout=60).stdout.strip()
    actual=command(['psql','-X',*connection,'-d','postgres','-At','-c','SHOW data_directory'])
    if Path(actual).resolve()!=root/'postgres':raise ValueError('PostgreSQL cluster does not match owned fixture')
    if args.action=='backup':
        archive.parent.mkdir(mode=0o700,exist_ok=True)
        if archive.exists():raise ValueError('Refusing archive overwrite')
        # --no-owner is applied again at restore; ACLs/global roles are outside this test archive.
        command(['pg_dump',*connection,'-d',args.database,'--format=custom','--no-owner','--no-acl','--file',str(archive)])
        archive.chmod(0o600)
        return hashlib.sha256(archive.read_bytes()).hexdigest()
    if args.database=='devcoreblog_f01_test':raise ValueError('Refusing restore into source database')
    if not args.sha256 or hashlib.sha256(archive.read_bytes()).hexdigest()!=args.sha256:raise ValueError('Archive checksum mismatch')
    command(['pg_restore','--list',str(archive)])
    exists=command(['psql','-X',*connection,'-d','postgres','-At','-c',f"SELECT count(*) FROM pg_database WHERE datname='{args.database}'"])
    if exists!='0':raise ValueError('Restore requires a new destination database')
    command(['createdb',*connection,'--template=template0',args.database])
    command(['pg_restore',*connection,'-d',args.database,'--no-owner','--no-acl','--single-transaction','--exit-on-error',str(archive)])
    return 'restored'


def main():
    p=argparse.ArgumentParser();p.add_argument('action',choices=('backup','restore'));p.add_argument('--fixture-root',type=Path,required=True);p.add_argument('--port',type=int,required=True);p.add_argument('--database',required=True);p.add_argument('--archive',type=Path,required=True);p.add_argument('--sha256')
    try:print(execute(p.parse_args()))
    except (ValueError,OSError,subprocess.SubprocessError) as error:
        # Never echo raw provider errors, connection data, SQL or archive contents.
        print('Archive operation refused/failed: '+type(error).__name__);raise SystemExit(1)
if __name__=='__main__':main()
