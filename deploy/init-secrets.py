#!/usr/bin/env python3
"""Generate a private-CA deployment fixture. Public deployments supply their managed TLS certificates instead."""
import argparse, json, os, pathlib, secrets, subprocess, urllib.parse
p = argparse.ArgumentParser()
p.add_argument('directory', type=pathlib.Path)
p.add_argument('--origin', default='https://localhost:7443')
a = p.parse_args()
u = urllib.parse.urlsplit(a.origin)
if u.scheme != 'https' or not u.hostname or u.path or u.query or u.fragment or u.username:
    p.error('origin must be an exact HTTPS origin')
d = a.directory.resolve()
d.mkdir(mode=0o700, parents=True, exist_ok=False)
os.chmod(d, 0o700)
def write(name, value):
    (d/name).write_text(value)
    os.chmod(d/name, 0o644)  # The enclosing 0700 directory protects host access; mounted secrets are readable by the non-root app.
def openssl(*args):
    subprocess.run(['openssl', *map(str,args)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
for name in ('admin_password', 'owner_password', 'app_password', 'tls_password', 'dp_password'):
    write(name, secrets.token_hex(32))
openssl('req','-x509','-newkey','rsa:3072','-nodes','-keyout',d/'ca.key','-out',d/'ca.crt','-days','3650','-subj','/CN=Messanger private deployment CA','-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign')
for name, host in [('db','postgres'), ('app',u.hostname)]:
    openssl('req','-new','-newkey','rsa:2048','-nodes','-keyout',d/(name+'.key'),'-out',d/(name+'.csr'),'-subj','/CN='+host)
    write(name+'.ext', 'subjectAltName=DNS:'+host+('\n' if name=='db' else ',IP:127.0.0.1\n')+'extendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid,issuer\n')
    openssl('x509','-req','-in',d/(name+'.csr'),'-CA',d/'ca.crt','-CAkey',d/'ca.key','-CAcreateserial','-out',d/(name+'.crt'),'-days','365','-extfile',d/(name+'.ext'))
openssl('pkcs12','-export','-inkey',d/'app.key','-in',d/'app.crt','-certfile',d/'ca.crt','-out',d/'app.pfx','-passout','file:'+str(d/'tls_password'))
openssl('req','-x509','-newkey','rsa:3072','-nodes','-keyout',d/'dp.key','-out',d/'dp.crt','-days','3650','-subj','/CN=Messanger Data Protection')
openssl('pkcs12','-export','-inkey',d/'dp.key','-in',d/'dp.crt','-out',d/'dp.pfx','-passout','file:'+str(d/'dp_password'))
openssl('genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:3072','-out',d/'jwt.pem')
for f in d.iterdir(): os.chmod(f, 0o644)
def connection(user, password):
    return f'Host=postgres;Database=messanger;Username={user};Password={password};SSL Mode=VerifyFull;Root Certificate=/run/secrets/ca_cert;Maximum Pool Size=20;Timeout=5;Command Timeout=30'
runtime = {
 'ConnectionStrings': {'Postgres': connection('messanger_app',(d/'app_password').read_text())},
 'Jwt': {'Issuer': a.origin, 'Audience': 'messanger-api', 'PrivateKeyPath': '/run/secrets/jwt_key'},
 'DataProtection': {'KeysPath':'/var/lib/messanger/keys','CertificatePath':'/run/secrets/dp_pfx','CertificatePassword':(d/'dp_password').read_text()},
 'Kestrel': {'Endpoints': {'Https': {'Url':'https://0.0.0.0:8443','Certificate':{'Path':'/run/secrets/tls_pfx','Password':(d/'tls_password').read_text()}}}},
 'HttpSecurity': {'AllowedOrigins':[a.origin], 'TrustedProxies':[]}, 'AllowedHosts':u.hostname,
 'HealthCheck': {'Url':f'https://{u.hostname}:8443/health/ready'}
}
write('runtime.json',json.dumps(runtime,indent=2))
write('migration.json', json.dumps({'ConnectionStrings':{'Postgres':connection('messanger_owner',(d/'owner_password').read_text())},'Database':{'RuntimeRole':'messanger_app'}},indent=2))
print('Created deployment secrets in', d)
