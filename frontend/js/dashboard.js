/* Dashboard, team workload and reports derived exclusively from database records. */
function counts(items){return statuses.map(status=>[status,items.filter(t=>t.status===status).length])}
function bars(items){const max=Math.max(1,...items.map(i=>i[1]));return items.map(([label,n])=>`<div class="bar"><div class="l"><span>${esc(label)}</span><b>${n}</b></div><div class="tr"><i style="width:${n/max*100}%"></i></div></div>`).join('')||'<p class="mu">Nenhum dado no período.</p>'}
function categoryCounts(items){return categories.map(c=>[c,items.filter(t=>t.categoria===c).length])}
function kpis(items){return `<div class="kpis">${counts(items).map(([s,n])=>`<div class="card kpi"><span>${esc(s)}</span><b>${n}</b></div>`).join('')}</div>`}
function vPanel(){
 const open=tickets.filter(t=>t.status!=='Resolvido');
 page('painel',`<div class="hd"><div><h1>Visão geral</h1><p class="sub">Visão geral dos chamados registrados.</p></div><button class="btn2" id="refresh">Atualizar</button></div>${kpis(tickets)}<div class="hd"><h3>Fila de chamados ativos</h3><a class="lnk" href="#/${isAdmin()?'admin-chamados':'fila'}">Ver todos →</a></div>${ticketTable(open.slice(0,5))}<div class="grid2"><div class="card chart"><h3>Chamados ativos por administrador</h3>${bars(agents.map(a=>[a.nome,open.filter(t=>t.responsavel===a.email).length]))}</div><div class="card chart"><h3>Chamados por categoria</h3>${bars(categoryCounts(tickets))}</div></div>`);
 $('#refresh').onclick=route;
}
