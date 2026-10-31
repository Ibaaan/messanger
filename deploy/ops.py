#!/usr/bin/env python3
"""Coordinated encrypted backup and explicit, project-scoped recovery."""
import argparse, datetime, json, os, pathlib, shutil, subprocess, tarfile, tempfile
os.umask(0o077)
p = argparse.ArgumentParser()
p.add_argument('--project', required=True)
p.add_argument('--secrets', required=True, type=pathlib.Path)
p.add_argument('--port', default='7443')
sub = p.add_subparsers(dest='action', required=True)
b = sub.add_parser('backup'); b.add_argument('archive', type=pathlib.Path); b.add_argument('--recipient', required=True, type=pathlib.Path)
r = sub.add_parser('restore'); r.add_argument('archive', type=pathlib.Path); r.add_argument('--recipient', required=True, type=pathlib.Path); r.add_argument('--identity', required=True, type=pathlib.Path); r.add_argument('--confirm-project', required=True)
a = p.parse_args()
root = pathlib.Path(__file__).resolve().parent
secrets_dir = a.secrets.resolve()
env = dict(os.environ, COMPOSE_PROJECT_NAME=a.project, MESSANGER_SECRETS_DIR=str(secrets_dir), MESSANGER_HTTPS_PORT=a.port)
base = ['docker','compose','--project-name',a.project,'-f',str(root/'compose.yaml')]
def compose(*args, **kwargs): return subprocess.run(base+list(args), env=env, check=True, **kwargs)
def output(*args): return subprocess.check_output(base+list(args), env=env, text=True).strip()
def safe_tar(file, target):
    with tarfile.open(file) as archive:
        for member in archive.getmembers():
            path = pathlib.PurePosixPath(member.name)
            if path.is_absolute() or '..' in path.parts or member.issym() or member.islnk() or not (member.isfile() or member.isdir()):
                raise RuntimeError('Unsafe backup archive member')
        archive.extractall(target, filter='data')
with tempfile.TemporaryDirectory(prefix='messanger-recovery-') as tmp:
    stage = pathlib.Path(tmp)
    if a.action == 'backup':
        if a.archive.exists(): raise SystemExit('Refusing to overwrite an existing backup')
        running = bool(output('ps','--status','running','-q','app'))
        if running: compose('stop','app', stdout=subprocess.DEVNULL)
        try:
            with (stage/'database.dump').open('wb') as dump:
                compose('exec','-T','postgres','pg_dump','-U','postgres','-d','messanger','--no-owner','--no-acl','-Fc',stdout=dump)
            with (stage/'keys.tar').open('wb') as keys:
                compose('run','--rm','--no-deps','-T','backup','tar','-C','/keys','-cf','-','.',stdout=keys)
            for path in secrets_dir.iterdir():
                if path.is_symlink() or not path.is_file(): raise RuntimeError('Secrets directory must contain only regular files')
            shutil.copytree(secrets_dir, stage/'secrets')
            image = output('images','-q','app')
            (stage/'manifest.json').write_text(json.dumps({'version':1,'createdUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'snapshotMode':'stopped-app','image':image}))
            with tarfile.open(stage/'archive.tar','w') as archive:
                for name in ('database.dump','keys.tar','secrets','manifest.json'): archive.add(stage/name,arcname=name)
            a.archive.parent.mkdir(parents=True,exist_ok=True)
            temporary = a.archive.with_name(a.archive.name+'.partial')
            try:
                subprocess.run(['openssl','cms','-encrypt','-binary','-aes-256-gcm','-outform','DER','-in',str(stage/'archive.tar'),'-out',str(temporary),'-recip',str(a.recipient.resolve()),'-keyopt','rsa_padding_mode:oaep'], check=True)
                temporary.replace(a.archive)
            finally: temporary.unlink(missing_ok=True)
        finally:
            if running: compose('up','-d','--no-deps','app',stdout=subprocess.DEVNULL)
        print('Encrypted backup created:', a.archive)
    else:
        if a.confirm_project != a.project: raise SystemExit('Confirmation must exactly match the target project')
        # Authenticate/decrypt and validate everything before stopping or modifying the target.
        subprocess.run(['openssl','cms','-decrypt','-binary','-inform','DER','-in',str(a.archive.resolve()),'-recip',str(a.recipient.resolve()),'-inkey',str(a.identity.resolve()),'-out',str(stage/'archive.tar')],check=True)
        safe_tar(stage/'archive.tar',stage/'contents')
        data = stage/'contents'
        if json.loads((data/'manifest.json').read_text()).get('version') != 1: raise RuntimeError('Unsupported backup version')
        safe_tar(data/'keys.tar',stage/'validated-keys')
        if secrets_dir.exists(): compose('stop','app','postgres',stdout=subprocess.DEVNULL)
        secrets_dir.mkdir(parents=True,exist_ok=True,mode=0o700)
        os.chmod(secrets_dir,0o700)
        for file in (data/'secrets').iterdir():
            if not file.is_file(): raise RuntimeError('Invalid secrets backup')
            shutil.copyfile(file,secrets_dir/file.name)
            os.chmod(secrets_dir/file.name,0o644)
        compose('up','-d','--force-recreate','--wait','postgres',stdout=subprocess.DEVNULL)
        compose('exec','-T','postgres','bash','/docker-entrypoint-initdb.d/10-roles.sh',stdout=subprocess.DEVNULL)
        with (data/'database.dump').open('rb') as dump:
            compose('exec','-T','postgres','pg_restore','-U','postgres','--role=messanger_owner','-d','messanger','--clean','--if-exists','--no-owner','--no-acl','--exit-on-error',stdin=dump,stdout=subprocess.DEVNULL)
        # A historical snapshot must not revive sessions revoked after that snapshot was taken.
        compose('exec','-T','postgres','psql','-v','ON_ERROR_STOP=1','-U','postgres','-d','messanger','-c',
            'BEGIN; UPDATE users SET session_version=session_version+1; UPDATE refresh_token_families SET revoked_at=COALESCE(revoked_at,now()); COMMIT;',stdout=subprocess.DEVNULL)
        with (data/'keys.tar').open('rb') as keys:
            compose('run','--rm','--no-deps','-T','backup','bash','-ec','find /keys -mindepth 1 -maxdepth 1 -type f -delete; tar -C /keys -xf -; chown -R 1654:1654 /keys; chmod 700 /keys; find /keys -type f -exec chmod 600 {} +',stdin=keys,stdout=subprocess.DEVNULL)
        compose('run','--rm','migrate',stdout=subprocess.DEVNULL)
        compose('up','-d','--wait','app',stdout=subprocess.DEVNULL)
        print('Restored project:',a.project)
