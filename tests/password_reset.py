"""Password recovery API/browser tests using temporary SQLite and a local .eml outbox.

Build the API first, then run `python3 tests/password_reset.py`.
The last test requires Python Playwright and Chromium. No real email or Google
requests are sent. Passwords, reset tokens and recovery URLs are never printed.
"""
import concurrent.futures
from contextlib import contextmanager
from datetime import datetime, timezone
from email import policy
from email.parser import BytesParser
import json
import os
from pathlib import Path
import re
import secrets
import socket
import sqlite3
import subprocess
import tempfile
import time
import unittest
from urllib.parse import parse_qs, urlsplit
import uuid

from integration import Client, DLL, DOTNET, ROOT


class ResetServer:
    def __init__(self, mode='File', environment='Development'):
        self.state = tempfile.TemporaryDirectory(prefix='suportehub-password-reset-')
        self.directory = Path(self.state.name)
        self.outbox = self.directory / 'mail'
        self.database = self.directory / 'app.sqlite3'
        self.log = open(self.directory / 'server.log', 'w+')
        self.process = None
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            port = sock.getsockname()[1]
        self.base = f'http://127.0.0.1:{port}'
        # No inherited provider/SMTP configuration or actual credentials are used.
        excluded = ('DynamoDb__', 'Email__', 'Authentication__Google__')
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith(excluded)}
        self.env.update({
            'ASPNETCORE_ENVIRONMENT': environment,
            'ASPNETCORE_URLS': self.base,
            'Storage__Provider': 'Sqlite',
            'Storage__DatabasePath': str(self.database),
            'Storage__KeysPath': str(self.directory / 'keys'),
            'Storage__UploadsPath': str(self.directory / 'uploads'),
            'Authentication__PublicBaseUrl': self.base,
            'Authentication__Google__ClientId': '',
            'Authentication__Google__ClientSecret': '',
            'Email__DeliveryMode': mode,
            'Email__OutboxPath': str(self.outbox),
        })

    def start(self):
        if not DOTNET or not DLL.exists():
            raise RuntimeError('Build the API and activate .NET 10 (or set DOTNET_BIN) first.')
        self.process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=self.env,
                                        stdout=self.log, stderr=subprocess.STDOUT)
        for _ in range(150):
            if self.process.poll() is not None:
                raise RuntimeError('The isolated recovery-test API stopped before readiness; inspect its local test log.')
            try:
                if Client(self.base).request('/api/health')[0] == 200:
                    return self
            except OSError:
                pass
            time.sleep(.1)
        raise RuntimeError('The isolated recovery-test API did not become ready.')

    def close(self):
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            self.process.wait(timeout=15)
        self.log.close()
        self.state.cleanup()

    def email_files(self):
        return set(self.outbox.glob('*.eml')) if self.outbox.exists() else set()

    def wait_email(self, recipient, previous=()):
        """Decode MIME transfer encoding before reading the fragment URL."""
        previous = set(previous)
        for _ in range(150):
            for file in self.email_files() - previous:
                try:
                    message = BytesParser(policy=policy.default).parsebytes(file.read_bytes())
                    if recipient.lower() not in str(message['To']).lower():
                        continue
                    body = message.get_body(preferencelist=('plain',))
                    text = body.get_content() if body else message.get_content()
                    for link in re.findall(r'https?://[^\s<>]+', text):
                        parts = urlsplit(link)
                        if not parts.fragment.startswith('/redefinir-senha?'):
                            continue
                        query = parse_qs(parts.fragment.partition('?')[2])
                        if query.get('email') == [recipient] and query.get('token'):
                            if parts.scheme + '://' + parts.netloc != self.base:
                                raise RuntimeError('The generated recovery message used the wrong test origin.')
                            return {'token': query['token'][0], 'link': link, 'file': file}
                except (OSError, ValueError, KeyError, AttributeError):
                    # Pickup files may be observed while still being written.
                    continue
            time.sleep(.05)
        raise RuntimeError('No complete recovery email was produced in the isolated local outbox.')


@contextmanager
def isolated_server(mode='File', environment='Development'):
    server = ResetServer(mode, environment)
    try:
        yield server.start()
    finally:
        server.close()


class PasswordResetTests(unittest.TestCase):
    def setUp(self):
        self.server = ResetServer().start()
        self.addCleanup(self.server.close)
        self.client = Client(self.server.base)
        self.email = uuid.uuid4().hex + '@example.com'
        self.password = secrets.token_urlsafe(24)
        self.new_password = secrets.token_urlsafe(24)

    def account(self):
        status, account = self.client.request('/api/auth/cadastro', {
            'nome': 'Pessoa de Teste', 'email': self.email, 'senha': self.password})
        self.assertEqual(status, 201)
        return account

    def request_reset(self):
        previous = self.server.email_files()
        status, response = Client(self.server.base).request('/api/auth/forgot-password', {'email': self.email})
        self.assertEqual(status, 202)
        return self.server.wait_email(self.email, previous)

    def reset(self, token, password=None, email=None):
        return Client(self.server.base).request('/api/auth/reset-password', {
            'email': email or self.email, 'token': token, 'senha': password or self.new_password})

    def login(self, password):
        return Client(self.server.base).request('/api/auth/login', {'email': self.email, 'senha': password})[0]

    def account_record(self):
        with sqlite3.connect(self.server.database) as db:
            row = db.execute('SELECT Data FROM Records WHERE PK=? AND SK=?', ('USERS', self.email)).fetchone()
        return json.loads(row[0])

    def test_01_uniform_response_and_hashed_token(self):
        self.account()
        status, providers = self.client.request('/api/auth/providers')
        self.assertEqual(status, 200)
        self.assertTrue(providers['passwordReset'])
        self.assertTrue(providers['localEmailPreview'])
        previous = self.server.email_files()
        existing = Client(self.server.base).request('/api/auth/forgot-password', {'email': self.email})
        mail = self.server.wait_email(self.email, previous)
        unknown = Client(self.server.base).request('/api/auth/forgot-password', {'email': 'unknown@example.com'})
        self.assertEqual(existing, unknown)
        self.assertEqual(existing[0], 202)
        record = self.account_record()
        self.assertEqual(len(record['PasswordResetTokenHash']), 64)
        self.assertFalse(mail['token'] in json.dumps(record), 'The raw reset token must not be stored in the account.')
        expiry = datetime.fromisoformat(record['PasswordResetExpiresAt'].replace('Z', '+00:00'))
        self.assertGreater(expiry, datetime.now(timezone.utc))
        before = self.server.email_files()
        time.sleep(.2)
        self.assertEqual(self.server.email_files(), before)

    def test_02_success_single_use_and_all_sessions_revoked(self):
        self.account()
        second_session = Client(self.server.base)
        self.assertEqual(second_session.request('/api/auth/login', {'email': self.email, 'senha': self.password})[0], 200)
        mail = self.request_reset()
        self.assertEqual(self.reset(mail['token'])[0], 200)
        self.assertEqual(self.client.request('/api/auth/me')[0], 401)
        self.assertEqual(second_session.request('/api/auth/me')[0], 401)
        self.assertEqual(self.login(self.password), 401)
        self.assertEqual(self.login(self.new_password), 200)
        self.assertEqual(self.reset(mail['token'])[0], 400)
        record = self.account_record()
        self.assertIsNone(record['PasswordResetTokenHash'])
        self.assertIsNone(record['PasswordResetExpiresAt'])
        self.assertTrue(record['SecurityStamp'])

    def test_03_new_link_invalidates_old_link(self):
        self.account()
        first = self.request_reset()
        second = self.request_reset()
        self.assertFalse(first['token'] == second['token'])
        self.assertEqual(self.reset(first['token'])[0], 400)
        self.assertEqual(self.login(self.password), 200)
        self.assertEqual(self.client.request('/api/auth/me')[0], 200)
        self.assertEqual(self.reset(second['token'])[0], 200)
        self.assertEqual(self.login(self.new_password), 200)

    def test_04_expired_token_does_not_change_password(self):
        self.account()
        mail = self.request_reset()
        record = self.account_record()
        record['PasswordResetExpiresAt'] = '2000-01-01T00:00:00Z'
        with sqlite3.connect(self.server.database) as db:
            db.execute('UPDATE Records SET Data=?, Version=Version+1 WHERE PK=? AND SK=?',
                       (json.dumps(record), 'USERS', self.email))
        self.assertEqual(self.reset(mail['token'])[0], 400)
        self.assertEqual(self.login(self.password), 200)
        self.assertEqual(self.client.request('/api/auth/me')[0], 200)

    def test_05_malformed_unknown_and_wrong_tokens(self):
        self.account()
        mail = self.request_reset()
        self.assertEqual(self.reset('too-short')[0], 400)
        self.assertEqual(self.reset(secrets.token_urlsafe(32))[0], 400)
        self.assertEqual(self.reset(mail['token'], email='unknown@example.com')[0], 400)
        self.assertEqual(self.login(self.password), 200)
        self.assertEqual(self.client.request('/api/auth/me')[0], 200)
        self.assertEqual(self.reset(mail['token'])[0], 200)

    def test_06_only_one_concurrent_reset_can_succeed(self):
        self.account()
        mail = self.request_reset()
        passwords = [secrets.token_urlsafe(24), secrets.token_urlsafe(24)]
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda p: self.reset(mail['token'], p)[0], passwords))
        self.assertEqual(sorted(results), [200, 400])
        accepted = results.index(200)
        self.assertEqual(self.login(passwords[accepted]), 200)
        self.assertEqual(self.login(passwords[1-accepted]), 401)

    def test_07_unavailable_delivery_and_no_file_outbox_in_production(self):
        for mode, environment in [('Disabled', 'Development'), ('File', 'Production')]:
            with self.subTest(mode=mode, environment=environment):
                with isolated_server(mode, environment) as server:
                    client = Client(server.base)
                    status, providers = client.request('/api/auth/providers')
                    self.assertEqual(status, 200)
                    self.assertFalse(providers['passwordReset'])
                    self.assertFalse(providers['localEmailPreview'])
                    self.assertEqual(client.request('/api/auth/forgot-password', {'email': 'unknown@example.com'})[0], 503)
                    self.assertFalse(server.email_files())

    def test_08_browser_recovery_and_login(self):
        # Browser exception logs can include a URL; suppress sensitive details on failure.
        try:
            from playwright.sync_api import sync_playwright
            self.account()
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(headless=True,
                    executable_path=os.environ.get('CHROMIUM_BIN', '/usr/bin/chromium'), args=['--no-sandbox'])
                try:
                    page = browser.new_page()
                    page_errors = []
                    reset_posts = []
                    page.on('pageerror', lambda error: page_errors.append(type(error).__name__))
                    page.on('request', lambda request: reset_posts.append(True)
                            if request.url.endswith('/api/auth/reset-password') else None)
                    page.goto(self.server.base + '/#/login')
                    page.get_by_role('link', name='Esqueci minha senha', exact=True).click()
                    page.locator('#recoveryEmail').fill(self.email)
                    page.get_by_text('Nesta versão de teste, o e-mail de recuperação é salvo como arquivo na pasta .local/mail do projeto. Abra o arquivo gerado nessa pasta para usar o link.', exact=True).wait_for()
                    previous = self.server.email_files()
                    page.locator('#forgotPassword [type=submit]').click()
                    page.get_by_text('Se houver uma conta para esse e-mail, o arquivo de recuperação será salvo em .local/mail, dentro da pasta do projeto. Abra o e-mail gerado para redefinir a senha.', exact=True).wait_for()
                    mail = self.server.wait_email(self.email, previous)
                    page.goto(mail['link'])
                    page.locator('#newPassword').fill(self.new_password)
                    page.locator('#confirmPassword').fill('different-password-value')
                    page.locator('#resetPassword [type=submit]').click()
                    page.get_by_text('As senhas precisam ser iguais.', exact=True).wait_for()
                    self.assertEqual(len(reset_posts), 0)
                    page.locator('#confirmPassword').fill(self.new_password)
                    page.locator('#resetPassword [type=submit]').click()
                    page.get_by_text('Senha atualizada. Entre com sua nova senha.', exact=True).wait_for()
                    self.assertFalse(mail['token'] in page.url)
                    self.assertEqual(len(reset_posts), 1)
                    page.locator('#em').fill(self.email)
                    page.locator('#pw').fill(self.new_password)
                    page.locator('#lf [type=submit]').click()
                    page.wait_for_url('**/#/painel')
                    self.assertEqual(page.request.get(self.server.base + '/api/auth/me').status, 200)
                    self.assertFalse(page_errors)
                finally:
                    browser.close()
        except Exception:
            raise AssertionError('Browser recovery verification failed; sensitive request details suppressed.') from None


if __name__ == '__main__':
    unittest.main(verbosity=2)
