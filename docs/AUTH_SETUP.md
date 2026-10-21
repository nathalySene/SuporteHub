# Login com Google e recuperação de senha

O código oferece login com Google, conexão de uma conta existente ao Google e recuperação de senha com link temporário. A execução local continua usando SQLite e precisa somente do SDK .NET 10. As credenciais OAuth e o serviço de e-mail precisam ser configurados por você; não foram criados automaticamente.

Na raiz do projeto, no PowerShell:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http
```

Use o navegador na mesma máquina, na porta **5218**. Mantenha o terminal aberto. Para aplicar mudanças de configuração, pare com `Ctrl+C` e execute o comando novamente.

## Guardar configurações locais

O projeto declara um `UserSecretsId`, então os comandos `dotnet user-secrets` abaixo guardam valores no perfil do seu usuário, fora do repositório. Não é necessário executar `user-secrets init`.

Substitua os exemplos pelos valores reais **apenas no seu computador**. Não envie o Client Secret ou a senha SMTP pelo chat e não os coloque em arquivos versionados. User Secrets serve para desenvolvimento local; na hospedagem, utilize o mecanismo seguro de configuração da plataforma.

As chaves de configuração são sensíveis a nomes completos: use `Authentication:Google:ClientId` e `Authentication:Google:ClientSecret`, conforme os comandos abaixo.

## Criar o acesso do Google

1. Abra o [Google Cloud Console](https://console.cloud.google.com/) com a conta Google que administrará o projeto.
2. Crie ou selecione um projeto para o SuporteHub.
3. Abra **Google Auth Platform**. Conforme a versão do console, essa configuração também aparece em **APIs e serviços → Tela de permissão OAuth**.
4. Em **Branding**, informe o nome do aplicativo e os e-mails de suporte e contato solicitados pelo console.
5. Em **Audience/Público-alvo**, escolha **Externo** se estiver usando uma conta Gmail pessoal. Em modo de teste, adicione seu próprio e-mail na lista de **Usuários de teste**. Somente os usuários permitidos pelo projeto poderão entrar enquanto ele estiver em teste.
6. Em **Data Access/Acesso a dados**, mantenha apenas os dados básicos necessários ao login: identidade (`openid`), e-mail e perfil. O SuporteHub não precisa de acesso ao Gmail, Drive ou Calendar.
7. Em **Clients/Clientes**, crie um cliente OAuth do tipo **Aplicativo da Web**.
8. Adicione este **URI de redirecionamento autorizado**, exatamente assim:

```text
http://localhost:5218/signin-google
```

O caminho é `/signin-google`, sem `#`, sem `/api` e sem barra adicional ao final. O Google retorna para o middleware da API nesse endereço. Ele não é uma tela que você deve abrir manualmente. Use o perfil `http` e o host `localhost` para testar com esse URI; trocar por `127.0.0.1`, outra porta ou HTTPS exige cadastrar o URI correspondente.

O fluxo é processado pelo servidor. A configuração de **origens JavaScript autorizadas** não substitui o URI de redirecionamento. Se o console pedir uma origem local, use apenas a origem da aplicação na porta 5218.

Depois da criação, copie o **Client ID** e o **Client Secret** e configure na raiz do projeto:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "SEU_CLIENT_ID.apps.googleusercontent.com" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Authentication:Google:ClientSecret" "SEU_CLIENT_SECRET" --project backend/SuporteHub.API/SuporteHub.API.csproj
```

Reinicie a aplicação. O botão **Continuar com Google** fica disponível quando esses dois valores estão configurados. A confirmação do acesso real depende de credenciais válidas e da permissão concedida na tela do Google.

### Entrar e conectar contas

- Se o banco local estiver vazio, crie primeiro a conta de suporte pelo cadastro com senha. Um cadastro social não recebe automaticamente o perfil de suporte.
- Para uma nova conta Google, use **Continuar com Google**; a aplicação cria uma conta de cliente após confirmar o e-mail verificado pelo Google.
- Se você já tem uma conta SuporteHub com esse e-mail, entre primeiro com sua senha. Em **Minha conta**, use **Conectar Google** e escolha a conta Google com o mesmo e-mail.
- A aplicação não une contas automaticamente apenas porque os e-mails são iguais. Após a conexão explícita, o identificador estável da conta Google é usado para os próximos logins.

### Se o Google exibir um erro

| Mensagem | O que conferir |
|---|---|
| `redirect_uri_mismatch` | O cliente deve ser do tipo Web e conter o URI exato acima. Confira host, porta, esquema e caminho. |
| Acesso negado a usuário de teste | Adicione o e-mail em **Audience/Público-alvo → Usuários de teste**, no mesmo projeto OAuth. |
| Botão indisponível | Configure Client ID e Client Secret no projeto indicado e reinicie a aplicação em Development. |
| Já existe uma conta com esse e-mail | Entre com a senha atual e conecte o Google em **Minha conta**. |
| Google ainda não confirma o e-mail | Utilize uma conta com e-mail verificado; a aplicação não aceita uma identidade sem essa confirmação. |

Referência oficial: [OAuth 2.0 para aplicações de servidor Web](https://developers.google.com/identity/protocols/oauth2/web-server).

## Testar “Esqueci minha senha” localmente

O perfil local **Development**, sem configuração SMTP, grava mensagens de teste como arquivos **`.eml`** em:

```text
.local/mail
```

Esse modo **não envia mensagens para sua caixa de entrada**. A tela informa quando a entrega é apenas local. Os arquivos ficam fora do frontend e são ignorados pelo Git; a API não fornece um endpoint para mostrar tokens ou baixar essa caixa de saída.

1. Crie uma conta com e-mail e senha, ou use uma conta já cadastrada.
2. Na tela de login, clique em **Esqueci minha senha** e informe esse e-mail.
3. Aguarde a criação do arquivo `.eml`. A aplicação responde com uma mensagem genérica tanto para e-mails cadastrados quanto para e-mails desconhecidos; isso evita revelar quais contas existem.
4. Abra a mensagem em um aplicativo que leia `.eml`, como Outlook ou Thunderbird, e use o link de redefinição.
5. Escolha uma nova senha com 10 a 128 caracteres. Depois, entre novamente com essa senha.

Para abrir a mensagem mais recente pelo PowerShell, estando na raiz do projeto:

```powershell
$mensagem = Get-ChildItem .\.local\mail\*.eml | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Invoke-Item -LiteralPath $mensagem.FullName
```

Se não houver aplicativo associado a `.eml`, este exemplo lê e decodifica a mensagem **localmente** e abre o link no navegador. Ele é destinado apenas ao teste local:

```powershell
$mensagem = Get-ChildItem .\.local\mail\*.eml | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $mensagem) { throw "Nenhuma mensagem local encontrada. Solicite a recuperação de uma conta cadastrada." }
$conteudo = Get-Content -LiteralPath $mensagem.FullName -Raw
$partes = $conteudo -split "\r?\n\r?\n", 2
$corpo = $partes[1]
if ($partes[0] -match 'Content-Transfer-Encoding:\s*base64') {
    $corpo = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($corpo.Trim()))
} elseif ($partes[0] -match 'Content-Transfer-Encoding:\s*quoted-printable') {
    $corpo = $corpo -replace '=\r?\n', ''
    $corpo = [regex]::Replace($corpo, '=([0-9A-Fa-f]{2})', {
        param($trecho)
        [char][Convert]::ToInt32($trecho.Groups[1].Value, 16)
    })
}
$link = [regex]::Match($corpo, 'https?://[^\s<>]+').Value
if (-not $link) { throw "O link de recuperação não foi encontrado na mensagem." }
Start-Process $link
```

O link expira em **30 minutos**, pode ser usado **uma única vez** e deve ser o mais recente solicitado para aquela conta. Uma redefinição concluída invalida as sessões anteriores. Não compartilhe arquivos `.eml` de recuperação: eles contêm um link capaz de alterar a senha.

## Enviar e-mail de verdade por SMTP

Escolha um provedor de e-mail transacional ou um servidor SMTP que permita envio com **STARTTLS na porta 587**. Obtenha o host, o remetente autorizado e, quando exigidas, as credenciais SMTP. Confirme o remetente/domínio no provedor antes de testar.

Configure os valores localmente com User Secrets:

```powershell
dotnet user-secrets set "Email:DeliveryMode" "Smtp" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:Host" "HOST_SMTP_DO_PROVEDOR" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:Port" "587" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:UserName" "SEU_USUARIO_SMTP" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:Password" "SUA_SENHA_SMTP" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:FromAddress" "REMETENTE_AUTORIZADO" --project backend/SuporteHub.API/SuporteHub.API.csproj
dotnet user-secrets set "Email:Smtp:FromName" "SuporteHub" --project backend/SuporteHub.API/SuporteHub.API.csproj
```

Se o servidor permite relay sem autenticação, omita usuário e senha juntos. Esta implementação requer STARTTLS e a verificação normal do certificado do servidor; a porta 465 com TLS implícito não é suportada.

Pare a aplicação e inicie novamente com **o mesmo comando**:

```powershell
dotnet run --project backend/SuporteHub.API/SuporteHub.API.csproj --launch-profile http
```

O perfil HTTP não força o modo de entrega; o valor `Smtp` dos User Secrets substitui a caixa local de desenvolvimento. Nesse modo, a solicitação passa a usar seu provedor SMTP. O envio real depende de o host aceitar as credenciais e o remetente e de a rede permitir o tráfego SMTP. O teste com arquivos `.eml` não confirma a entrega em uma caixa de entrada real.

Para voltar ao teste por arquivos:

```powershell
dotnet user-secrets remove "Email:DeliveryMode" --project backend/SuporteHub.API/SuporteHub.API.csproj
```

Reinicie a aplicação depois da alteração.

### Opção AWS: Amazon SES

O SES pode ser o provedor SMTP do projeto. Na região São Paulo, o host SMTP é `email-smtp.sa-east-1.amazonaws.com`, com porta 587. Confirme sua identidade de remetente no SES e crie as **credenciais SMTP do SES**; elas não são as mesmas chaves de acesso usadas diretamente pelo SDK DynamoDB.

Se a conta SES ainda estiver no sandbox, os destinatários também precisam estar verificados. Para enviar a destinatários gerais, solicite a saída do sandbox conforme o processo do SES. Nenhum remetente, conta SES ou credencial foi criado automaticamente por esta implementação.

## Publicar em um domínio real

Configure `Authentication:PublicBaseUrl` com a origem HTTPS pública da aplicação. Esse endereço fixo é usado nos links de redefinição, sem depender do cabeçalho Host de uma requisição recebida.

Cadastre no cliente Google o URI do domínio público com o mesmo caminho `/signin-google`. Configure corretamente HTTPS e o encaminhamento confiável de esquema/host caso utilize um proxy reverso. Use o mecanismo de segredos da hospedagem para Client Secret e senha SMTP.

Fora de Development, a ausência de uma configuração de entrega mantém a recuperação por e-mail indisponível; o modo de arquivos locais não é habilitado em produção. A aplicação não deve ser apresentada como pronta para login Google real ou envio SMTP real antes de validar as credenciais e a conectividade desses provedores.
