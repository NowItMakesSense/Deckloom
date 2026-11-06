<#
.SYNOPSIS
  Cria o banco local do Deckloom (schemas, tabelas, GRANTs, dados iniciais e historico do EF) a partir das migrations.

.DESCRIPTION
  1. Verifica o arquivo .env (so a existencia; o conteudo nao e lido) e restaura o dotnet-ef fixado em backend/dotnet-tools.json.
  2. Gera, com dotnet ef, o script idempotente de cada contexto. A ordem importa: deck_suggestion antes de accounts,
     porque player_preferred_format tem FK para deck_suggestion.format.
  3. Sobe o servico db (e o mail) pelo docker compose e espera o Postgres responder.
  4. Confere se os papeis do ADR-0015 existem. Eles so sao criados pelo db/init/01-roles.sh, na primeira inicializacao do volume.
  5. Aplica o SQL no banco "deckloom" como deckloom_migrator (dono dos objetos: deckloom_owner, via ALTER ROLE ... SET role).
     A senha e lida dentro do contêiner db; o script nao a le nem a imprime.

  Nao sobe a API e nao gera nem aplica nada alem do SQL de criacao. Roda uma vez, em banco vazio: se os schemas
  ja existirem, o script para sem alterar nada.

.PARAMETER SomenteGerar
  Gera o SQL em %TEMP%\deckloom\criar-banco.sql e para, sem subir contêineres nem tocar no banco.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\db\criar-banco-local.ps1

.NOTES
  Requer: .NET SDK 10, Docker com Docker Compose e o arquivo .env preenchido (ver README, "Executar localmente").
#>
[CmdletBinding()]
param(
    [switch]$SomenteGerar
)

$ErrorActionPreference = 'Stop'
$utf8SemBom = New-Object System.Text.UTF8Encoding $false
$OutputEncoding = $utf8SemBom

$raiz = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $raiz

$backend = Join-Path $raiz 'backend'
$projetos = @(
    @{ Contexto = 'FormatCatalogDbContext'; Projeto = 'backend/src/Modules/DeckSuggestion/Deckloom.DeckSuggestion.Infrastructure/Deckloom.DeckSuggestion.Infrastructure.csproj' },
    @{ Contexto = 'AccountsDbContext';      Projeto = 'backend/src/Modules/Accounts/Deckloom.Accounts.Infrastructure/Deckloom.Accounts.Infrastructure.csproj' }
)

$pastaTemp = Join-Path $env:TEMP 'deckloom'
New-Item -ItemType Directory -Force -Path $pastaTemp | Out-Null
$sqlFinal = Join-Path $pastaTemp 'criar-banco.sql'

Write-Host '==> Restaurando dotnet-ef (backend/dotnet-tools.json)'
Push-Location $backend
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore falhou.' }

    $partes = @()
    foreach ($item in $projetos) {
        $saida = Join-Path $pastaTemp ($item.Contexto + '.sql')
        Write-Host "==> Gerando script completo (todas as migrations): $($item.Contexto)"
        $caminhoProjeto = Join-Path $raiz $item.Projeto
        dotnet ef migrations script --context $item.Contexto --project $caminhoProjeto --startup-project $caminhoProjeto --output $saida
        if ($LASTEXITCODE -ne 0) { throw "dotnet ef falhou para $($item.Contexto). Veja a mensagem acima." }
        $partes += $saida
    }
}
finally {
    Pop-Location
}

# O SQL do EF traz START TRANSACTION; e COMMIT; por migration. Eles sao retirados e o arquivo inteiro vira uma unica
# transacao: se algo falhar, nada fica gravado pela metade.
$corpo = New-Object System.Text.StringBuilder
foreach ($parte in $partes) {
    $texto = [System.IO.File]::ReadAllText($parte, $utf8SemBom)
    [void]$corpo.Append(([regex]::Replace($texto, '(?m)^(START TRANSACTION|COMMIT);\r?\n', '')))
}

$conteudo = New-Object System.Text.StringBuilder
[void]$conteudo.AppendLine("-- Criacao do banco Deckloom. Gerado por scripts/db/criar-banco-local.ps1 em $(Get-Date -Format s).")
[void]$conteudo.AppendLine('-- Ordem: deck_suggestion antes de accounts (FK de player_preferred_format). Uma transacao. So para banco vazio.')
[void]$conteudo.AppendLine('BEGIN;')
[void]$conteudo.Append($corpo.ToString())
[void]$conteudo.AppendLine('COMMIT;')
[System.IO.File]::WriteAllText($sqlFinal, $conteudo.ToString(), $utf8SemBom)
Write-Host "==> SQL gerado: $sqlFinal"

if ($SomenteGerar) {
    Write-Host 'Parando aqui (-SomenteGerar). Nada foi aplicado.'
    return
}

if (-not (Test-Path '.env')) {
    throw 'Arquivo .env nao encontrado na raiz. Crie-o a partir do .env.example e preencha os valores (README, "Executar localmente com Docker Compose").'
}

Write-Host '==> Subindo db e mail'
docker compose up -d db mail
if ($LASTEXITCODE -ne 0) { throw 'docker compose up falhou.' }

Write-Host '==> Esperando o Postgres responder'
$pronto = $false
for ($i = 0; $i -lt 30 -and -not $pronto; $i++) {
    docker compose exec -T db pg_isready -U postgres -d deckloom | Out-Null
    if ($LASTEXITCODE -eq 0) { $pronto = $true } else { Start-Sleep -Seconds 2 }
}
if (-not $pronto) { throw 'Postgres nao respondeu em 60 s. Veja: docker compose logs db' }

Write-Host '==> Conferindo os papeis do ADR-0015'
$papeis = (docker compose exec -T db psql -U postgres -d deckloom -tA -c "SELECT count(*) FROM pg_roles WHERE rolname IN ('deckloom_owner','deckloom_migrator','deckloom_app')").Trim()
if ($papeis -ne '3') {
    throw 'Papeis do banco ausentes. O db/init/01-roles.sh so roda com o volume db-data vazio. Se o volume ja existe e nao tem dados que voce queira, apague-o com "docker compose down -v" (destrutivo) e rode este script de novo.'
}

Write-Host '==> Conferindo se o banco ainda esta vazio'
$schemasExistentes = (docker compose exec -T db psql -U postgres -d deckloom -tA -c "SELECT count(*) FROM pg_namespace WHERE nspname IN ('accounts','deck_suggestion')").Trim()
if ($schemasExistentes -ne '0') {
    throw 'Os schemas accounts/deck_suggestion ja existem. Este script so cria o banco do zero. Para recriar, apague o volume com "docker compose down -v" (destrutivo) e rode de novo.'
}

Write-Host '==> Aplicando o SQL como deckloom_migrator'
Get-Content -Raw -Encoding UTF8 $sqlFinal |
    docker compose exec -T db sh -c 'PGPASSWORD="$DECKLOOM_MIGRATOR_DB_PASSWORD" psql -U deckloom_migrator -d deckloom -v ON_ERROR_STOP=1 -q -f -'
if ($LASTEXITCODE -ne 0) { throw 'A aplicacao do SQL falhou. Como o script e uma unica transacao, nada foi gravado. Corrija a causa (veja a mensagem acima) e rode de novo.' }

Write-Host '==> Verificando o resultado (nomes de tabelas, sem dados)'
docker compose exec -T db psql -U postgres -d deckloom -tA -c "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_schema IN ('accounts','deck_suggestion') ORDER BY 1"

Write-Host 'Banco criado. Proximo passo: docker compose up -d --build  (depois abra http://localhost:8081)'
