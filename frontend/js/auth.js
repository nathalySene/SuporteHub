/* Authentication uses server-issued HttpOnly cookies; passwords/tokens are never put in web storage. */
function authShell(content){render(`<div class="login"><div class="brand"><div class="logo"><div class="ico">♥</div>SuporteHub</div><div><h2>Precisa de ajuda?<br>A gente resolve 💜</h2><p>Abra um chamado, acompanhe o atendimento e converse com a equipe de suporte.</p><ul><li>✓ Histórico de atendimento</li><li>✓ Respostas e anexos</li><li>✓ Acompanhamento de status</li></ul></div><small>SuporteHub · Atendimento em um só lugar</small></div><div class="fwrap">${content}</div></div>`)}
async function setupHint(element){
 try{const setup=await api('/auth/setup');if(element.isConnected&&setup.primeiroAcesso)element.textContent='Primeiro acesso: crie a conta de suporte. Depois você poderá cadastrar contas de cliente.'}catch{}
}
const googleErrors={
 'google-unavailable':'O login com Google está indisponível no momento. Entre com seu e-mail e senha.',
 'google-cancelled':'O acesso pelo Google foi cancelado. Você pode tentar novamente.',
 'google-invalid':'Não foi possível confirmar seu acesso pelo Google. Tente novamente.',
 'google-account-exists':'Já existe uma conta com esse e-mail. Entre com sua senha e conecte o Google em Minha conta.',
 'google-conflict':'Não foi possível conectar essa conta Google. Tente novamente.',
 'google-setup-required':'Crie primeiro a conta de suporte em Criar conta. Depois você poderá entrar com o Google.',
 'google-email-mismatch':'Use a conta Google com o mesmo e-mail da sua conta SuporteHub.',
 'google-already-linked':'Essa conta Google já está conectada. Entre com a conta correspondente.'
};
function googleMessage(params){const code=params.get('erro');return code?(Object.hasOwn(googleErrors,code)?googleErrors[code]:'Não foi possível concluir o acesso pelo Google. Tente novamente.'):''}
async function googleAvailability(button,status){
 try{const providers=await api('/auth/providers');if(!button.isConnected)return;button.disabled=providers.google!==true;status.textContent=providers.google===true?'':'O login com Google ainda não está disponível.'}
 catch{if(button.isConnected){button.disabled=true;status.textContent='O login com Google está indisponível no momento.'}}
}
function googleButton(){return '<div class="or">ou</div><button type="button" class="btn2" id="googleLogin" disabled>Continuar com Google</button><p class="mu" id="googleStatus" role="status"></p>'}
function bindGoogleLogin(){const button=$('#googleLogin');button.onclick=()=>location.assign('/api/auth/google/start');googleAvailability(button,$('#googleStatus'))}
function vLogin(params=new URLSearchParams()){
 const message=googleMessage(params),reset=params.get('passwordReset')==='1';
 authShell(`<form id="lf"><div><h1>Que bom te ver! 👋</h1><p class="sub">Entre com sua conta para continuar.</p><p class="sub" id="setupHint"></p></div>${reset?'<p role="status">Senha atualizada. Entre com sua nova senha.</p>':''}<div><label class="l" for="em">E-mail</label><input class="in" id="em" type="email" required maxlength="254" autocomplete="username"></div><div><label class="l" for="pw">Senha</label><input class="in" id="pw" type="password" required maxlength="128" autocomplete="current-password"></div><div class="opts"><label><input id="remember" type="checkbox"> Lembrar de mim</label><a class="lnk" href="#/esqueci-senha">Esqueci minha senha</a></div><p class="err" id="le" role="alert">${esc(message)}</p><button type="submit" class="btn">Entrar ✨</button>${googleButton()}<p>Ainda não tem conta? <a class="lnk" href="#/cadastro">Criar conta</a></p></form>`);
 setupHint($('#setupHint'));bindGoogleLogin();
 $('#lf').onsubmit=e=>{e.preventDefault();const form=e.currentTarget;submit(form,async()=>{user=await api('/auth/login',{method:'POST',body:JSON.stringify({email:$('#em').value.trim(),senha:$('#pw').value,lembrar:$('#remember').checked})});location.hash=isAgent()?'#/painel':'#/novo'})};
}
function vRegister(){
 authShell(`<form id="register"><div><h1>Crie sua conta 💜</h1><p class="sub">Acompanhe seus chamados com segurança.</p><p class="sub" id="setupHint"></p></div><div><label class="l" for="name">Nome</label><input class="in" id="name" required minlength="2" maxlength="100" autocomplete="name"></div><div><label class="l" for="em">E-mail</label><input class="in" id="em" type="email" required maxlength="254" autocomplete="username"></div><div><label class="l" for="pw">Senha (10 a 128 caracteres)</label><input class="in" id="pw" type="password" required minlength="10" maxlength="128" autocomplete="new-password"></div><p class="err" role="alert"></p><button type="submit" class="btn">Criar conta</button>${googleButton()}<a class="lnk" href="#/login">Já tenho conta</a></form>`);
 setupHint($('#setupHint'));bindGoogleLogin();
 $('#register').onsubmit=e=>{e.preventDefault();submit(e.currentTarget,async()=>{user=await api('/auth/cadastro',{method:'POST',body:JSON.stringify({nome:$('#name').value.trim(),email:$('#em').value.trim(),senha:$('#pw').value})});location.hash=isAgent()?'#/painel':'#/novo'})};
}
function vForgotPassword(){
 authShell(`<form id="forgotPassword" data-pending-label="Enviando…"><div><h1>Esqueceu sua senha?</h1><p class="sub">Informe seu e-mail para receber um link e escolher uma nova senha.</p></div><div><label class="l" for="recoveryEmail">E-mail</label><input class="in" id="recoveryEmail" type="email" required maxlength="254" autocomplete="username"></div><p class="err" role="alert"></p><p id="recoveryStatus" class="mu" role="status"></p><button type="submit" class="btn" disabled>Enviar link de recuperação</button><a class="lnk" href="#/login">Voltar para entrar</a></form>`);
 const form=$('#forgotPassword'),button=form.querySelector('[type=submit]'),status=$('#recoveryStatus');let localEmailPreview=false;
 api('/auth/providers').then(p=>{if(!form.isConnected)return;localEmailPreview=p.localEmailPreview===true;button.disabled=p.passwordReset!==true;if(p.passwordReset!==true)status.textContent='A recuperação por e-mail está indisponível no momento. Tente novamente mais tarde.';else if(localEmailPreview)status.textContent='Nesta versão de teste, o e-mail de recuperação é salvo como arquivo na pasta .local/mail do projeto. Abra o arquivo gerado nessa pasta para usar o link.'}).catch(()=>{if(form.isConnected)status.textContent='Não foi possível verificar o envio de e-mails. Atualize a página e tente novamente.'});
 form.onsubmit=e=>{e.preventDefault();const email=$('#recoveryEmail').value.trim();status.textContent='';submit(form,async()=>{await api('/auth/forgot-password',{method:'POST',body:JSON.stringify({email})});if(form.isConnected)status.textContent=localEmailPreview?'Se houver uma conta para esse e-mail, o arquivo de recuperação será salvo em .local/mail, dentro da pasta do projeto. Abra o e-mail gerado para redefinir a senha.':'Se houver uma conta para esse e-mail, você receberá um link para redefinir sua senha. Confira também a pasta de spam.'})};
}
function vResetPassword(params){
 const email=params.get('email')||'',token=params.get('token')||'';
 if(!email||!token){authShell('<form><div><h1>Link de recuperação incompleto</h1><p class="sub">Abra o link recebido por e-mail ou solicite um novo.</p></div><a class="btn" href="#/esqueci-senha">Solicitar novo link</a><a class="lnk" href="#/login">Voltar para entrar</a></form>');return}
 authShell(`<form id="resetPassword"><div><h1>Escolha uma nova senha</h1><p class="sub">Use de 10 a 128 caracteres.</p></div><div><label class="l" for="resetEmail">E-mail</label><input class="in" id="resetEmail" type="email" value="${esc(email)}" readonly autocomplete="username"></div><div><label class="l" for="newPassword">Nova senha</label><input class="in" id="newPassword" type="password" required minlength="10" maxlength="128" autocomplete="new-password"></div><div><label class="l" for="confirmPassword">Confirme a nova senha</label><input class="in" id="confirmPassword" type="password" required minlength="10" maxlength="128" autocomplete="new-password"></div><p class="err" role="alert"></p><button type="submit" class="btn">Salvar nova senha</button><a class="lnk" href="#/esqueci-senha">Solicitar outro link</a></form>`);
 const form=$('#resetPassword');
 form.onsubmit=e=>{e.preventDefault();const password=$('#newPassword').value;if(password!==$('#confirmPassword').value){form.querySelector('[role=alert]').textContent='As senhas precisam ser iguais.';return}submit(form,async()=>{await api('/auth/reset-password',{method:'POST',body:JSON.stringify({email,token,senha:password})});user=null;tickets=[];agents=[];history.replaceState(null,'',location.pathname+location.search+'#/login?passwordReset=1');route()})};
}
function vAccount(params=new URLSearchParams()){
 const message=googleMessage(params),linked=user.googleLinked===true;
 page('conta',`<div><h1>Minha conta 💜</h1><p class="sub">Confira seus dados e as formas de entrar.</p></div><div class="card box" style="max-width:600px"><h3>${esc(user.nome)}</h3><p>${esc(user.email)}</p><h3 style="margin-top:28px">Google</h3>${params.get('google')==='linked'?'<p role="status">Sua conta Google foi conectada.</p>':''}<form id="googleLink" data-pending-label="Conectando…"><p class="err" role="alert">${esc(message)}</p><p class="mu" id="googleStatus" role="status">${linked?'Sua conta Google está conectada.':'Conecte o Google para entrar com a mesma conta.'}</p>${linked?'':'<button type="submit" class="btn2" disabled>Conectar Google</button>'}</form><p style="margin-top:28px"><a class="lnk" href="#/esqueci-senha">Redefinir minha senha por e-mail</a></p></div>`);
 if(linked)return;
 const form=$('#googleLink'),button=form.querySelector('[type=submit]');googleAvailability(button,$('#googleStatus'));
 form.onsubmit=e=>{e.preventDefault();submit(form,async()=>{const result=await api('/auth/google/link',{method:'POST'});const target=new URL(result.url,location.origin);if(target.origin!==location.origin||target.pathname!=='/api/auth/google/start')throw new Error('Não foi possível conectar ao Google. Tente novamente.');location.assign(target.href)})};
}
