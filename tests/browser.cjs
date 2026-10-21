/* End-to-end UI checks using an isolated temporary SQLite database.
 * Requires Playwright, /usr/bin/chromium and .NET 10. No Docker, WSL or AWS.
 * This harness never uses existing application databases or accounts.
 */
const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {spawn}=require('node:child_process');
const {randomUUID,randomBytes}=require('node:crypto');
const {mkdtempSync,rmSync,readFileSync}=require('node:fs');
const path=require('node:path');
const os=require('node:os');
const net=require('node:net');
(async()=>{
 const root=path.resolve(__dirname,'..'),project=path.join(root,'backend/SuporteHub.API');
 const state=mkdtempSync(path.join(os.tmpdir(),'suportehub-browser-'));
 const dotnet=process.env.DOTNET_BIN||'dotnet',dll=path.join(project,'bin/Debug/net10.0/SuporteHub.API.dll');
 const password=randomBytes(24).toString('hex'),email=`cliente-${randomUUID()}@example.com`;
 const env={...Object.fromEntries(Object.entries(process.env).filter(([key])=>!key.startsWith('DynamoDb__'))),ASPNETCORE_ENVIRONMENT:'Development',Storage__Provider:'Sqlite',Storage__DatabasePath:path.join(state,'application.sqlite3'),Storage__KeysPath:path.join(state,'keys'),Storage__UploadsPath:path.join(state,'uploads')};
 let app,browser,log='';
 try{
  const port=await new Promise(resolve=>{const socket=net.createServer();socket.listen(0,'127.0.0.1',()=>{const p=socket.address().port;socket.close(()=>resolve(p))})});
  const base=`http://127.0.0.1:${port}`;env.ASPNETCORE_URLS=base;
  app=spawn(dotnet,[dll],{cwd:root,env});app.stdout.on('data',b=>log=(log+b).slice(-20000));app.stderr.on('data',b=>log=(log+b).slice(-20000));
  let ready=false;for(let i=0;i<100;i++){try{ready=(await fetch(base+'/api/health')).ok;if(ready)break}catch{}if(app.exitCode!==null||app.signalCode!==null)throw Error(log);await new Promise(r=>setTimeout(r,100))}assert.ok(ready,log);
  const health=await (await fetch(base+'/api/health')).json();assert.equal(health.database.toLowerCase(),'sqlite');assert.equal(readFileSync(env.Storage__DatabasePath).subarray(0,16).toString(),'SQLite format 3\0');console.log('PASS: persistent SQLite startup without Docker or AWS');
  browser=await chromium.launch({headless:true,executablePath:process.env.CHROMIUM_BIN||'/usr/bin/chromium',args:['--no-sandbox']});
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  const navigate=async route=>{await page.goto(base+'/#/'+route)};
  const login=async address=>{await page.locator('#em').fill(address);await page.locator('#pw').fill(password);await page.locator('#lf [type=submit]').click()};
  await navigate('cadastro');await page.getByText('Primeiro acesso: crie a conta de suporte. Depois você poderá cadastrar contas de cliente.',{exact:true}).waitFor();
  await page.locator('#name').fill('Agente Teste');await page.locator('#em').fill('agente@example.com');await page.locator('#pw').fill(password);await page.locator('#register [type=submit]').click();await page.waitForURL('**/#/painel');await page.locator('.kpi b').first().waitFor();assert.equal(await page.locator('.kpi b').first().textContent(),'0');
  await page.locator('#out').click();await page.waitForURL('**/#/login');console.log('PASS: first local account initializes support without default credentials');
  await navigate('cadastro');await page.locator('#name').fill('Cliente <Teste>');await page.locator('#em').fill(email);await page.locator('#pw').fill(password);await page.locator('#register [type=submit]').click();await page.waitForURL('**/#/novo');
  console.log('PASS: registration creates an authenticated customer');
  await navigate('chamados');await page.locator('#lt .empty').waitFor();
  await navigate('novo');await page.locator('#as').fill('Problema <script>window.injected=true</script>');await page.locator('#ct').selectOption('Sistemas');await page.locator('#ds').fill('Descrição original no banco.');
  const file={name:'evidencia.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.7\nTest evidence\n%%EOF')};await page.locator('#files').setInputFiles(file);
  await page.locator('#nf [type=submit]').click();await page.waitForURL(/#\/chamado\/\d+$/);await page.getByText('Descrição original no banco.',{exact:true}).waitFor();const detail=page.url();
  await page.getByRole('link',{name:'evidencia.pdf',exact:true}).waitFor();assert.equal(await page.evaluate(()=>window.injected),undefined);
  await page.locator('#rt').fill('Mais informações do cliente.');await page.locator('#reply [type=submit]').click();await page.getByText('Mais informações do cliente.',{exact:true}).waitFor();await page.reload();await page.getByText('Mais informações do cliente.',{exact:true}).waitFor();
  const downloadPromise=page.waitForEvent('download');await page.getByRole('link',{name:'evidencia.pdf',exact:true}).click();const download=await downloadPromise;assert.equal(download.suggestedFilename(),'evidencia.pdf');
  console.log('PASS: create, attach/download, reply, reload and HTML escaping');
  await navigate('chamados');await page.locator('#lt a.row').waitFor();await page.locator('#q').fill('nothing-to-find');await page.locator('#lt .empty').waitFor();await page.locator('#q').fill('Problema');await page.locator('#lt a.row').waitFor();
  await page.route('**/api/chamados',r=>r.abort());await page.locator('#refresh').click();await page.locator('[role=alert]').waitFor();await page.unroute('**/api/chamados');await page.locator('#retry').click();await page.locator('#lt a.row').waitFor();
  console.log('PASS: search and network-error recovery');
  await page.locator('#out').click();await page.waitForURL('**/#/login');await page.locator('#em').fill(email);await page.locator('#pw').fill('incorrect-password');await page.locator('#lf [type=submit]').click();await page.getByText('E-mail ou senha inválidos.',{exact:true}).waitFor();await login('agente@example.com');await page.waitForURL('**/#/painel');await page.locator('.kpi b').first().waitFor();assert.equal(await page.locator('.kpi b').first().textContent(),'1');
  await page.goto(detail);await page.locator('#agent').selectOption('agente@example.com');await page.locator('#assign [type=submit]').click();await page.getByText('Chamado atribuído a Agente Teste.',{exact:true}).waitFor();await page.locator('#statusValue').selectOption('Em andamento');await page.locator('#status [type=submit]').click();await page.getByText('Status alterado para Em andamento.',{exact:true}).waitFor();await page.locator('#rt').fill('Resposta persistente do suporte.');await page.locator('#reply [type=submit]').click();await page.getByText('Resposta persistente do suporte.',{exact:true}).waitFor();
  await navigate('meus');await page.locator('#lt a.row').waitFor();await navigate('equipe');await page.getByText('1 chamado(s) ativo(s)',{exact:true}).waitFor();
  await navigate('relatorios');await page.locator('#period').selectOption('7');const csvPromise=page.waitForEvent('download');await page.locator('#export').click();assert.equal((await csvPromise).suggestedFilename(),'suportehub-chamados.csv');
  console.log('PASS: agent login, assignment, status, reply, personal queue, team and report export');
  await page.locator('#out').click();await page.waitForURL('**/#/login');await login(email);await page.waitForURL('**/#/novo');await page.goto(detail);await page.getByText('Resposta persistente do suporte.',{exact:true}).waitFor();await page.locator('#statusValue').selectOption('Resolvido');await page.locator('#status [type=submit]').click();await page.getByText('Status alterado para Resolvido.',{exact:true}).waitFor();assert.equal(await page.locator('#rt').isDisabled(),true);
  await page.locator('#out').click();await page.waitForURL('**/#/login');await login('agente@example.com');await page.waitForURL('**/#/painel');await page.locator('.kpi b').last().waitFor();assert.equal(await page.locator('.kpi b').last().textContent(),'1');
  assert.deepEqual(errors,[]);console.log('PASS: customer resolution persists in dashboard; no JavaScript errors');
 }finally{
  if(browser)await browser.close();if(app&&app.exitCode===null&&app.signalCode===null){await new Promise(resolve=>{app.once('exit',resolve);app.kill()})}rmSync(state,{recursive:true,force:true});
 }
})().catch(e=>{console.error(e);process.exit(1)});
