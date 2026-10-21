/* Shared UI, authenticated requests and routing. */
const $=s=>document.querySelector(s);
const esc=v=>String(v??"").replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const statuses=['Aberto','Em andamento','Aguardando','Resolvido'];
const categories=['Acesso','Hardware','Rede','Sistemas','Equipamentos'];
const priorities=['Baixa','Média','Alta','Urgente'];
const colors={Aberto:['#dbeafe','#1d4ed8'],'Em andamento':['#fef3c7','#b45309'],Aguardando:['#ede9fe','#6d28d9'],Resolvido:['#d1fae5','#047857']};
const priorityColors={Baixa:'#8e7ba3','Média':'#d97706',Alta:'#dc2626',Urgente:'#9f1239'};
const pill=s=>{const c=colors[s]||['#ede9fe','#6d28d9'];return `<span class="pill" style="background:${c[0]};color:${c[1]}">${esc(s)}</span>`};
const date=s=>s?new Date(s).toLocaleString('pt-BR'):'—';
let user=null,tickets=[],agents=[],routeVersion=0;
const isAgent=()=>user?.perfil==='agent';
const render=html=>{$('#app').innerHTML=html};
const toast=message=>{const e=$('#toast');e.textContent=message;e.classList.add('on');clearTimeout(toast.timer);toast.timer=setTimeout(()=>e.classList.remove('on'),4000)};
async function api(path,options={}){
 let response;
 const multipart=options.body instanceof FormData;
 try{response=await fetch('/api'+path,{credentials:'same-origin',...options,signal:options.signal||AbortSignal.timeout(20000),headers:{...(multipart?{}:{'Content-Type':'application/json'}),'X-Requested-With':'SuporteHub',...options.headers}})}
 catch{throw new Error('Não foi possível conectar ao servidor. Verifique a conexão e tente novamente.')}
 const data=response.status===204?null:await response.json().catch(()=>null);
 if(!response.ok){const err=new Error(data?.mensagem||(data?.errors?Object.values(data.errors).flat().join(' '):null)||({401:'E-mail ou senha inválidos, ou sessão expirada.',403:'Você não tem permissão para esta ação.',404:'Registro não encontrado.',409:'O registro foi atualizado. Recarregue e tente novamente.',429:'Muitas tentativas. Aguarde um minuto antes de tentar novamente.'}[response.status])||'Não foi possível concluir a operação.');err.status=response.status;throw err}
 return data;
}
function shell(active,content){
 const links=isAgent()?[['painel','🌈','Painel'],['fila','🎀','Fila de chamados'],['meus','💌','Meus atendimentos'],['equipe','👯','Equipe'],['relatorios','📊','Relatórios']]:[['novo','✨','Novo chamado'],['chamados','📬','Meus chamados']];
 const initials=user.nome.split(/\s+/).slice(0,2).map(s=>s[0]).join('').toUpperCase();
 return `<div class="bub b1"></div><div class="bub b2"></div><div class="shell"><aside><div class="logo"><div class="ico">♥</div>SuporteHub</div><nav>${links.map(([path,icon,label])=>`<a href="#/${path}" class="${path===active?'on':''}">${icon} ${label}</a>`).join('')}</nav><a class="lnk" href="#/conta" style="margin-top:auto;padding:0 8px">Minha conta</a><button class="user" id="out" title="Sair" style="margin-top:0"><span class="av">${esc(initials)}</span><span><b>${esc(user.nome)}</b><small>${isAgent()?'Agente de suporte':'Cliente'} · Sair</small></span></button></aside><main>${content}</main></div>`;
}
function page(active,content){render(shell(active,content));$('#out').onclick=async()=>{try{await api('/auth/logout',{method:'POST'});user=null;tickets=[];agents=[];location.hash='#/login'}catch(e){toast(e.message)}}}
async function submit(form,task){
 const button=form.querySelector('[type=submit]'),error=form.querySelector('[role=alert]');
 if(button.disabled)return;
 const label=button.textContent;button.disabled=true;button.textContent=form.dataset.pendingLabel||'Salvando…';if(error)error.textContent='';
 try{await task()}catch(e){if(error&&form.isConnected)error.textContent=e.message;else toast(e.message);if(e.status===401&&user){user=null;location.hash='#/login'}}
 finally{button.disabled=false;button.textContent=label}
}
async function route(){
 const version=++routeVersion,fragment=location.hash.replace(/^#\//,''),question=fragment.indexOf('?'),path=question<0?fragment:fragment.slice(0,question),params=new URLSearchParams(question<0?'':fragment.slice(question+1)),[view='login',id]=path.split('/');
 if(view==='esqueci-senha'){vForgotPassword();return}
 if(view==='redefinir-senha'){vResetPassword(params);return}
 if(!user){if(view==='cadastro')vRegister();else if(view!=='login'&&view!==''){location.hash='#/login'}else vLogin(params);return}
 if(['login','cadastro',''].includes(view)){location.hash=isAgent()?'#/painel':'#/novo';return}
 if(!isAgent()&&['painel','fila','meus','equipe','relatorios'].includes(view)){location.hash='#/chamados';return}
 if(view==='conta'){vAccount(params);return}
 if(view==='novo'){vNovo();return}
 page(view,'<p role="status">Carregando…</p>');
 try{
  if(view==='chamado'){
   const [ticket,team]=await Promise.all([api('/chamados/'+encodeURIComponent(id)),isAgent()?api('/equipe'):Promise.resolve([])]);
   if(version!==routeVersion)return;agents=team;vTicket(ticket);
  }else{
   const [items,team]=await Promise.all([api('/chamados'),isAgent()?api('/equipe'):Promise.resolve([])]);
   if(version!==routeVersion)return;tickets=items;agents=team;
   if(['chamados','fila','meus'].includes(view))vList(view);
   else if(view==='equipe')vTeam();else if(view==='relatorios')vReports();else if(isAgent())vPanel();else vList('chamados');
  }
 }catch(e){
  if(version!==routeVersion)return;
  if(e.status===401){user=null;location.hash='#/login';return}
  page(view,`<p class="err" role="alert">${esc(e.message)}</p><button class="btn2" id="retry">Tentar novamente</button>`);$('#retry').onclick=route;
 }
 scrollTo(0,0);
}
addEventListener('hashchange',()=>{if(initialized)route()});
let initialized=false;
async function initialize(){
 render('<main><p role="status">Verificando sessão…</p></main>');
 try{user=await api('/auth/me')}
 catch(e){if(e.status!==401){render(`<main><p class="err" role="alert">${esc(e.message)}</p><button class="btn2" id="retry">Tentar novamente</button></main>`);$('#retry').onclick=initialize;return}}
 initialized=true;route();
}
addEventListener('DOMContentLoaded',initialize);
