"""Run against a disposable DynamoDB Local endpoint, never a real AWS table.
Set DYNAMODB_TEST_ENDPOINT to a loopback endpoint; .NET10 and Playwright required.
The unique table is inside an ephemeral in-memory DynamoDB Local instance supplied by the caller.
"""
import os
import re
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import unittest
from urllib.parse import urlsplit
import uuid
from integration import Client, DOTNET, DLL, ROOT


class DynamoAdminTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        endpoint = os.environ.get('DYNAMODB_TEST_ENDPOINT', '')
        parsed = urlsplit(endpoint)
        if parsed.scheme != 'http' or parsed.hostname not in ('127.0.0.1', 'localhost'):
            raise RuntimeError('Use a disposable loopback DynamoDB Local endpoint; real AWS is forbidden in this test.')
        cls.state = tempfile.TemporaryDirectory(prefix='suportehub-dynamo-admin-')
        cls.log = open(Path(cls.state.name)/'api.log', 'w+')
        cls.password = secrets.token_urlsafe(24)
        cls.table = 'suportehub-admin-test-' + uuid.uuid4().hex
        cls.env = {**os.environ, 'Storage__Provider': 'DynamoDb',
                   'DynamoDb__ServiceUrl': endpoint, 'DynamoDb__Region': 'sa-east-1', 'DynamoDb__TableName': cls.table,
                   'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__KeysPath': cls.state.name+'/keys',
                   'Storage__UploadsPath': cls.state.name+'/uploads', 'AWS_EC2_METADATA_DISABLED': 'true'}
        with socket.socket() as s:
            s.bind(('127.0.0.1', 0)); port=s.getsockname()[1]
        cls.base = f'http://127.0.0.1:{port}'; cls.env['ASPNETCORE_URLS'] = cls.base
        result = cls.command('--create-admin', f'admin@example.com\nAdministrador DynamoDB\n{cls.password}\n')
        if result.returncode: raise RuntimeError('DynamoDB admin provisioning failed: '+result.stdout)
        cls.creation_output = result.stdout
        cls.start()

    @classmethod
    def command(cls, action, data):
        return subprocess.run([DOTNET, str(DLL), action], input=data, text=True, capture_output=True,
                              cwd=ROOT, env=cls.env, timeout=30)

    @classmethod
    def start(cls):
        cls.process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=cls.env, stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(150):
            if cls.process.poll() is not None: raise RuntimeError('DynamoDB API exited unexpectedly.')
            try:
                if Client(cls.base).request('/api/health')[0] == 200: return
            except OSError: pass
            time.sleep(.1)
        raise RuntimeError('DynamoDB API failed readiness.')

    @classmethod
    def stop(cls):
        cls.process.terminate(); cls.process.wait(timeout=15)

    @classmethod
    def tearDownClass(cls):
        cls.stop(); cls.log.close(); cls.state.cleanup()

    def login(self, email):
        client = Client(self.base)
        self.assertEqual(client.request('/api/auth/login', {'email':email,'senha':self.password})[0], 200)
        return client

    def test_01_created_admin_readback_session_and_permissions(self):
        self.assertIn('Perfil: admin', self.creation_output)
        self.assertIn(self.table, self.creation_output)
        admin = self.login('admin@example.com')
        self.assertEqual(admin.request('/api/auth/me')[1]['perfil'], 'admin')
        self.assertEqual(admin.request('/api/admin/usuarios')[0], 200)
        health = admin.request('/api/health')[1]
        self.assertEqual((health['database'],health['table'],health['region'],health['version']),('DynamoDB',self.table,'sa-east-1','1.3.1'))
        checked = self.command('--check-account', 'admin@example.com\n')
        self.assertEqual(checked.returncode, 0); self.assertIn('Perfil: admin',checked.stdout)
        duplicate = self.command('--create-admin', f'admin@example.com\nDuplicado\n{secrets.token_urlsafe(24)}\n')
        self.assertNotEqual(duplicate.returncode, 0)
        self.assertIn('PromoteAdmin', duplicate.stderr)
        self.assertEqual(self.login('admin@example.com').request('/api/auth/me')[1]['perfil'],'admin')

    def test_02_user_remains_user_until_explicit_promotion_and_survives_restart(self):
        owner = Client(self.base); email='owner@example.com'
        status, created = owner.request('/api/auth/cadastro',{'nome':'Usuario DynamoDB','email':email,'senha':self.password,'perfil':'admin'})
        self.assertEqual(status,201);self.assertEqual(created['perfil'],'user')
        self.assertEqual(owner.request('/api/admin/usuarios')[0],403)
        promoted = self.command('--promote-admin',email+'\n')
        self.assertEqual(promoted.returncode,0);self.assertIn('Perfil: admin',promoted.stdout)
        self.assertEqual(owner.request('/api/auth/me')[0],401)
        admin=self.login(email);self.assertEqual(admin.request('/api/auth/me')[1]['perfil'],'admin')
        self.stop();self.start()
        self.assertEqual(admin.request('/api/auth/me')[1]['perfil'],'admin')
        self.assertEqual(admin.request('/api/admin/usuarios')[0],200)

    def test_03_real_browser_login_admin_and_cache_headers(self):
        from playwright.sync_api import sync_playwright
        for path in ['/', '/index.html', '/js/app.js?v=1.3.1', '/js/auth.js?v=1.3.1', '/api/health']:
            response=Client(self.base).http.open(self.base+path)
            self.assertIn('no-store',response.headers.get('Cache-Control',''));response.close()
        with sync_playwright() as p:
            browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_BIN','/usr/bin/chromium'),args=['--no-sandbox'])
            try:
                page=browser.new_page(); errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
                page.route(re.compile(r'/js/app\.js$'), lambda route: route.fulfill(body='window.staleAssetExecuted=true',content_type='application/javascript'))
                page.goto(self.base+'/#/login');page.get_by_text('Banco AWS',exact=True).wait_for()
                page.locator('#em').fill('admin@example.com');page.locator('#pw').fill(self.password)
                page.locator('#lf [type=submit]').click();page.wait_for_url('**/#/painel')
                page.get_by_text('Área do administrador',exact=True).wait_for()
                page.goto(self.base+'/#/usuarios');page.locator('#userRows .row').first.wait_for()
                self.assertEqual(page.get_by_role('link',name='Usuários',exact=True).count(),1)
                page.reload();page.get_by_text('Área do administrador',exact=True).wait_for()
                self.assertNotEqual(page.evaluate('window.staleAssetExecuted'),True)
                self.assertEqual(errors,[])
            finally: browser.close()

    def test_04_aws_launcher_guard_rejects_local_endpoint_before_provisioning(self):
        result=subprocess.run([DOTNET,str(DLL),'--require-aws=true','--check-account'],input='admin@example.com\n',text=True,capture_output=True,cwd=ROOT,env=self.env,timeout=20)
        self.assertNotEqual(result.returncode,0)
        self.assertIn('Modo AWS obrigatório',result.stderr)
        self.assertNotIn('Conta confirmada',result.stdout)

if __name__=='__main__': unittest.main(verbosity=2)
