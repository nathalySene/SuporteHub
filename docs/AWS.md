# Conectar o SuporteHub à AWS

Este modo executa a página/API no seu computador e armazena contas e chamados no DynamoDB da AWS. Não publica automaticamente um site na internet. A tabela usada é **suportehub-dev**, na região **sa-east-1 (São Paulo)**, stack informada **suportehub-dev**.

O projeto não cria, apaga, redefine ou migra essa tabela. Contas e chamados do SQLite não são copiados automaticamente. Preserve `.local` para continuar tendo acesso aos dados locais. No modo AWS, anexos e chaves de sessão ainda ficam em `.local/aws-uploads` e `.local/aws-keys` no computador que executa a API; não são enviados ao S3.

## 1. Conferir a tabela no console

No console AWS, selecione São Paulo e abra **DynamoDB → Tabelas → suportehub-dev**. Em **Visão geral**, confirme:

- Chave de partição: `PK`, tipo **String**.
- Chave de classificação/ordenação: `SK`, tipo **String**.
- Tabela disponível (`ACTIVE`).

Copie o **ARN da tabela** para configurar a política de acesso. Se as chaves forem diferentes, não apague a tabela nem tente recriá-la: será preciso adaptar o armazenamento após conhecer seu esquema.

## 2. Instalar a AWS CLI v2

Baixe o instalador oficial para Windows:

https://awscli.amazonaws.com/AWSCLIV2.msi

Após instalar, abra um **novo PowerShell** e confira:

```powershell
aws --version
```

Entrar no console pelo navegador não fornece credenciais automaticamente à API.

## 3. Preparar a identidade

### Se sua organização usa IAM Identity Center (SSO)

Peça ao administrador o endereço inicial/issuer e a região do Identity Center. A região do SSO pode ser diferente da região da tabela. O permission set deve permitir as ações de DynamoDB listadas abaixo.

```powershell
aws configure sso --profile suportehub
aws sso login --profile suportehub
```

Siga a autenticação pelo navegador. O perfil deve usar a conta AWS que contém a tabela. Renove `aws sso login --profile suportehub` quando a sessão expirar.

### Se você usa somente o console e ainda não tem SSO

No console, abra **IAM → Usuários** e crie uma identidade dedicada, por exemplo `suportehub-local`, sem conceder `AdministratorAccess`. Você precisa ter autorização para gerenciar IAM; se não tiver, peça essa etapa ao administrador da conta AWS.

Anexe uma política de permissões como esta, substituindo **o ARN de exemplo pelo ARN real da sua tabela**:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:DescribeTable",
        "dynamodb:GetItem",
        "dynamodb:PutItem",
        "dynamodb:Query",
        "dynamodb:UpdateItem"
      ],
      "Resource": "arn:aws:dynamodb:sa-east-1:SEU_ID_DA_CONTA:table/suportehub-dev"
    }
  ]
}
```

No usuário IAM dedicado, abra **Credenciais de segurança → Criar chave de acesso** e selecione o caso de uso de aplicação/código local apropriado. Não use chaves da conta root. Guarde os valores somente no seu computador e não os envie pelo chat ou coloque em arquivos do projeto.

No PowerShell, execute:

```powershell
aws configure --profile suportehub
```

Informe a Access Key ID e a Secret Access Key nos prompts locais. Use `sa-east-1` como região padrão e `json` como formato. A AWS CLI guarda esse perfil no diretório `.aws` do seu usuário, fora do projeto. Credenciais desse perfil também serão usadas pelo SDK da API. Prefira SSO com credenciais temporárias quando estiver disponível.

## 4. Testar a conexão sem gravar registros

Na raiz do projeto, que contém `backend`, `frontend` e `scripts`, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-aws.ps1 -CheckOnly
```

O parâmetro de execução vale apenas para esse processo PowerShell e não altera permanentemente a política do Windows. O script consulta a identidade e o esquema da tabela; não cria contas ou chamados nessa etapa.

Se seu perfil tem outro nome, acrescente `-AwsProfile NOME_DO_PERFIL` em todos os comandos do script.

## 5. Criar o administrador no banco AWS

Uma conta local SQLite não aparece automaticamente na AWS. Para criar um administrador novo na tabela AWS:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-aws.ps1 -CreateAdmin
```

Informe e-mail, nome e senha no terminal. Esse passo grava uma conta na tabela existente; não cria recursos AWS. Se a conta já existir na tabela e precisar de administração, use `-PromoteAdmin` em vez de `-CreateAdmin`. Isso preserva a senha e os chamados, mas concede administração a essa conta.

O cadastro público no banco AWS cria sempre **Usuário**, inclusive quando a tabela está vazia.

## 6. Iniciar com a AWS

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-aws.ps1
```

Abra **http://localhost:5218** e entre com a conta criada no banco AWS. Confira também **http://localhost:5218/api/health**: o campo `database` deve mostrar **DynamoDB**. Mantenha o terminal aberto; encerre com `Ctrl+C`.

Se já verificou o perfil e as chaves da tabela, pode iniciar diretamente:

```powershell
$env:AWS_PROFILE = "suportehub"
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile aws
```

O perfil `aws` força DynamoDB, região São Paulo e a tabela indicada, sem utilizar DynamoDB Local ou selecionar SQLite silenciosamente. O script faz a verificação de esquema antes desse comando.

## Erros comuns

| Erro | Ação |
|---|---|
| `aws` não reconhecido | Instale a AWS CLI v2 e abra outro PowerShell. |
| Perfil não encontrado/credenciais ausentes | Configure `suportehub` ou passe `-AwsProfile`. |
| Token expirado | Renove o login SSO ou a credencial temporária do perfil. |
| `AccessDeniedException` | Confira a identidade, o ARN real e as ações da política IAM. |
| `ResourceNotFoundException` | Confira conta AWS, região São Paulo e nome da tabela. |
| Chaves incompatíveis | Não recrie a tabela; confirme o esquema antes de adaptar o código. |
| E-mail/senha não funcionam | Use uma conta cadastrada na AWS, não apenas no SQLite. |
| Porta ocupada | Pare a versão local anterior com `Ctrl+C`. |

Para voltar ao banco local, pare a execução AWS e use `--launch-profile http`. Os bancos permanecem separados. Ações da aplicação no modo AWS gravam dados na tabela e podem gerar uso/cobrança normal do DynamoDB; nenhuma infraestrutura paga é criada pelo script.

A validação da integração real depende de uma identidade válida com essas permissões e da conectividade. Não considere o banco AWS conectado até o teste e `/api/health` confirmarem DynamoDB no seu computador.

## Administrador entra como usuário: verificação na versão 1.3.1

Use os arquivos atualizados da mesma pasta para criar/promover a conta e iniciar a aplicação. Pare o servidor anterior com `Ctrl+C` antes de executar comandos administrativos. O script agora recusa a execução quando há um servidor na porta 5218, compila o projeto e executa a DLL com configuração AWS explícita. Não depende de um perfil de inicialização ausente ou de configurações herdadas de SQLite.

Para corrigir uma conta que já existe, preservando sua senha:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-aws.ps1 -PromoteAdmin
```

Informe exatamente o e-mail que será usado no login. O comando faz uma leitura consistente depois da gravação e deve mostrar **Conta confirmada no banco** e **Perfil: admin**, além de DynamoDB, tabela e região. Se essa confirmação não aparecer, a promoção não foi confirmada. Não faça outro cadastro público depois: entre com a conta promovida.

Para consultar somente o perfil de uma conta, com a API parada:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-aws.ps1 -CheckAccount
```

O comando solicita o e-mail e exibe somente sua identificação e o perfil salvo, sem hash ou senha. Para um e-mail novo, `-CreateAdmin` também confirma o perfil depois de gravar. Se o e-mail já existe, o comando orienta usar `-PromoteAdmin`, sem sobrescrever a senha.

Inicie usando o mesmo script, sem opções. Em **http://localhost:5218/api/health**, confirme:

```json
{
  "status": "ok",
  "database": "DynamoDB",
  "table": "suportehub-dev",
  "region": "sa-east-1",
  "version": "1.3.1"
}
```

Se a resposta não tiver a versão 1.3.1, você ainda está acessando outro servidor ou uma compilação anterior. Use a mesma origem `http://localhost:5218` em todas as telas; `127.0.0.1` e `localhost` possuem cookies separados.

Abra a página inicial com `Ctrl+F5`. O login mostra **Banco AWS**. Entre com e-mail e senha da conta confirmada; o frontend consulta `/api/auth/me` para obter o perfil da sessão antes de direcionar. Depois do login, esse endpoint deve devolver `perfil: admin`. Antes do login, `401` e uma resposta vazia nesse endereço são esperados.

A versão atual força a cópia dos arquivos de frontend na compilação, usa URLs de assets com versão e evita cache de páginas/scripts para não reaproveitar uma interface antiga que classifique `admin` como cliente. Nenhuma senha, conta ou chamado existente é apagado por essa atualização.

### Testes específicos

`tests/dynamodb_admin.py` valida criação, confirmação, duplicidade, promoção, sessão, reinício e login real no navegador contra DynamoDB Local efêmero em loopback. Ele não aceita endpoints AWS e não modifica tabelas reais. Defina `DYNAMODB_TEST_ENDPOINT` para uma instância de teste descartável.

`tests/aws_launcher.py` executa o script em PowerShell real com comandos `aws`/`dotnet` simulados, verificando a escolha do banco, argumentos administrativos, falha de compilação e proteção da porta. Não usa credenciais AWS. É possível indicar o executável PowerShell por `PWSH_BIN`. Esses testes não substituem a confirmação da identidade e do perfil na tabela AWS do seu ambiente.
