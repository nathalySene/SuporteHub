# Administrador e Usuário

O sistema tem somente dois perfis e uma única tela de login:

| Perfil | Acesso |
|---|---|
| Administrador | Todos os chamados, atribuição, prioridade, status, notas internas, relatórios e cadastro de contas |
| Usuário | Abrir chamados, acompanhar os próprios chamados, enviar respostas e anexos e marcar o próprio chamado como resolvido |

O perfil é identificado pelo servidor após informar e-mail e senha. Não existe seleção de perfil no login. O cadastro público nunca permite escolher administração.

## Começar

Na raiz do projeto, no PowerShell:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http
```

Abra `http://localhost:5218`. No banco SQLite local Development vazio, a primeira conta cadastrada será **Administrador**. Os próximos cadastros serão **Usuário**. Na AWS ou fora de Development, o cadastro público cria somente usuários; o primeiro administrador deve ser criado por um operador com acesso ao servidor.

O administrador entra em **Visão geral** e tem acesso a Chamados, Atendidos, Relatórios e Usuários. O usuário entra em **Meus chamados** e tem acesso a Novo chamado.

## Contas de versões antigas

As contas antes chamadas de suporte/agente são reconhecidas como **Administrador**. As contas antes chamadas de cliente são reconhecidas como **Usuário**. Senhas, e-mails, nomes, chamados, histórico e anexos são preservados.

Essa compatibilidade é aplicada ao ler a conta, sem reescrever o banco inteiro ou migrar uma tabela AWS na inicialização. Quando uma conta é gravada novamente, seu perfil é salvo no formato atual (`admin` ou `user`). Sessões antigas com nomes de perfil anteriores precisam fazer login novamente.

Com a aplicação parada, preserve a pasta `.local` ao atualizar. Não substitua seu banco por um banco vazio.

## Cadastrar contas

Na área do administrador, abra **Usuários**. É possível buscar por nome ou e-mail, filtrar por perfil e usar **Adicionar conta**. Informe nome, e-mail, senha inicial (10–128 caracteres) e o perfil:

- **Usuário** é a opção padrão, para quem precisa abrir e acompanhar chamados.
- **Administrador** concede acesso a todos os chamados e ao cadastro de outras contas.

Não existem senhas padrão. Contas cadastradas nessa tela usam o mesmo login. E-mails duplicados são recusados. Os hashes das senhas não são exibidos nem retornados pela API.

## Tornar uma conta existente administradora

Pare a aplicação com `Ctrl+C` e execute, usando o mesmo banco configurado para a aplicação:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http -- --promote-admin
```

Informe o e-mail já cadastrado. A senha e os chamados são mantidos. Inicie a aplicação e entre novamente.

Para criar o primeiro administrador de um ambiente sem cadastro inicial local:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http -- --create-admin
```

Informe e-mail, nome e senha no terminal. A senha fica oculta na entrada interativa. Esses comandos exigem acesso ao servidor e não são rotas públicas.

## Chamados e relatórios

Chamados permite busca, filtros, paginação, exportação CSV e abertura em nome de um usuário cadastrado. O administrador pode salvar responsável, prioridade e status juntos; apenas administradores podem receber a atribuição. O histórico registra as alterações. Notas internas são visíveis somente aos administradores e ficam ocultas nas respostas da API para usuários.

Atendidos mostra a pessoa que resolveu, a data e o tempo da última resolução. Reabrir limpa esses dados; resolver novamente registra a nova conclusão. Não há avaliação, estrelas ou indicadores de satisfação.

Relatórios usa a data de abertura como filtro. A taxa de resolução é a quantidade atualmente resolvida dividida pelo total aberto no período. A primeira resposta considera somente mensagens públicas da administração. O gráfico diário usa os mesmos chamados selecionados. O ranking conta quem efetivamente resolveu; resoluções pelo próprio usuário não são atribuídas a um administrador.

Registros antigos sem datas/autoria mostram `—` e não entram nas médias de tempo. Atendidos sem data de resolução aparecem em **Todo o período**. A data da última atualização não é usada como substituta da resolução.

**Exportar PDF** abre a impressão do navegador; selecione **Salvar como PDF**. O CSV usa os resultados filtrados e protege células que poderiam ser interpretadas como fórmulas.

## Verificação

```bash
dotnet build backend/SuporteHub.API/SuporteHub.API.csproj
python3 tests/integration.py
node tests/browser.cjs
python3 tests/admin.py
```

Os testes usam SQLite, contas e arquivos temporários separados dos dados reais. Playwright e Chromium são necessários para os testes de navegador. `DOTNET_BIN` e `CHROMIUM_BIN` permitem indicar os executáveis. Não foi necessário criar serviços Google/e-mail, instalar Docker ou modificar a tabela AWS existente.
