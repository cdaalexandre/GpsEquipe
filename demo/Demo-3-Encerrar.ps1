#Requires -Version 5.1
<#
    Demo-3-Encerrar.ps1  --  GpsEquipe v2
    Fecha a gravacao: mata a chave que apareceu na barra de enderecos, apaga a
    semente da cena LGPD e elimina os segredos que ficaram em disco.
    Dois estagios: pre-visualizacao por padrao, execucao real com -Executar.

    Uso:
      .\Demo-3-Encerrar.ps1                                   # so mostra o que vai fazer
      .\Demo-3-Encerrar.ps1 -Executar                         # faz
      .\Demo-3-Encerrar.ps1 -Executar -RemoverAppSettingResidual
      .\Demo-3-Encerrar.ps1 -Executar -ManterSeedLgpd          # nao apaga o registro antigo

    O que faz com -Executar:
      1  apaga a semente da cena LGPD (exclusao nomeada)
      2  rotaciona a chave default de VerRelatorio (a Azure gera o valor novo;
         o valor vem mascarado no resultado, logo nada sensivel vai ao console)
      3  prova que a URL antiga morreu: espera HTTP 401
      4  apaga C:\demo-gpsequipe (PIN e URL com chave)
      5  limpa o clipboard
      6  opcional: remove o app setting residual do diagnostico do 503
      7  imprime as linhas de registro para o Anotacoes-v2.txt

    NAO apaga as coordenadas gravadas durante a demonstracao: elas sao a evidencia
    real do funcionamento e ficam.
    Somente texto ASCII: PowerShell 5.1 le arquivo sem BOM como ANSI.
#>
[CmdletBinding()]
param(
    [switch]$Executar,
    [switch]$ManterSeedLgpd,
    [switch]$RemoverAppSettingResidual
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- nomes vivos (Anotacoes-v2.txt, Secao 2) ----
$RG        = 'GpsEquipe-RG'
$STO       = 'gpsequipebad1'
$APP       = 'GpsEquipe-App-bad1'
$TB_COORD  = 'Coordenadas'
$TENANT    = '38ae2f02-5710-4e12-80bb-83600c3fdf1e'
$USUARIO   = 'alexandre.calzetta@cs.unicid.edu.br'
$API       = 'https://gpsequipe-app-bad1.azurewebsites.net/api'

$PART_LGPD = '2026-01-01'
$ROW_LGPD  = 'demolgpd01'
$PASTA     = 'C:\demo-gpsequipe'
$ARQ_SEG   = Join-Path $PASTA 'segredos-demo.txt'
$RESIDUO   = 'WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED'

function Escrever($t) { Write-Host $t }
function Tabela($obj) { ($obj | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Host }

function Test-Guarda {
    $j = az account show -o json 2>$null
    if (-not $j) { Escrever 'ABORTADO: az account show nao respondeu.'; return $false }
    $c = $j | ConvertFrom-Json
    Escrever ('conta   : ' + $c.name)
    Escrever ('tenant  : ' + $c.tenantId)
    Escrever ('usuario : ' + $c.user.name)
    if ($c.tenantId -ne $TENANT -or $c.user.name -ne $USUARIO) {
        Escrever 'ABORTADO: tenant ou usuario fora do esperado. NADA foi tocado.'
        return $false
    }
    Escrever 'GUARDA OK'
    return $true
}

function Get-Status($uri) {
    try {
        $r = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 60
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    }
}

# ------------------------------------------------------------------ inicio
Escrever '=================================================='
Escrever ' Demo-3-Encerrar :: GpsEquipe v2'
$modo = if ($Executar) { 'EXECUCAO REAL' } else { 'PRE-VISUALIZACAO (nada sera alterado)' }
Escrever (' modo: ' + $modo)
Escrever '=================================================='
Escrever ''

if (-not (Test-Guarda)) { return }

# URL antiga, lida do arquivo ANTES de qualquer exclusao. Valor nunca impresso.
$urlAntiga = $null
if (Test-Path $ARQ_SEG) {
    $linha = Get-Content $ARQ_SEG | Where-Object { $_ -like 'https://*verrelatorio?code=*' } | Select-Object -First 1
    if ($linha) { $urlAntiga = $linha.Trim() }
}

Escrever ''
Escrever '--- 1. O QUE ESTE SCRIPT VAI ALTERAR ---'

$seed = @()
$j = az storage entity query --account-name $STO --table-name $TB_COORD --filter ("PartitionKey eq '" + $PART_LGPD + "'") --auth-mode key -o json 2>$null
if ($LASTEXITCODE -eq 0 -and $j) { $seed = @(($j | ConvertFrom-Json).items) }
Escrever ('semente LGPD na particao ' + $PART_LGPD + ': ' + @($seed).Count + ' linha(s)')
if (@($seed).Count -gt 0) { Tabela (@($seed) | Select-Object PartitionKey, RowKey, Celular) }
if ($ManterSeedLgpd) { Escrever '  -> sera MANTIDA por -ManterSeedLgpd' }

Escrever ('chave a rotacionar: default de VerRelatorio em ' + $APP)
Escrever ('URL antiga localizada no arquivo local: ' + $(if ($urlAntiga) { 'sim' } else { 'nao (a prova do 401 sera pulada)' }))

if (Test-Path $PASTA) {
    Escrever ('pasta de segredos a apagar: ' + $PASTA)
    Tabela (Get-ChildItem $PASTA | Select-Object Name, Length, LastWriteTime)
} else {
    Escrever ('pasta de segredos: ' + $PASTA + ' nao existe')
}

if ($RemoverAppSettingResidual) {
    $nomes = az functionapp config appsettings list --name $APP --resource-group $RG --query "[].name" -o tsv 2>$null
    $tem = @($nomes) -contains $RESIDUO
    Escrever ('app setting residual ' + $RESIDUO + ': ' + $(if ($tem) { 'presente, sera removido' } else { 'ausente, nada a fazer' }))
    Escrever '  AVISO: remover app setting reinicia a Function App.'
}

if (-not $Executar) {
    Escrever ''
    Escrever '--- ESTA FOI A PRE-VISUALIZACAO ---'
    Escrever 'Para executar de verdade:'
    Escrever '  .\Demo-3-Encerrar.ps1 -Executar'
    return
}

# ------------------------------------------------- 2. apagar a semente
Escrever ''
Escrever '--- 2. SEMENTE DA CENA LGPD ---'
if ($ManterSeedLgpd) {
    Escrever 'mantida por parametro.'
} else {
    az storage entity delete --account-name $STO --table-name $TB_COORD `
        --partition-key $PART_LGPD --row-key $ROW_LGPD --auth-mode key -o none 2>$null
    Escrever ('exit code: ' + $LASTEXITCODE)
    $j2 = az storage entity query --account-name $STO --table-name $TB_COORD --filter ("PartitionKey eq '" + $PART_LGPD + "'") --auth-mode key -o json 2>$null
    $restou = 0
    if ($j2) { $restou = @(($j2 | ConvertFrom-Json).items).Count }
    Escrever ('linhas restantes na particao ' + $PART_LGPD + ': ' + $restou + ' (esperado 0)')
}

# ------------------------------------------------- 3. rotacionar a chave
Escrever ''
Escrever '--- 3. ROTACAO DA CHAVE DE VerRelatorio ---'
az functionapp function keys set --name $APP --resource-group $RG `
    --function-name VerRelatorio --key-name default -o none 2>$null
$codRot = $LASTEXITCODE
Escrever ('exit code da rotacao: ' + $codRot)
if ($codRot -ne 0) {
    Escrever 'FALHA na rotacao. Confira a sintaxe da sua versao da CLI:'
    Escrever '  az functionapp function keys set --help'
    Escrever 'A chave do video AINDA ESTA VALIDA. Resolva antes de entregar o video.'
} else {
    Escrever 'chave nova gerada pela Azure (valor mascarado no resultado, nada impresso aqui).'
}

# ------------------------------------------------- 4. provar a revogacao
Escrever ''
Escrever '--- 4. PROVA DA REVOGACAO (URL antiga deve dar 401) ---'
if (-not $urlAntiga) {
    Escrever 'URL antiga nao localizada; teste pulado. Confira manualmente pelo favorito do navegador.'
} else {
    $st = 0
    for ($i = 1; $i -le 4; $i++) {
        Start-Sleep -Seconds 10
        $st = Get-Status $urlAntiga
        Escrever ('tentativa ' + $i + ': HTTP ' + $st)
        if ($st -eq 401) { break }
    }
    if ($st -eq 401) {
        Escrever 'REVOGADA: a URL que aparece no video nao abre mais.'
    } else {
        Escrever 'ATENCAO: a URL antiga ainda responde ' + $st + '. Repita o teste em alguns minutos.'
    }
}

# ------------------------------------------------- 5. apagar os segredos locais
Escrever ''
Escrever '--- 5. SEGREDOS EM DISCO ---'
if (Test-Path $PASTA) {
    Remove-Item -Path $PASTA -Recurse -Force
    Escrever ('apagada: ' + $PASTA + ' | ainda existe? ' + (Test-Path $PASTA))
} else {
    Escrever 'nada a apagar.'
}
Set-Clipboard -Value ' '
Escrever 'clipboard limpo.'

# ------------------------------------------------- 6. residuo de app setting
if ($RemoverAppSettingResidual) {
    Escrever ''
    Escrever '--- 6. APP SETTING RESIDUAL ---'
    az functionapp config appsettings delete --name $APP --resource-group $RG `
        --setting-names $RESIDUO -o none 2>$null
    Escrever ('exit code: ' + $LASTEXITCODE)
    Start-Sleep -Seconds 20
    $nomes2 = az functionapp config appsettings list --name $APP --resource-group $RG --query "[].name" -o tsv 2>$null
    Escrever ('ainda presente? ' + (@($nomes2) -contains $RESIDUO))
    $st2 = Get-Status 'https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio'
    Escrever ('app respondendo apos o reinicio: HTTP ' + $st2 + ' (401 e o esperado, prova que o host subiu)')
}

# ------------------------------------------------- 7. registro
Escrever ''
Escrever '=================================================='
Escrever ' REGISTRO PARA O Anotacoes-v2.txt (copie as 3 linhas)'
Escrever '=================================================='
$hoje = Get-Date -Format 'yyyy-MM-dd'
Escrever ('  GRAVACAO DA DEMONSTRACAO (' + $hoje + '): roteiro de 9 cenas executado com')
Escrever ('    Demo-1-Limpar.ps1, Demo-2-Preparar.ps1 e Demo-3-Encerrar.ps1.')
Escrever ('  Comandos efetivos: az resource list / az functionapp function list / POST recebercoordenadas')
Escrever ('    (1 valido e 3 invalidos) / POST admin/functions/AnonimizarCoordenadas -> 202 / GET verrelatorio.')
Escrever ('  Resultado: 403 com texto unico nos 3 casos, relatorio 401 sem chave e 200 com chave,')
Escrever ('    anonimizacao comprovada ao vivo na particao ' + $PART_LGPD + ', chave default de VerRelatorio rotacionada')
Escrever ('    apos a gravacao (exit ' + $codRot + ').')
Escrever ''
Escrever 'CONFERENCIA DO VIDEO ANTES DE ENTREGAR:'
Escrever '  [ ] nenhuma connection string, account key ou local.settings.json visivel'
Escrever '  [ ] o PIN nao foi falado em voz alta nem apareceu em texto claro'
Escrever '  [ ] a chave do relatorio ja foi rotacionada (passo 3 e 4 deste script)'
Escrever '  [ ] nenhum recurso, caminho ou documento da PRODESP na tela'
Escrever '  [ ] o audio pegou as cenas 4, 7 e 8'
Escrever ''
Escrever 'Para gravar de novo: rode Demo-1 e Demo-2 outra vez. O Demo-2 gera PIN e URL novos.'
Escrever 'Para entregar a chave ao gestor depois: consulte o ModoDeUso-GpsEquipe.md.'
