# Run from Windows PowerShell with .NET 10 and AWS CLI v2 installed.
[CmdletBinding()]
param(
    [string]$AwsProfile = 'suportehub',
    [switch]$CheckOnly,
    [switch]$CreateAdmin,
    [switch]$PromoteAdmin,
    [switch]$CheckAccount
)
$ErrorActionPreference = 'Stop'
if (@($CreateAdmin, $PromoteAdmin, $CheckAccount | Where-Object { $_ }).Count -gt 1) { throw 'Use somente uma acao: -CreateAdmin, -PromoteAdmin ou -CheckAccount.' }
if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw 'Instale a AWS CLI v2 e abra um novo PowerShell. Consulte docs/AWS.md.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale o SDK .NET 10.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:AWS_PROFILE = $AwsProfile
$env:AWS_PAGER = ''
$env:AWS_EC2_METADATA_DISABLED = 'true'
Write-Host "Verificando identidade do perfil $AwsProfile..."
aws sts get-caller-identity --profile $AwsProfile --region sa-east-1 --query Arn --output text
if ($LASTEXITCODE -ne 0) { throw 'Perfil AWS indisponivel ou sessao expirada. Configure o perfil ou renove o login SSO. Consulte docs/AWS.md.' }

# Read-only preflight: never creates a table, modifies schema or copies local data.
$tableJson = aws dynamodb describe-table --profile $AwsProfile --region sa-east-1 --table-name suportehub-dev --query 'Table.{KeySchema:KeySchema,AttributeDefinitions:AttributeDefinitions,TableStatus:TableStatus}' --output json
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel inspecionar suportehub-dev em sa-east-1. Confira a tabela e a permissao DescribeTable.' }
$table = ($tableJson -join "`n") | ConvertFrom-Json
$hash = @($table.KeySchema | Where-Object { $_.KeyType -eq 'HASH' })
$range = @($table.KeySchema | Where-Object { $_.KeyType -eq 'RANGE' })
$pk = @($table.AttributeDefinitions | Where-Object { $_.AttributeName -eq 'PK' -and $_.AttributeType -eq 'S' })
$sk = @($table.AttributeDefinitions | Where-Object { $_.AttributeName -eq 'SK' -and $_.AttributeType -eq 'S' })
if ($hash.Count -ne 1 -or $range.Count -ne 1 -or $hash[0].AttributeName -ne 'PK' -or $range[0].AttributeName -ne 'SK' -or $pk.Count -ne 1 -or $sk.Count -ne 1) {
    throw 'Tabela incompativel: a API requer PK (String, particao) e SK (String, ordenacao). A tabela existente nao foi modificada.'
}
if ($table.TableStatus -notin @('ACTIVE', 'UPDATING')) { throw 'Aguarde a tabela ficar disponivel.' }
Write-Host 'Tabela e identidade verificadas. Nenhum registro foi escrito nesta verificacao.'
if ($CheckOnly) { return }
# A stale server on port 5218 can show a different version or database.
$client = New-Object System.Net.Sockets.TcpClient
$portBusy = $false
try { $task = $client.ConnectAsync('127.0.0.1', 5218); if ($task.Wait(500)) { $portBusy = $client.Connected } }
catch { } finally { $client.Dispose() }
if ($portBusy) { throw 'Ja existe um servidor na porta 5218. Pare a versao anterior com Ctrl+C antes de criar/promover contas ou iniciar esta versao.' }

# Explicit configuration: no dependency on launchSettings.json or a default SQLite profile.
$env:Storage__Provider = 'DynamoDb'
$env:DynamoDb__TableName = 'suportehub-dev'
$env:DynamoDb__Region = 'sa-east-1'
$env:DynamoDb__ServiceUrl = ''
$env:Storage__KeysPath = Join-Path $repoRoot '.local/aws-keys'
$env:Storage__UploadsPath = Join-Path $repoRoot '.local/aws-uploads'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5218'
Write-Host 'SuporteHub 1.3.1 | DynamoDB | suportehub-dev | sa-east-1'
Push-Location $repoRoot
try {
    & dotnet build 'backend/SuporteHub.API/SuporteHub.API.csproj' --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao falhou; nenhuma conta foi criada ou promovida. Pare qualquer API antiga antes de tentar novamente.' }
    $dll = Join-Path $repoRoot 'backend/SuporteHub.API/bin/Debug/net10.0/SuporteHub.API.dll'
    $appArgs = @($dll, '--require-aws=true')
    if ($CreateAdmin) { $appArgs += '--create-admin' }
    if ($PromoteAdmin) { $appArgs += '--promote-admin' }
    if ($CheckAccount) { $appArgs += '--check-account' }
    & dotnet @appArgs
    if ($LASTEXITCODE -ne 0) { throw 'A operacao nao foi concluida. Confira a mensagem acima.' }
} finally { Pop-Location }
