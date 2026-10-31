#!/usr/bin/env python3
"""Disposable HTTPS/TLS/least-privilege/restart/backup/restore/failure drill. Never targets the local developer database."""
import base64, copy, hashlib, http.cookiejar, json, os, pathlib, secrets, socket, ssl, struct, subprocess, sys, tempfile, time, urllib.error, urllib.request
root = pathlib.Path(__file__).resolve().parents[1]
suffix = secrets.token_hex(4)
projects = ['messanger-release-a-'+suffix, 'messanger-release-b-'+suffix]
ports = [int(os.environ.get('MESSANGER_SMOKE_PORT','17443')), int(os.environ.get('MESSANGER_SMOKE_RESTORE_PORT','17444'))]
password = 'release test passphrase 123'
def run(*args, **kwargs): return subprocess.run(list(args),check=True,**kwargs)
class Client:
    def __init__(self, port, ca, jar=None):
        self.base='https://localhost:'+str(port)
        self.jar=jar or http.cookiejar.CookieJar()
        self.context=ssl.create_default_context(cafile=str(ca))
        self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=self.context),urllib.request.HTTPCookieProcessor(self.jar))
    def call(self,path,body=None,token=None,proof=None,method=None):
        headers={}
        if token: headers['Authorization']='Bearer '+token
        if proof: headers['X-CSRF-Token']=proof
        data=None if body is None else json.dumps(body).encode()
        if data is not None: headers['Content-Type']='application/json'
        request=urllib.request.Request(self.base+path,data=data,headers=headers,method=method or ('POST' if body is not None else 'GET'))
        try: response=self.opener.open(request,timeout=8)
        except urllib.error.HTTPError as error: response=error
        raw=response.read()
        return response.status, json.loads(raw) if raw else None
    def wait(self,status=200,timeout=90):
        deadline=time.monotonic()+timeout
        last_error='none'
        while time.monotonic()<deadline:
            try:
                code,_=self.call('/health/ready')
                if code==status:return
            except (OSError,urllib.error.URLError) as error: last_error=str(error)
            time.sleep(.25)
        raise RuntimeError('Readiness did not reach expected status '+str(status)+': '+last_error)
class WebSocket:
    def __init__(self, client, token, origin):
        raw=socket.create_connection(('127.0.0.1',int(client.base.rsplit(':',1)[1])),timeout=10)
        self.socket=client.context.wrap_socket(raw,server_hostname='localhost')
        key=base64.b64encode(os.urandom(16)).decode()
        request=('GET /hubs/chat?access_token='+token+' HTTP/1.1\r\nHost: '+client.base.removeprefix('https://')+'\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: '+key+'\r\nOrigin: '+origin+'\r\n\r\n')
        self.socket.sendall(request.encode())
        head=b''
        while not head.endswith(b'\r\n\r\n'): head+=self.socket.recv(1)
        if not head.startswith(b'HTTP/1.1 101'):raise RuntimeError('WebSocket upgrade failed')
        expected=base64.b64encode(hashlib.sha1((key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest())
        if expected.lower() not in head.lower():raise RuntimeError('Invalid WebSocket handshake')
        self.send({'protocol':'json','version':1})
        self.receive()
    def send(self,value):
        payload=json.dumps(value,separators=(',',':')).encode()+b'\x1e'
        mask=os.urandom(4)
        length=len(payload)
        frame=bytes([0x81,0x80|length]) if length<126 else bytes([0x81,0x80|126])+struct.pack('!H',length)
        self.socket.sendall(frame+mask+bytes(value^mask[index%4] for index,value in enumerate(payload)))
    def read(self,count):
        result=b''
        while len(result)<count:
            value=self.socket.recv(count-len(result))
            if not value:raise RuntimeError('WebSocket closed')
            result+=value
        return result
    def receive(self):
        first,second=self.read(2); length=second&127
        if length==126:length=struct.unpack('!H',self.read(2))[0]
        elif length==127:length=struct.unpack('!Q',self.read(8))[0]
        if first&15==8:raise RuntimeError('WebSocket closed')
        return [json.loads(value) for value in self.read(length).decode().split('\x1e') if value]
    def invoke(self,target,args):
        self.send({'type':1,'invocationId':'1','target':target,'arguments':args})
        while True:
            for value in self.receive():
                if value.get('type')==3 and value.get('invocationId')=='1':
                    if 'error' in value:raise RuntimeError('Hub invocation failed')
                    return value.get('result')
    def close(self): self.socket.close()
def assert_status(result, status):
    if result[0]!=status:raise RuntimeError('Unexpected HTTP status: '+str(result[0])+' expected '+str(status))
    return result[1]
with tempfile.TemporaryDirectory(prefix='messanger-release-check-') as tmp:
    work=pathlib.Path(tmp)
    dirs=[work/'primary-secrets',work/'restore-secrets']
    envs=[dict(os.environ,COMPOSE_PROJECT_NAME=project,MESSANGER_SECRETS_DIR=str(directory),MESSANGER_HTTPS_PORT=str(port)) for project,directory,port in zip(projects,dirs,ports)]
    def compose(index,*args,**kwargs):return subprocess.run(['docker','compose','--project-name',projects[index],'-f',str(root/'deploy/compose.yaml'),*args],env=envs[index],check=True,**kwargs)
    def sql(index,query):
        return subprocess.check_output(['docker','compose','--project-name',projects[index],'-f',str(root/'deploy/compose.yaml'),'exec','-T','postgres','psql','-U','postgres','-d','messanger','-At','-v','ON_ERROR_STOP=1','-c',query],env=envs[index],text=True).strip()
    def ops(index,*args,check=True):
        return subprocess.run([sys.executable,str(root/'deploy/ops.py'),'--project',projects[index],'--secrets',str(dirs[index]),'--port',str(ports[index]),*map(str,args)],check=check)
    try:
        run(sys.executable,str(root/'deploy/init-secrets.py'),str(dirs[0]),'--origin','https://localhost:'+str(ports[0]))
        compose(0,'up','-d','--wait','--wait-timeout','180','app')
        client=Client(ports[0],dirs[0]/'ca.crt'); client.wait()
        print('PASS HTTPS readiness and controlled migration',flush=True)
        # Validate real runtime credentials, isolation, and database transport.
        assert sql(0,"SELECT rolsuper FROM pg_roles WHERE rolname='messanger_app'")=='f'
        assert sql(0,"SELECT has_schema_privilege('messanger_app','public','CREATE')")=='f'
        assert sql(0,"SELECT has_table_privilege('messanger_app','\"__EFMigrationsHistory\"','INSERT')")=='f'
        assert int(sql(0,"SELECT count(*) FROM pg_stat_ssl s JOIN pg_stat_activity a USING(pid) WHERE a.usename='messanger_app' AND s.ssl"))>0
        pg= subprocess.check_output(['docker','compose','--project-name',projects[0],'-f',str(root/'deploy/compose.yaml'),'ps','-q','postgres'],env=envs[0],text=True).strip()
        app= subprocess.check_output(['docker','compose','--project-name',projects[0],'-f',str(root/'deploy/compose.yaml'),'ps','-q','app'],env=envs[0],text=True).strip()
        inspect=json.loads(subprocess.check_output(['docker','inspect',app],text=True))[0]
        assert inspect['Config']['User']=='1654:1654' and inspect['HostConfig']['ReadonlyRootfs']
        assert not json.loads(subprocess.check_output(['docker','inspect',pg],text=True))[0]['HostConfig'].get('PortBindings')
        print('PASS non-root/read-only app, private database, least privilege, verified database TLS',flush=True)
        proof=assert_status(client.call('/auth/csrf'),200)['token']
        owner=assert_status(client.call('/registration',{'username':'release_owner','password':password}),201)
        member=assert_status(client.call('/registration',{'username':'release_member','password':password}),201)
        login=assert_status(client.call('/auth/login',{'username':'release_owner','password':password},proof=proof),200)
        token=login['accessToken']
        chat=assert_status(client.call('/api/chats/create',{'chatName':'Recovery check','userIds':[member['id']]},token=token),201)
        socket_client=WebSocket(client,token,client.base)
        saved=socket_client.invoke('SendMessage',[chat['id'],'Persisted through encrypted recovery'])
        socket_client.close()
        assert_status(client.call('/api/chats/'+str(chat['id'])+'/messages',token=token),200)
        compose(0,'restart','app',stdout=subprocess.DEVNULL); client.wait()
        renewed=assert_status(client.call('/auth/refresh',{},proof=proof),200)
        token=renewed['accessToken']
        print('PASS live WSS messaging and refresh/CSRF continuity after process restart',flush=True)
        latest=sql(0,'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1')
        sql(0,'DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId"=\''+latest+"'")
        client.wait(503); assert_status(client.call('/health/live'),200)
        sql(0,'INSERT INTO "__EFMigrationsHistory" VALUES (\''+latest+"','10.0.14')")
        client.wait()
        compose(0,'stop','postgres',stdout=subprocess.DEVNULL); client.wait(503)
        assert_status(client.call('/health/live'),200)
        compose(0,'up','-d','--wait','postgres',stdout=subprocess.DEVNULL); client.wait()
        assert_status(client.call('/api/chats/'+str(chat['id'])+'/messages',token=token),200)
        print('PASS migration/database failure readiness, liveness, and recovery',flush=True)
        key=work/'backup.key'; cert=work/'backup.crt'
        run('openssl','req','-x509','-newkey','rsa:3072','-nodes','-keyout',str(key),'-out',str(cert),'-days','1','-subj','/CN=Recovery test',stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        archive=work/'snapshot.cms'
        ops(0,'backup',archive,'--recipient',cert); client.wait()
        corrupted=work/'corrupted.cms'; data=bytearray(archive.read_bytes()); data[len(data)//2]^=1; corrupted.write_bytes(data)
        failed=ops(0,'restore',corrupted,'--recipient',cert,'--identity',key,'--confirm-project',projects[0],check=False)
        assert failed.returncode!=0; assert_status(client.call('/health/live'),200)
        print('PASS corrupted backup rejected before changing running deployment',flush=True)
        ops(1,'restore',archive,'--recipient',cert,'--identity',key,'--confirm-project',projects[1])
        jar=http.cookiejar.CookieJar()
        for cookie in client.jar:jar.set_cookie(copy.copy(cookie))
        restored=Client(ports[1],dirs[1]/'ca.crt',jar); restored.wait()
        assert_status(restored.call('/api/users/me',token=token),403)
        # The old CSRF proof must still decrypt using the restored key ring; sessions intentionally require fresh login.
        fresh=assert_status(restored.call('/auth/login',{'username':'release_owner','password':password},proof=proof),200)
        history=assert_status(restored.call('/api/chats/'+str(chat['id'])+'/messages',token=fresh['accessToken']),200)
        assert any(message['id']==saved['id'] and message['text']=='Persisted through encrypted recovery' for message in history)
        print('PASS coordinated database/secrets/key-ring restore, saved messages, fresh login, and old-session revocation',flush=True)
    except Exception:
        if dirs[0].exists():
            subprocess.run(['docker','compose','--project-name',projects[0],'-f',str(root/'deploy/compose.yaml'),'logs','--tail','15','app','postgres'],env=envs[0])
        raise
    finally:
        for index in (1,0):
            if dirs[index].exists():
                try:compose(index,'down','-v','--remove-orphans',stdout=subprocess.DEVNULL)
                except subprocess.CalledProcessError:print('Cleanup needed for disposable project '+projects[index],file=sys.stderr)
