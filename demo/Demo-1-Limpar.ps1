#Requires -Version 5.1
<#
    Demo-1-Limpar.ps1  --  GpsEquipe v2
    Limpa a base ANTES da gravacao. Dois estagios: pre-visualizacao por padrao,
    execucao real somente com -Executar.

    Uso:
      .\Demo-1-Limpar.ps1                      # so mostra o que seria apagado
      .\Demo-1-Limpar.ps1 -Executar            # apaga o escopo Hoje (recomendado)
      .\Demo-1-Limpar.ps1 -Escopo Tudo         # pre-visualiza faxina total
      .\Demo-1-Limpar.ps1 -Escopo Tudo -Executar
      .\Demo-1-Limpar.ps1 -Executar -Reiniciar # apaga e reinicia a Function App

    Escopo Hoje  = particao UTC de hoje + residuos de teste (RowKey demo*/teste*,
                   Celular abc*). PRESERVA o historico real dos dias anteriores,
                   que e o que a cena do filtro por periodo precisa mostrar.
    Escopo Tudo  = tudo em Coordenadas. Video mais limpo, cena do filtro mais pobre.

    Nunca toca FuncionariosPermitidos: cadastro e segredo do Authenticator
    sobrevivem. O PIN foi extirpado no Incremento 8B.
    Nunca enumera resource groups. Toda exclusao e nomeada por PartitionKey/RowKey.
    Somente texto ASCII: PowerShell 5.1 le arquivo sem BOM como ANSI.
#>
[CmdletBinding()]
param(
    [switch]$Executar,
    [ValidateSet('Hoje','Tudo')]
    [string]$Escopo = 'Hoje',
    [switch]$Reiniciar
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- nomes vivos (Anotacoes-v2.txt, Secao 2) ----
$RG      = 'GpsEquipe-RG'
$STO     = 'gpsequipebad1'
$APP     = 'GpsEquipe-App-bad1'
$TB      = 'Coordenadas'
$TENANT  = '38ae2f02-5710-4e12-80bb-83600c3fdf1e'
$USUARIO = 'alexandre.calzetta@cs.unicid.edu.br'

function Escrever($t)  { Write-Host $t }
function Tabela($obj)  { ($obj | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Host }

function Test-Guarda {
    $j = az account show -o json 2>$null
    if (-not $j) {
        Escrever 'ABORTADO: az account show nao respondeu.'
        Escrever "Rode: az login --tenant $TENANT"
        return $false
    }
    $c = $j | ConvertFrom-Json
    Escrever ('conta   : ' + $c.name)
    Escrever ('tenant  : ' + $c.tenantId)
    Escrever ('usuario : ' + $c.user.name)
    if ($c.tenantId -ne $TENANT -or $c.user.name -ne $USUARIO) {
        Escrever 'ABORTADO: tenant ou usuario fora do esperado. NADA foi tocado.'
        Escrever "Corrija com: az logout ; az login --tenant $TENANT"
        return $false
    }
    Escrever 'GUARDA OK'
    return $true
}

function Get-Coordenadas {
    $j = az storage entity query --account-name $STO --table-name $TB --auth-mode key -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $j) {
        Escrever 'FALHA ao consultar a tabela. Verifique login e nome da conta.'
        return $null
    }
    return @(($j | ConvertFrom-Json).items)
}

# ------------------------------------------------------------------ inicio
Escrever '=================================================='
Escrever ' Demo-1-Limpar :: GpsEquipe v2'
$modo = if ($Executar) { 'EXECUCAO REAL' } else { 'PRE-VISUALIZACAO (nada sera apagado)' }
Escrever (' modo   : ' + $modo)
Escrever (' escopo : ' + $Escopo)
Escrever '=================================================='
Escrever ''

if (-not (Test-Guarda)) { return }

$hoje  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
$todos = Get-Coordenadas
if ($null -eq $todos) { return }

Escrever ''
Escrever ('--- 1. ESTADO ATUAL DE ' + $TB + ' (particao UTC de hoje: ' + $hoje + ') ---')
Escrever ('total de linhas: ' + $todos.Count)
Tabela ($todos | Group-Object PartitionKey |
        Select-Object @{n='particao';e={$_.Name}}, Count |
        Sort-Object particao)

if ($todos.Count -ge 1000) {
    Escrever 'AVISO: 1000 ou mais linhas. A consulta pode estar paginada; rode o script de novo apos executar.'
}

# ---- selecao dos alvos ----
if ($Escopo -eq 'Tudo') {
    $alvos = $todos
} else {
    $alvos = @($todos | Where-Object {
        $_.PartitionKey -eq $hoje -or
        $_.RowKey -match '^(demo|teste)' -or
        ($_.Celular -is [string] -and $_.Celular -like 'abc*')
    })
}

Escrever ''
Escrever '--- 2. ALVOS DESTA LIMPEZA ---'
Escrever ('linhas marcadas para exclusao: ' + $alvos.Count)
if ($alvos.Count -gt 0) {
    Tabela ($alvos | Select-Object PartitionKey, RowKey, Celular |
            Sort-Object PartitionKey, RowKey)
}

$preservados = $todos.Count - $alvos.Count
Escrever ('linhas preservadas: ' + $preservados)

if ($alvos.Count -eq 0) {
    Escrever ''
    Escrever 'NADA A FAZER: a base ja esta limpa para o escopo escolhido.'
}

# ---- estagio 1: apenas previa ----
if (-not $Executar) {
    Escrever ''
    Escrever '--- 3. ESTA FOI A PRE-VISUALIZACAO ---'
    Escrever 'Confira a lista acima. Para apagar de verdade, repita com -Executar:'
    Escrever ('  .\Demo-1-Limpar.ps1 -Escopo ' + $Escopo + ' -Executar')
    return
}

# ---- estagio 2: execucao real ----
Escrever ''
Escrever '--- 3. APAGANDO (exclusao nomeada, uma a uma) ---'
$ok = 0; $falha = 0
foreach ($a in $alvos) {
    az storage entity delete --account-name $STO --table-name $TB `
        --partition-key $a.PartitionKey --row-key $a.RowKey --auth-mode key -o none 2>$null
    if ($LASTEXITCODE -eq 0) {
        $ok++
    } else {
        $falha++
        Escrever ('  FALHA -> ' + $a.PartitionKey + ' / ' + $a.RowKey)
    }
}
Escrever ('apagadas: ' + $ok + ' | falhas: ' + $falha)

Escrever ''
Escrever '--- 4. ESTADO DEPOIS ---'
$depois = Get-Coordenadas
if ($null -ne $depois) {
    Escrever ('total de linhas: ' + $depois.Count)
    if ($depois.Count -gt 0) {
        Tabela ($depois | Group-Object PartitionKey |
                Select-Object @{n='particao';e={$_.Name}}, Count |
                Sort-Object particao)
    }
    $sobrouHoje = @($depois | Where-Object { $_.PartitionKey -eq $hoje }).Count
    Escrever ('linhas na particao de hoje: ' + $sobrouHoje + ' (esperado 0)')
}

# ---- opcional: reiniciar a aplicacao ----
if ($Reiniciar) {
    Escrever ''
    Escrever '--- 5. REINICIANDO A FUNCTION APP ---'
    Escrever 'AVISO: reinicio gera cold start. O Demo-2-Preparar.ps1 faz o aquecimento depois.'
    az functionapp restart --name $APP --resource-group $RG -o none 2>$null
    Escrever ('exit code do restart: ' + $LASTEXITCODE)
    Start-Sleep -Seconds 20
    $estado = az functionapp show --name $APP --resource-group $RG --query state -o tsv 2>$null
    Escrever ('state: ' + $estado + ' (esperado Running)')
}

Escrever ''
Escrever '=================================================='
Escrever ' PROXIMO PASSO: .\Demo-2-Preparar.ps1'
Escrever '=================================================='
