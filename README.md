# SuporteHub 💜

Sistema de chamados com frontend HTML/CSS/JavaScript, API ASP.NET Core (.NET 10) e banco SQLite local. O modo local precisa somente do SDK .NET 10: não exige Docker, WSL, virtualização, credenciais AWS ou instalação separada do SQLite. A opção de usar a tabela DynamoDB existente na AWS continua disponível.

O frontend e a API são servidos na mesma origem. HTML, CSS e JavaScript são incluídos na saída de compilação e publicação em `wwwroot/`, independentemente da pasta usada para iniciar a DLL.

## Começar agora no Windows

Na raiz do projeto atualizado, execute diretamente no PowerShell:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http
```

Esse comando restaura os pacotes necessários, compila o projeto e inicia a aplicação. Mantenha o terminal aberto e aguarde a mensagem de que a aplicação está ouvindo na porta **5218**. No navegador da mesma máquina, use `localhost` na porta `5218`.

Não execute `bash scripts/start.sh` nem `docker compose` para esse modo. Se já baixou uma versão anterior que exigia Docker, obtenha os arquivos atualizados da branch `codex/suportehub-funcional` antes de executar o comando acima.

Abra a tela de cadastro: se o banco local estiver vazio, a primeira conta será a conta de suporte. Escolha seu nome, e-mail e senha. As contas seguintes serão clientes. Saia e crie outra conta para testar o fluxo do cliente e entre novamente com a conta de suporte para atender o chamado.

A criação da primeira conta usa uma transação para permitir apenas uma conta inicial de suporte, inclusive com cadastros simultâneos. Na AWS, o cadastro público continua criando apenas clientes.

## Linux/macOS e ambiente Codex

O mesmo comando `dotnet run` funciona na raiz do projeto. No ambiente Codex preparado, ative primeiro o SDK:

```bash
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.cloud-setup/dotnet-home
export NUGET_PACKAGES=/workspace/.cloud-setup/nuget
```

Também é possível compilar e usar o script de inicialização Bash:

```bash
dotnet build backend/SuporteHub.API/SuporteHub.API.csproj
./scripts/start.sh
```

O script inicia a DLL compilada com SQLite e caminhos absolutos para os dados. Ele não inicia containers. Compile novamente após alterar o backend. O sandbox de comandos do ambiente Codex exige execução aprovada fora do sandbox para os mutexes usados por `dotnet restore/build`; essa limitação não se aplica ao PowerShell do seu computador.

## Persistência e atualização de versões antigas

O modo local usa `Storage__Provider=Sqlite`. Os scripts e perfis de desenvolvimento guardam o banco no arquivo **`.local/suportehub.db`**, os anexos em `.local/uploads` e as chaves de proteção das sessões em `.local/keys`. A API cria o banco e sua estrutura automaticamente. Mantenha esses arquivos para preservar contas, chamados, histórico, anexos e sessões após reiniciar.

Dados de uma versão anterior com DynamoDB Local **não são migrados automaticamente** para SQLite. A mudança não apaga nem modifica os arquivos antigos em `.local/dynamodb` ou as tabelas AWS. Quem já tem dados no banco anterior deve mantê-los e fazer uma migração separada antes de trocar seu banco de trabalho. O `compose.yml` foi preservado para compatibilidade com esse ambiente anterior, mas não participa da inicialização padrão.

O arquivo SQLite, seus arquivos auxiliares e os demais dados locais são ignorados pelo Git. Faça backup de `.local/` com a aplicação parada. Em múltiplas instâncias, anexos e chaves de sessão precisam de armazenamento compartilhado; S3 para os arquivos e proteção das chaves em repouso são etapas de implantação ainda não implementadas.

## Se a página retornar 404

Pare a execução anterior com `Ctrl+C` e use os arquivos atualizados deste projeto. Na raiz, execute:

```powershell
dotnet clean backend/SuporteHub.API/SuporteHub.API.csproj
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http
```

Abra a raiz do servidor na máquina onde o comando está em execução. Acessar essa porta no seu computador não conecta ao servidor deste ambiente remoto. A interface de configuração do Codex não fornece endereço público de preview.

A saída compilada deve conter `backend/SuporteHub.API/bin/Debug/net10.0/wwwroot/index.html`. A página inicial e `/api/health` devem responder 200; `/api/chamados` sem login responde 401, como esperado.

Para gerar uma distribuição com o frontend incluído:

```powershell
dotnet publish backend/SuporteHub.API/SuporteHub.API.csproj -o ./artifacts/app
```

Mantenha `wwwroot/` junto da DLL. Esse comando gera os arquivos da aplicação; ele não hospeda o site na internet.

## Funcionalidades

- Cadastro de clientes, login com senha verificada e sessão em cookie HttpOnly.
- Login com Google e conexão segura de uma conta existente, após configurar o cliente OAuth.
- Recuperação de senha com link temporário de uso único e invalidação das sessões anteriores; entrega local de teste ou SMTP configurado.
- Criação de chamados, busca, filtros, paginação e consulta de detalhes.
- Histórico com respostas do cliente e do agente.
- Upload e download de PNG, JPG e PDF de até 10 MB, até 20 anexos por chamado.
- Atribuição e transferência para agentes, atualização de status e reabertura pelo agente.
- Painel com contagens reais, fila geral, fila pessoal, equipe e relatórios com filtro de período e exportação CSV.
- Autorização na API: clientes acessam apenas seus próprios chamados; agentes têm acesso à operação de suporte. O solicitante vem da sessão, nunca de um e-mail enviado no formulário.

Não existem contas ou senhas padrão. Os testes usam bancos separados e não criam contas no banco local da aplicação.

## Configurar Google e recuperação de senha

O passo a passo para criar o cliente OAuth no Google Cloud e configurar o envio de mensagens está em [docs/AUTH_SETUP.md](docs/AUTH_SETUP.md).

No primeiro uso local, crie a conta de suporte pelo cadastro com senha. O login Google só fica disponível após informar Client ID e Client Secret por configuração local segura. Se já existe uma conta com o mesmo e-mail, entre com a senha e conecte o Google em **Minha conta**.

No perfil Development sem SMTP, **Esqueci minha senha** gera uma mensagem de teste `.eml` em **`.local/mail`**; ela não chega à sua caixa de entrada. Abra a mensagem local para usar o link. Para enviar e-mail de verdade, configure um provedor SMTP com remetente autorizado e STARTTLS na porta 587. A integração não cria contas Google Cloud ou serviços de e-mail automaticamente. O guia explica os comandos PowerShell com `dotnet user-secrets` e a opção de usar Amazon SES.

## Opção: usar a tabela AWS existente

Configuração preservada:

- Tabela: **suportehub-dev**
- Região: **sa-east-1** (São Paulo)
- Stack informada: **suportehub-dev**

Para esse modo, configure explicitamente `Storage__Provider=DynamoDb`. Disponibilize uma identidade AWS válida ao SDK .NET, por exemplo uma role da máquina ou um perfil AWS montado de forma segura. Não coloque chaves no código, no repositório ou no chat. Valores substituídos por um proxy de segredos não equivalem a credenciais brutas para assinar requisições AWS SigV4.

A identidade precisa de `dynamodb:GetItem`, `dynamodb:PutItem`, `dynamodb:Query` e `dynamodb:UpdateItem` no ARN da tabela. Libere o destino HTTPS `dynamodb.sa-east-1.amazonaws.com` na rede. A inicialização não cria, limpa nem modifica o esquema de tabelas AWS; faz uma leitura para confirmar acesso.

Com o projeto compilado, no PowerShell:

```powershell
$env:Storage__Provider = "DynamoDb"
$env:DynamoDb__TableName = "suportehub-dev"
$env:DynamoDb__Region = "sa-east-1"
Remove-Item Env:DynamoDb__ServiceUrl -ErrorAction SilentlyContinue
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5218"
$env:Storage__KeysPath = Join-Path $PWD ".local/aws-keys"
$env:Storage__UploadsPath = Join-Path $PWD ".local/aws-uploads"
dotnet backend/SuporteHub.API/bin/Debug/net10.0/SuporteHub.API.dll
```

Em Bash:

```bash
./scripts/run-aws.sh
```

O script AWS usa diretórios separados para anexos e sessões e não troca silenciosamente para o banco local. Use HTTPS no acesso público, com o terminador TLS configurado para a implantação. Não sirva `frontend/` separadamente: a API precisa estar na mesma origem.

**Validação AWS pendente:** a tentativa anterior de leitura encontrou ausência de identidade/perfil AWS utilizável. O endpoint também foi bloqueado pela política de rede, e sua liberação foi salva no rascunho do ambiente. A conexão com a tabela real depende dessas duas configurações; nenhuma delas é necessária para o modo SQLite.

### Contrato dos registros DynamoDB

A tabela deve ter chaves string `PK` e `SK`, como no template `infra/dynamodb.yml`:

| PK | SK | Conteúdo |
|---|---|---|
| `USERS` | e-mail normalizado | Conta, nome, perfil e hash de senha |
| `TICKETS` | `TICKET#0000000001` | Chamado, histórico e metadados dos anexos |
| `META` | `TICKET_COUNTER` | Contador atômico `NextId` |

As entidades são armazenadas em `Data` (JSON) e `Version` (número), com condição de versão nas atualizações. O contador usa `UpdateItem` atômico. A aplicação não apaga nem migra registros existentes. A estrutura atual da tabela real ainda não pôde ser inspecionada. Os GSIs do template não são usados nesta versão; a API consulta as partições `USERS` e `TICKETS` e filtra no servidor.

## Criar outros agentes

No SQLite vazio, o primeiro cadastro pela interface cria a conta de suporte. Para agentes adicionais ou para o primeiro agente na AWS, use o comando administrativo com as mesmas configurações de banco usadas pela API:

```powershell
dotnet backend/SuporteHub.API/bin/Debug/net10.0/SuporteHub.API.dll --create-agent
```

Antes do comando, configure o provedor e os caminhos apropriados. Para SQLite, na raiz do repositório:

```powershell
$env:Storage__Provider = "Sqlite"
$env:Storage__DatabasePath = Join-Path $PWD ".local/suportehub.db"
```

Para AWS, use as variáveis e a identidade da seção anterior. O comando pede e-mail, nome e senha de 10 a 128 caracteres, sem mostrar a senha no terminal nem passá-la em argumentos. Ele não substitui uma conta existente.

## API

| Método | Rota | Uso |
|---|---|---|
| GET | `/api/health` | Leitura de prontidão do banco configurado |
| GET | `/api/auth/setup` | Informa se o modo local aguarda a primeira conta de suporte |
| POST | `/api/auth/cadastro` | Cadastro de cliente, ou primeira conta de suporte no banco local vazio |
| GET | `/api/auth/providers` | Disponibilidade das integrações de autenticação |
| POST | `/api/auth/login` | Login |
| GET | `/api/auth/google/start` | Iniciar login Google |
| POST | `/api/auth/google/link` | Preparar conexão Google da conta autenticada |
| POST | `/api/auth/forgot-password` | Solicitar recuperação sem revelar existência de conta |
| POST | `/api/auth/reset-password` | Consumir link temporário e alterar senha |
| GET | `/api/auth/me` | Conta autenticada |
| POST | `/api/auth/logout` | Encerrar sessão |
| GET, POST | `/api/chamados` | Listar e criar |
| GET | `/api/chamados/{id}` | Detalhes autorizados |
| POST | `/api/chamados/{id}/respostas` | Adicionar resposta |
| PATCH | `/api/chamados/{id}/status` | Alterar status; cliente pode resolver seu chamado |
| PATCH | `/api/chamados/{id}/responsavel` | Atribuir agente (apenas agentes) |
| POST | `/api/chamados/{id}/anexos` | Upload multipart, campo `arquivo` |
| GET | `/api/chamados/{id}/anexos/{fileId}` | Download autorizado |
| GET | `/api/equipe` | Agentes cadastrados (apenas agentes) |

Requisições que modificam dados exigem `X-Requested-With: SuporteHub`. Não há CORS para outras origens. O cookie é HttpOnly e SameSite Strict; é Secure quando a conexão usa HTTPS. O login possui limitação de tentativas. Senhas são armazenadas com o `PasswordHasher` do ASP.NET Core; senhas e tokens não ficam em web storage. O OpenAPI fica em `/openapi/v1.json` no ambiente Development.

## Testes reproduzíveis

Com o código compilado, sem iniciar Docker nem outro serviço de banco:

```bash
python3 tests/integration.py
node tests/browser.cjs
python3 tests/password_reset.py
dotnet run --project tests/GoogleAuth.Tests/GoogleAuth.Tests.csproj
```

Os testes criam arquivos SQLite temporários e contas de teste, sem usar o banco da aplicação nem a tabela AWS. O teste Python usa somente sua biblioteca padrão; o teste de navegador exige Playwright e Chromium (`CHROMIUM_BIN` permite indicar outro executável). Ambos aceitam `DOTNET_BIN` para localizar o SDK.

A cobertura inclui inicialização da conta de suporte sem duplicidade, autenticação e autorização, isolamento entre clientes, respostas, arquivos, atribuição, status, IDs concorrentes e persistência após reinício. O navegador também valida busca, recuperação de erro, fila pessoal, equipe, relatórios e escape de HTML.

A recuperação de senha possui oito testes de API e navegador (Python Playwright), incluindo expiração, uso único e encerramento de sessões anteriores. Os nove testes Google exercitam o middleware OAuth com um provedor simulado, incluindo state, correlação, PKCE e conexão explícita de contas; não usam credenciais nem confirmam acesso ao Google real.

Os pacotes `Microsoft.Data.Sqlite` e `Microsoft.AspNetCore.OpenApi` foram atualizados para 10.0.13. A compilação validada terminou sem avisos, e a auditoria NuGet não encontrou vulnerabilidades conhecidas nas dependências consultadas. Para verificar novamente, execute `dotnet list backend/SuporteHub.API/SuporteHub.API.csproj package --vulnerable --include-transitive`.
