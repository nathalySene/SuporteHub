"""Admin API + Chromium tests against disposable SQLite, without AWS or default passwords."""
import json
import os
from pathlib import Path
import subprocess
import sqlite3
import unittest
from urllib.parse import quote
from integration import Client, DOTNET, DLL, ROOT, PROJECT
import integration as support


class AdminTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = support.PersistenceTests
        cls.fixture.setUpClass()
        cls.base, cls.password = cls.fixture.base, cls.fixture.password
        result = subprocess.run([DOTNET, str(DLL), '--create-admin'], cwd=PROJECT, env=cls.fixture.env,
                                input=f'admin@example.com\nAdministradora Teste\n{cls.password}\n', text=True, capture_output=True)
        if result.returncode: raise RuntimeError('Administrator provisioning failed.')

    @classmethod
    def tearDownClass(cls):
        cls.fixture.tearDownClass()

    def login(self, email='admin@example.com'):
        client = Client(self.base)
        self.assertEqual(client.request('/api/auth/login', {'email': email, 'senha': self.password})[0], 200)
        return client

    def customer(self):
        return support.PersistenceTests().account()

    def new_ticket(self, admin, email):
        status, ticket = admin.request('/api/admin/chamados', {'titulo': 'Problema de acesso administrativo', 'descricao': 'Descrição persistida no banco', 'categoria': 'Acesso', 'prioridade': 'Alta', 'solicitante': email})
        self.assertEqual(status, 201)
        return ticket

    def test_01_permissions_and_public_signup_cannot_claim_admin(self):
        customer, email = self.customer()
        agent = self.login('agente@example.com')
        for client, expected in [(Client(self.base), 401), (customer, 403)]:
            self.assertEqual(client.request('/api/admin/usuarios')[0], expected)
            self.assertEqual(client.request('/api/admin/usuarios', {'nome': 'Teste', 'email': 'nao@example.com', 'senha': self.password, 'perfil': 'admin'})[0], expected)
        self.assertEqual(customer.request('/api/auth/me')[1]['perfil'], 'user')
        admin = self.login()
        status, users = admin.request('/api/admin/usuarios')
        self.assertEqual(status, 200)
        self.assertNotIn('senha', json.dumps(users).lower())
        self.assertNotIn('hash', json.dumps(users).lower())

    def test_02_management_notes_privacy_and_resolution(self):
        admin = self.login(); owner, email = self.customer(); outsider, _ = self.customer()
        ticket = self.new_ticket(admin, email); tid = ticket['id']
        self.assertEqual(ticket['usuario'], email)
        body = {'status': 'Em andamento', 'prioridade': 'Urgente', 'responsavel': 'agente@example.com'}
        self.assertEqual(owner.request(f'/api/chamados/{tid}/gerenciar', body, 'PATCH')[0], 403)
        invalid = {**body, 'responsavel': email}
        self.assertEqual(admin.request(f'/api/chamados/{tid}/gerenciar', invalid, 'PATCH')[0], 400)
        status, updated = admin.request(f'/api/chamados/{tid}/gerenciar', body, 'PATCH')
        self.assertEqual(status, 200)
        self.assertEqual((updated['status'], updated['prioridade'], updated['responsavel']), ('Em andamento', 'Urgente', 'agente@example.com'))
        self.assertEqual(admin.request(f'/api/chamados/{tid}/notas', {'texto': 'Nota privada da equipe'})[0], 200)
        self.assertEqual(owner.request(f'/api/chamados/{tid}/notas', {'texto': 'Inválida'})[0], 403)
        self.assertNotIn('Nota privada', json.dumps(owner.request(f'/api/chamados/{tid}')[1]))
        self.assertNotIn('Nota privada', json.dumps(owner.request('/api/chamados')[1]))
        self.assertEqual(outsider.request(f'/api/chamados/{tid}')[0], 404)
        self.assertEqual(self.login('agente@example.com').request(f'/api/chamados/{tid}/respostas', {'texto': 'Primeira resposta do suporte'})[0], 200)
        status, closed = admin.request(f'/api/chamados/{tid}/status', {'status': 'Resolvido'}, 'PATCH')
        self.assertEqual(status, 200); self.assertIsNotNone(closed['dataResolucao']); self.assertEqual(closed['resolvidoPor'], 'admin@example.com')
        status, unchanged = admin.request(f'/api/chamados/{tid}/gerenciar', {**body, 'status': 'Resolvido'}, 'PATCH')
        self.assertEqual(unchanged['dataResolucao'], closed['dataResolucao'])
        status, requester = admin.request('/api/admin/solicitantes/'+quote(email))
        self.assertEqual(status, 200); self.assertEqual(requester['resolvidos'], 1)
        self.assertNotIn('hash', json.dumps(requester).lower())
        self.fixture.stop(); self.fixture.start()
        persisted = admin.request(f'/api/chamados/{tid}')[1]
        self.assertEqual(persisted['notasInternas'][0]['texto'], 'Nota privada da equipe')
        self.assertEqual(persisted['dataResolucao'], closed['dataResolucao'])
        status, reopened = admin.request(f'/api/chamados/{tid}/status', {'status': 'Aberto'}, 'PATCH')
        self.assertEqual(status, 200); self.assertIsNone(reopened['dataResolucao']); self.assertIsNone(reopened['resolvidoPor'])

    def test_03_create_accounts_with_two_profiles_and_reject_invalid_role(self):
        admin = self.login()
        body = {'nome': 'Agente Novo', 'email': 'novo@example.com', 'senha': self.password, 'perfil': 'user'}
        status, account = admin.request('/api/admin/usuarios', body)
        self.assertEqual(status, 201); self.assertEqual(account['perfil'], 'user')
        self.assertEqual(admin.request('/api/admin/usuarios', body)[0], 409)
        self.assertEqual(self.login('novo@example.com').request('/api/admin/usuarios')[0], 403)
        status, created = admin.request('/api/admin/usuarios', {**body, 'email': 'novo-admin@example.com', 'perfil': 'admin'})
        self.assertEqual(status, 201); self.assertEqual(created['perfil'], 'admin')
        self.assertEqual(self.login('novo-admin@example.com').request('/api/admin/usuarios')[0], 200)
        self.assertEqual(admin.request('/api/admin/usuarios', {**body, 'email': 'invalido@example.com', 'perfil': 'agent'})[0], 400)

    def test_04_actual_browser_admin_workflow(self):
        from playwright.sync_api import sync_playwright
        admin = self.login(); _, email = self.customer()
        with sync_playwright() as p:
            browser = p.chromium.launch(executable_path=os.environ.get('CHROMIUM_BIN', '/usr/bin/chromium'), args=['--no-sandbox'])
            try:
                page = browser.new_page(viewport={'width': 1440, 'height': 1000}); errors=[]
                page.on('pageerror', lambda error: errors.append(str(error)))
                page.goto(self.base+'/#/login')
                self.assertEqual(page.locator('#googleLogin').count(), 0)
                page.locator('#em').fill('admin@example.com'); page.locator('#pw').fill(self.password)
                page.locator('#lf [type=submit]').click(); page.wait_for_url('**/#/painel')
                page.get_by_text('Área do administrador', exact=True).wait_for()
                page.goto(self.base+'/#/admin-novo'); page.locator('#requester').select_option(email)
                page.locator('#as').fill('=Ticket navegador <img src=x onerror=alert(1)>')
                page.locator('#ds').fill('Problema criado pela administração'); page.locator('#adminNew [type=submit]').click()
                page.wait_for_url('**/#/chamado/*'); page.locator('#manage').wait_for()
                self.assertEqual(page.locator('main img').count(), 0)
                page.locator('#agent').select_option('agente@example.com'); page.locator('#priority').select_option('Urgente')
                page.locator('#statusValue').select_option('Em andamento'); page.locator('#manage [type=submit]').click()
                page.get_by_text('Prioridade alterada para Urgente.', exact=True).wait_for()
                page.locator('#replyMode').select_option('notas'); page.locator('#rt').fill('Nota interna via navegador')
                page.locator('#adminReply [type=submit]').click(); page.get_by_text('Nota interna via navegador', exact=True).wait_for()
                page.locator('#replyMode').select_option('respostas'); page.locator('#rt').fill('Resposta pública via navegador')
                page.locator('#adminReply [type=submit]').click(); page.get_by_text('Resposta pública via navegador', exact=True).wait_for()
                page.locator('#resolve').click(); page.get_by_text('Status alterado para Resolvido.', exact=True).wait_for()
                page.goto(self.base+'/#/atendidos'); page.locator('#q').fill('Ticket navegador'); page.locator('#lt a.row').wait_for()
                self.assertEqual(page.locator('#lt a.row').count(), 1)
                with page.expect_download() as download: page.locator('#export').click()
                csv=Path(download.value.path()).read_text(encoding='utf-8-sig')
                self.assertIn("'=Ticket navegador", csv)
                page.goto(self.base+'/#/admin-chamados'); page.locator('#q').fill('Não existe esse assunto'); page.get_by_text('Nenhum chamado encontrado neste período.').wait_for()
                page.locator('#q').fill('Ticket navegador'); page.locator('#priorityFilter').select_option('Baixa'); page.get_by_text('Nenhum chamado encontrado neste período.').wait_for()
                page.locator('#priorityFilter').select_option('Urgente'); page.locator('#lt a.row').wait_for()
                page.goto(self.base+'/#/admin-relatorios'); page.locator('.donut').wait_for()
                self.assertNotRegex(page.locator('main').inner_text().lower(), r'avaliação|satisfação')
                self.assertIn('Taxa de resolução', page.locator('main').inner_text())
                page.evaluate('window.print = () => window.printRequested = true')
                page.locator('#printReport').click(); self.assertTrue(page.evaluate('window.printRequested'))
                pdf=page.pdf(format='A4',print_background=True); self.assertTrue(pdf.startswith(b'%PDF-'))
                if os.environ.get('ADMIN_SCREENSHOT'): page.screenshot(path=os.environ['ADMIN_SCREENSHOT'], full_page=True)
                page.set_viewport_size({'width':390,'height':844}); page.goto(self.base+'/#/admin-relatorios'); page.locator('.donut').wait_for()
                self.assertTrue(page.evaluate('document.documentElement.scrollWidth <= window.innerWidth'))
                page.goto(self.base+'/#/usuarios'); page.locator('#accountName').fill('Usuário pela tela')
                page.locator('#accountEmail').fill('agente-tela@example.com'); page.locator('#accountPassword').fill(self.password)
                page.locator('#addUser [type=submit]').click(); page.get_by_text('Usuário pela tela', exact=True).wait_for()
                for _ in range(7): self.new_ticket(admin, email)
                page.goto(self.base+'/#/admin-chamados'); page.locator('#q').fill('Problema de acesso administrativo')
                page.locator('#lt a.row').first.wait_for(); self.assertEqual(page.locator('#lt a.row').count(), 6)
                page.locator('#next').click(); self.assertGreater(page.locator('#lt a.row').count(), 0)
                page.locator('#prev').click(); self.assertEqual(page.locator('#lt a.row').count(), 6)
                self.assertEqual(errors, [])
            finally:
                browser.close()

    def test_05_promote_existing_account_without_losing_password_or_tickets(self):
        owner, email = self.customer()
        ticket = support.PersistenceTests().ticket(owner)
        result = subprocess.run([DOTNET, str(DLL), '--promote-admin'], cwd=PROJECT, env=self.fixture.env,
                                input=email+'\n', text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0)
        self.assertEqual(owner.request('/api/auth/me')[0], 401)
        promoted = self.login(email)
        self.assertEqual(promoted.request('/api/auth/me')[1]['perfil'], 'admin')
        self.assertEqual(promoted.request(f'/api/chamados/{ticket["id"]}')[1]['usuario'], email)
        self.assertEqual(promoted.request('/api/admin/usuarios')[0], 200)

    def test_06_legacy_accounts_keep_credentials_and_records(self):
        owner, email = self.customer()
        ticket = support.PersistenceTests().ticket(owner)
        result = subprocess.run([DOTNET, str(DLL), '--create-admin'], cwd=PROJECT, env=self.fixture.env,
                                input=f'legacy-admin@example.com\nAdministrador Antigo\n{self.password}\n', text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0)
        self.fixture.stop()
        database = self.fixture.env['Storage__DatabasePath']
        hashes = {}
        with sqlite3.connect(database) as db:
            for address, old_role in [(email, 'client'), ('legacy-admin@example.com', 'agent')]:
                payload = json.loads(db.execute("SELECT Data FROM Records WHERE PK='USERS' AND SK=?", (address,)).fetchone()[0])
                hashes[address] = payload['SenhaHash']; payload['Perfil'] = old_role
                db.execute("UPDATE Records SET Data=?, Version=Version+1 WHERE PK='USERS' AND SK=?", (json.dumps(payload), address))
        self.fixture.start()
        legacy_user = self.login(email); legacy_admin = self.login('legacy-admin@example.com')
        self.assertEqual(legacy_user.request('/api/auth/me')[1]['perfil'], 'user')
        self.assertEqual(legacy_admin.request('/api/auth/me')[1]['perfil'], 'admin')
        self.assertEqual(legacy_admin.request('/api/admin/usuarios')[0], 200)
        self.assertEqual(legacy_user.request('/api/admin/usuarios')[0], 403)
        self.assertEqual(legacy_user.request(f'/api/chamados/{ticket["id"]}')[1]['usuario'], email)
        with sqlite3.connect(database) as db:
            for address, old_role in [(email, 'client'), ('legacy-admin@example.com', 'agent')]:
                payload = json.loads(db.execute("SELECT Data FROM Records WHERE PK='USERS' AND SK=?", (address,)).fetchone()[0])
                self.assertEqual(payload['SenhaHash'], hashes[address])
                self.assertEqual(payload['Perfil'], old_role)  # no bulk startup rewrite

if __name__ == '__main__':
    unittest.main(verbosity=2)
