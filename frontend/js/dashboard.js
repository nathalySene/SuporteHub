/* Dashboard, team workload and reports derived exclusively from database records. */
function counts(items){return statuses.map(status=>[status,items.filter(t=>t.status===status).length])}
function bars(items){const max=Math.max(1,...items.map(i=>i[1]));return items.map(([label,n])=>`<div class="bar"><div class="l"><span>${esc(label)}</span><b>${n}</b></div><div class="tr"><i style="width:${n/max*100}%"></i></div></div>`).join('')||'<p class="mu">Nenhum dado no período.</p>'}
function categoryCounts(items){return categories.map(c=>[c,items.filter(t=>t.categoria===c).length])}
function kpis(items){return `<div class="kpis">${counts(items).map(([s,n])=>`<div class="card kpi"><span>${esc(s)}</span><b>${n}</b></div>`).join('')}</div>`}
function vPanel(){
 const open=tickets.filter(t=>t.status!=='Resolvido');
 page('painel',`<div class="hd"><div><h1>Painel de suporte 🌈</h1><p class="sub">Visão geral dos chamados registrados.</p></div><button class="btn2" id="refresh">Atualizar</button></div>${kpis(tickets)}<div class="hd"><h3>🎀 Fila de chamados ativos</h3><a class="lnk" href="#/fila">Ver todos →</a></div>${ticketTable(open.slice(0,5))}<div class="grid2"><div class="card chart"><h3>👯 Chamados ativos por agente</h3>${bars(agents.map(a=>[a.nome,open.filter(t=>t.responsavel===a.email).length]))}</div><div class="card chart"><h3>🎨 Chamados por categoria</h3>${bars(categoryCounts(tickets))}</div></div>`);
 $('#refresh').onclick=route;
}
function vTeam(){
 page('equipe',`<div class="hd"><div><h1>Equipe 👯</h1><p class="sub">Distribuição dos atendimentos por agente.</p></div><button class="btn2" id="refresh">Atualizar</button></div><div class="grid2">${agents.map(a=>`<div class="card box"><h3>${esc(a.nome)}</h3><p class="mu">${esc(a.email)}</p><p>${tickets.filter(t=>t.responsavel===a.email&&t.status!=='Resolvido').length} chamado(s) ativo(s)</p><p>${tickets.filter(t=>t.responsavel===a.email&&t.status==='Resolvido').length} chamado(s) resolvido(s)</p></div>`).join('')||'<p class="empty">Nenhum agente cadastrado.</p>'}</div><p class="mu">Abra um chamado na fila para atribuir ou transferir o atendimento.</p>`);
 $('#refresh').onclick=route;
}
function vReports(){
 page('relatorios',`<div class="hd"><div><h1>Relatórios 📊</h1><p class="sub">Chamados por data de abertura.</p></div><button class="btn2" id="export">Exportar CSV</button></div><div class="filt"><label for="period">Período</label><select class="in" id="period" style="width:auto"><option value="0">Todo o período</option><option value="7">Últimos 7 dias</option><option value="30">Últimos 30 dias</option></select></div><div id="report"></div>`);
 let filtered=[];
 const draw=()=>{const days=+$('#period').value;filtered=tickets.filter(t=>!days||Date.now()-new Date(t.dataCriacao).getTime()<=days*86400000);$('#report').innerHTML=`${kpis(filtered)}<div class="grid2" style="margin-top:24px"><div class="card chart"><h3>Por categoria</h3>${bars(categoryCounts(filtered))}</div><div class="card chart"><h3>Por prioridade</h3>${bars(priorities.map(p=>[p,filtered.filter(t=>t.prioridade===p).length]))}</div></div><p class="mu">${filtered.length} chamado(s) no período selecionado.</p>`};
 $('#period').onchange=draw;
 $('#export').onclick=()=>{
  const cell=value=>{let text=String(value??'');if(/^[=+@\-\t\r\n]/.test(text))text="'"+text;return '"'+text.replace(/"/g,'""')+'"'};
  const rows=[['Número','Assunto','Categoria','Prioridade','Status','Solicitante','Responsável','Abertura','Atualização'],...filtered.map(t=>[t.id,t.titulo,t.categoria,t.prioridade,t.status,t.nomeUsuario,t.nomeResponsavel||'',date(t.dataCriacao),date(t.dataAtualizacao)])];
  const blob=new Blob(['\ufeff'+rows.map(row=>row.map(cell).join(';')).join('\r\n')],{type:'text/csv;charset=utf-8'}),url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download='suportehub-chamados.csv';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
 };draw();
}
