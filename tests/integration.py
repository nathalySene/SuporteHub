"""Integration tests against disposable SQLite files (no Docker, WSL or AWS).
Run after building: python3 tests/integration.py
Requires .NET 10 on PATH (or DOTNET_BIN); all database state is temporary.
"""
import concurrent.futures
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import tempfile
import time
import unittest
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'backend/SuporteHub.API'
DOTNET = os.environ.get('DOTNET_BIN') or shutil.which('dotnet')
DLL = PROJECT / 'bin/Debug/net10.0/SuporteHub.API.dll'

class Client:
    def __init__(self, base):
        self.base = base
        self.cookies = http.cookiejar.CookieJar()
        self.http = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies))

    def request(self, path, data=None, method=None, headers=None, raw=False):
        body = json.dumps(data).encode() if data is not None and not isinstance(data, bytes) else data
        request = urllib.request.Request(self.base + path, data=body, method=method,
            headers={'X-Requested-With': 'SuporteHub', 'Content-Type': 'application/json', **(headers or {})})
        try:
            response = self.http.open(request, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        payload = response.read()
        result = payload if raw else json.loads(payload) if payload else None
        return response.status, result

    def upload(self, ticket, filename, content):
        boundary = 'boundary' + uuid.uuid4().hex
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="arquivo"; filename="{filename}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()
            + content + f'\r\n--{boundary}--\r\n'.encode())
        return self.request(f'/api/chamados/{ticket}/anexos', body, 'POST', {'Content-Type': f'multipart/form-data; boundary={boundary}'})

class PersistenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DOTNET or not DLL.exists():
            raise RuntimeError('Build the API and set DOTNET_BIN or activate the .NET SDK first.')
        cls.state = tempfile.TemporaryDirectory(prefix='suportehub-tests-')
        cls.log = open(Path(cls.state.name) / 'api.log', 'w+')
        cls.env = {**{key: value for key, value in os.environ.items() if not key.startswith('DynamoDb__')},
            'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__Provider': 'Sqlite',
            'Storage__DatabasePath': str(Path(cls.state.name) / 'application.sqlite3'),
            'Storage__KeysPath': cls.state.name + '/keys', 'Storage__UploadsPath': cls.state.name + '/uploads'}
        cls.password = secrets.token_urlsafe(24)
        # stdin is used so the test password never appears in process arguments or logs.
        result = subprocess.run([DOTNET, str(DLL), '--create-agent'], cwd=PROJECT, env=cls.env,
            input=f'agente@example.com\nAgente Teste\n{cls.password}\n', text=True, capture_output=True, timeout=30)
        if result.returncode:
            raise RuntimeError('Agent provisioning failed: ' + result.stdout + result.stderr)
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
        cls.base = f'http://127.0.0.1:{port}'
        cls.env['ASPNETCORE_URLS'] = cls.base
        cls.start()

    @classmethod
    def start(cls):
        cls.process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=cls.env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(150):
            if cls.process.poll() is not None:
                cls.log.seek(0); raise RuntimeError(cls.log.read())
            try:
                if Client(cls.base).request('/api/health')[0] == 200:
                    return
            except OSError:
                pass
            time.sleep(.1)
        raise RuntimeError('API did not become ready.')

    @classmethod
    def stop(cls):
        cls.process.terminate()
        cls.process.wait(timeout=15)

    @classmethod
    def tearDownClass(cls):
        cls.stop(); cls.log.close(); cls.state.cleanup()
        # Temporary databases, uploads and cookie keys are removed after the child exits.

    def account(self):
        client = Client(self.base)
        email = uuid.uuid4().hex + '@example.com'
        status, result = client.request('/api/auth/cadastro', {'nome': 'Cliente Teste', 'email': email, 'senha': self.password, 'perfil': 'agent'})
        self.assertEqual(status, 201); self.assertEqual(result['perfil'], 'client')
        return client, email

    def ticket(self, client):
        status, ticket = client.request('/api/chamados', {'titulo': 'Chamado de integração', 'descricao': 'Descrição inicial completa', 'categoria': 'Sistemas', 'prioridade': 'Alta', 'usuario': 'impostor@example.com'})
        self.assertEqual(status, 201)
        return ticket

    def agent(self):
        client = Client(self.base)
        self.assertEqual(client.request('/api/auth/login', {'email': 'agente@example.com', 'senha': self.password})[0], 200)
        return client

    def test_00_local_database_uses_a_persistent_sqlite_file(self):
        status, health = Client(self.base).request('/api/health')
        self.assertEqual(status, 200)
        self.assertEqual(health['status'], 'ok')
        self.assertEqual(health['database'].lower(), 'sqlite')
        database = Path(self.env['Storage__DatabasePath'])
        self.assertTrue(database.is_file())
        with database.open('rb') as stream:
            self.assertEqual(stream.read(16), b'SQLite format 3\x00')

    def test_00_frontend_is_served_outside_project_directory(self):
        client = Client(self.base)
        status, html = client.request('/', raw=True)
        self.assertEqual(status, 200)
        self.assertIn(b'<title>SuporteHub', html)
        for path in ['/index.html', '/css/style.css', '/js/app.js', '/js/auth.js', '/js/chamados.js', '/js/dashboard.js']:
            status, body = client.request(path, raw=True)
            self.assertEqual(status, 200, path)
            self.assertTrue(body, path)
        self.assertEqual(client.request('/api/route-that-does-not-exist', raw=True)[0], 404)

    def test_00_only_one_initial_local_support_account(self):
        env = {**self.env, 'Storage__DatabasePath': str(Path(self.state.name) / ('bootstrap-' + uuid.uuid4().hex + '.sqlite3'))}
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
        base = f'http://127.0.0.1:{port}'; env['ASPNETCORE_URLS'] = base
        process = subprocess.Popen([DOTNET, str(DLL)], cwd=PROJECT, env=env, stdout=self.log, stderr=subprocess.STDOUT)
        try:
            for _ in range(100):
                try:
                    if Client(base).request('/api/health')[0] == 200: break
                except OSError: pass
                time.sleep(.1)
            client = Client(base)
            self.assertEqual(client.request('/api/auth/setup')[1], {'primeiroAcesso': True})
            def register(number):
                return Client(base).request('/api/auth/cadastro', {'nome': 'Primeiro Suporte', 'email': f'initial-{number}@example.com', 'senha': self.password})
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                results = list(pool.map(register, range(2)))
            self.assertTrue(all(status in (201, 409) for status, _ in results))
            self.assertEqual(sum(status == 201 and data['perfil'] == 'agent' for status, data in results), 1)
            self.assertEqual(client.request('/api/auth/setup')[1], {'primeiroAcesso': False})
            status, data = client.request('/api/auth/cadastro', {'nome': 'Cliente Seguinte', 'email': 'next@example.com', 'senha': self.password, 'perfil': 'agent'})
            self.assertEqual((status, data['perfil']), (201, 'client'))
            process.terminate(); process.wait(timeout=15)
            process = subprocess.Popen([DOTNET, str(DLL)], cwd=PROJECT, env=env, stdout=self.log, stderr=subprocess.STDOUT)
            for _ in range(100):
                try:
                    if Client(base).request('/api/health')[0] == 200: break
                except OSError: pass
                time.sleep(.1)
            self.assertEqual(Client(base).request('/api/auth/setup')[1], {'primeiroAcesso': False})
        finally:
            if process.poll() is None: process.terminate(); process.wait(timeout=15)

    def test_01_authentication_and_isolation(self):
        anonymous = Client(self.base)
        self.assertEqual(anonymous.request('/api/chamados')[0], 401)
        self.assertEqual(anonymous.request('/api/auth/login', {'email': 'absent@example.com', 'senha': 'wrong'})[0], 401)
        owner, email = self.account(); other, _ = self.account(); ticket = self.ticket(owner)
        self.assertEqual(ticket['usuario'], email)
        self.assertEqual(other.request('/api/chamados')[1], [])
        self.assertEqual(other.request(f'/api/chamados/{ticket["id"]}')[0], 404)
        self.assertEqual(other.request(f'/api/chamados/{ticket["id"]}/respostas', {'texto': 'Acesso indevido'})[0], 404)
        self.assertEqual(owner.request('/api/equipe')[0], 403)
        self.assertEqual(owner.request(f'/api/chamados/{ticket["id"]}/responsavel', {'email': 'agente@example.com'}, 'PATCH')[0], 403)
        self.assertEqual(owner.request('/api/auth/logout', method='POST')[0], 204)
        self.assertEqual(owner.request('/api/auth/me')[0], 401)
        self.assertEqual(owner.request('/api/auth/login', {'email': email, 'senha': 'wrong'})[0], 401)
        self.assertEqual(owner.request('/api/auth/login', {'email': email, 'senha': self.password})[0], 200)
        self.assertEqual(owner.request('/api/auth/cadastro', {'nome': 'Duplicado', 'email': email, 'senha': self.password})[0], 409)
        self.assertEqual(owner.request('/api/auth/logout', method='POST', headers={'X-Requested-With': ''})[0], 400)

    def test_02_conversation_assignment_files_and_status(self):
        owner, _ = self.account(); ticket = self.ticket(owner); tid = ticket['id']; agent = self.agent()
        self.assertEqual(owner.request(f'/api/chamados/{tid}/respostas', {'texto': 'Mais informações'})[0], 200)
        self.assertEqual(agent.request(f'/api/chamados/{tid}/responsavel', {'email': 'agente@example.com'}, 'PATCH')[0], 200)
        status, updated = agent.request(f'/api/chamados/{tid}/status', {'status': 'Em andamento'}, 'PATCH')
        self.assertEqual(status, 200); self.assertEqual(updated['responsavel'], 'agente@example.com')
        self.assertEqual(owner.request(f'/api/chamados/{tid}/status', {'status': 'Em andamento'}, 'PATCH')[0], 400)
        content = b'%PDF-1.7\nIntegration test file\n%%EOF'
        status, uploaded = owner.upload(tid, 'evidencia.pdf', content)
        self.assertEqual(status, 200)
        file_id = uploaded['anexos'][0]['id']
        self.assertEqual(owner.request(f'/api/chamados/{tid}/anexos/{file_id}', raw=True), (200, content))
        stranger, _ = self.account()
        self.assertEqual(stranger.request(f'/api/chamados/{tid}/anexos/{file_id}')[0], 404)
        self.assertEqual(owner.upload(tid, 'fake.pdf', b'not really a pdf')[0], 400)
        self.assertEqual(owner.upload(tid, 'program.exe', b'not allowed')[0], 400)
        self.assertEqual(owner.request(f'/api/chamados/{tid}/status', {'status': 'Resolvido'}, 'PATCH')[0], 200)
        self.assertEqual(owner.request(f'/api/chamados/{tid}/respostas', {'texto': 'Depois de resolver'})[0], 400)
        self.assertEqual(agent.request(f'/api/chamados/{tid}/status', {'status': 'Aberto'}, 'PATCH')[0], 200)
        self.assertEqual(agent.request(f'/api/chamados/{tid}/respostas', {'texto': 'Reaberto para investigar'})[0], 200)

    def test_03_database_and_session_survive_restart(self):
        client, _ = self.account(); ticket = self.ticket(client); tid = ticket['id']
        self.assertEqual(client.request(f'/api/chamados/{tid}/respostas', {'texto': 'Persistir esta resposta'})[0], 200)
        content = b'%PDF-1.7\nPersistent file\n%%EOF'
        status, t = client.upload(tid, 'persistente.pdf', content); self.assertEqual(status, 200)
        self.stop(); self.start()
        self.assertEqual(client.request('/api/auth/me')[0], 200)
        status, restored = client.request(f'/api/chamados/{tid}')
        self.assertEqual(status, 200); self.assertIn('Persistir esta resposta', [h['texto'] for h in restored['historico']])
        self.assertEqual(client.request(f'/api/chamados/{tid}/anexos/{t["anexos"][0]["id"]}', raw=True), (200, content))

    def test_04_validation_and_atomic_ids(self):
        client, email = self.account()
        self.assertEqual(client.request('/api/chamados', {'titulo': '', 'descricao': 'abc', 'categoria': 'Sistemas', 'prioridade': 'Alta'})[0], 400)
        self.assertEqual(client.request('/api/chamados', {'titulo': 'abc', 'descricao': 'abc', 'categoria': 'Invalid', 'prioridade': 'Alta'})[0], 400)
        cookies = list(client.cookies)
        def create(_):
            c = Client(self.base)
            for cookie in cookies: c.cookies.set_cookie(cookie)
            return self.ticket(c)['id']
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            ids = list(pool.map(create, range(8)))
        self.assertEqual(len(set(ids)), 8)
        self.assertEqual(len(client.request('/api/chamados')[1]), 8)

    def test_05_concurrent_updates_do_not_lose_successful_replies(self):
        client, _ = self.account(); ticket = self.ticket(client); tid = ticket['id']
        cookies = list(client.cookies)
        def reply(number):
            c = Client(self.base)
            for cookie in cookies: c.cookies.set_cookie(cookie)
            status, _ = c.request(f'/api/chamados/{tid}/respostas', {'texto': f'Resposta concorrente {number}'})
            return number, status
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(reply, range(8)))
        self.assertTrue(all(status in (200, 409) for _, status in results))
        successful = {f'Resposta concorrente {number}' for number, status in results if status == 200}
        self.assertTrue(successful)
        history = client.request(f'/api/chamados/{tid}')[1]['historico']
        stored = {entry['texto'] for entry in history if entry['tipo'] == 'resposta'}
        self.assertEqual(stored, successful)

if __name__ == '__main__':
    unittest.main(verbosity=2)
