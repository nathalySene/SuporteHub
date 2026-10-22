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
let user=null,tickets=[],agents=[],routeVersion=0,serverInfo=null;
const databaseLabel=()=>serverInfo?.database==='DynamoDB'?'Banco AWS':serverInfo?.database==='SQLite'?'Banco local':'';
const isAdmin=()=>user?.perfil==='admin';
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
 const links=isAdmin()?[['painel','Visão geral'],['admin-chamados','Chamados'],['atendidos','Atendidos'],['admin-relatorios','Relatórios'],['usuarios','Usuários']]:[['chamados','Meus chamados'],['novo','Novo chamado']];
 const initials=user.nome.split(/\s+/).slice(0,2).map(s=>s[0]).join('').toUpperCase();
 return `<div class="bub b1"></div><div class="bub b2"></div><div class="shell"><aside><div class="logo"><div class="ico" aria-hidden="true">S</div>SuporteHub</div><span class="admin-badge">${isAdmin()?'Área do administrador':'Área do usuário'}</span><nav>${links.map(([path,label])=>`<a href="#/${path}" class="${path===active?'on':''}">${label}</a>`).join('')}</nav><button class="user" id="out" title="Sair"><span class="av">${esc(initials)}</span><span><b>${esc(user.nome)}</b><small>${isAdmin()?'Administrador':'Usuário'} · Sair</small></span></button></aside><main>${content}</main></div>`;
}
function page(active,content){render(shell(active,content));$('#out').onclick=async()=>{try{await api('/auth/logout',{method:'POST'});user=null;tickets=[];agents=[];location.hash='#/login'}catch(e){toast(e.message)}}}
async function submit(form,task){
 const button=form.querySelector('[type=submit]'),error=form.querySelector('[role=alert]');
 if(button.disabled)return;
 const label=button.textContent;button.disabled=true;button.textContent='Salvando…';if(error)error.textContent='';
 try{await task()}catch(e){if(error&&form.isConnected)error.textContent=e.message;else toast(e.message);if(e.status===401&&user){user=null;location.hash='#/login'}}
 finally{button.disabled=false;button.textContent=label}
}
async function route(){
 const version=++routeVersion,[view='login',id]=location.hash.replace(/^#\//,'').split('/');
 if(!user){if(view==='cadastro')vRegister();else if(view!=='login'&&view!==''){location.hash='#/login'}else vLogin();return}
 if(['login','cadastro',''].includes(view)){location.hash=isAdmin()?'#/painel':'#/chamados';return}
 const legacy={fila:'admin-chamados',meus:'admin-chamados',equipe:'usuarios',relatorios:'admin-relatorios'};
 if(Object.hasOwn(legacy,view)){location.hash=isAdmin()?'#/'+legacy[view]:'#/chamados';return}
 const adminViews=['painel','admin-chamados','atendidos','admin-relatorios','admin-novo','usuarios'];
 if(adminViews.includes(view)&&!isAdmin()){location.hash='#/chamados';return}
 if(![...adminViews,'novo','chamados','chamado'].includes(view)){location.hash=isAdmin()?'#/painel':'#/chamados';return}
 if(view==='novo'){if(isAdmin())location.hash='#/admin-novo';else vNovo();return}
 if(view==='chamados'&&isAdmin()){location.hash='#/admin-chamados';return}
 page(view,'<p role="status">Carregando…</p>');
 try{
  if(view==='chamado'){
   const [ticket,team]=await Promise.all([api('/chamados/'+encodeURIComponent(id)),isAdmin()?api('/equipe'):Promise.resolve([])]);
   if(version!==routeVersion)return;agents=team;if(isAdmin())await vAdminTicket(ticket,version);else vTicket(ticket);
  }else if(view==='usuarios'){
   const accounts=await api('/admin/usuarios');if(version!==routeVersion)return;vAdminUsers(accounts);
  }else{
   const [items,team]=await Promise.all([api('/chamados'),isAdmin()?api('/equipe'):Promise.resolve([])]);
   if(version!==routeVersion)return;tickets=items;agents=team;
   if(view==='admin-novo')await vAdminNew(version);
   else if(view==='admin-chamados'||view==='atendidos')vAdminList(view);
   else if(view==='admin-relatorios')vAdminReports();
   else if(isAdmin())vPanel();else vList('chamados');
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
 try{const results=await Promise.allSettled([api('/health'),api('/auth/me')]);if(results[0].status==='fulfilled')serverInfo=results[0].value;if(results[1].status==='rejected')throw results[1].reason;user=results[1].value}
 catch(e){if(e.status!==401){render(`<main><p class="err" role="alert">${esc(e.message)}</p><button class="btn2" id="retry">Tentar novamente</button></main>`);$('#retry').onclick=initialize;return}}
 initialized=true;route();
}
addEventListener('DOMContentLoaded',initialize);
