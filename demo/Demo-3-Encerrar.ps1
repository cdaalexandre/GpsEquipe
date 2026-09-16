#Requires -Version 5.1
<#
    Demo-3-Encerrar.ps1  --  GpsEquipe v2
    Fecha a gravacao: mata os segredos que apareceram ou ficaram em disco, apaga
    a semente da cena LGPD e elimina a pasta de segredos locais.
    Dois estagios: pre-visualizacao por padrao, execucao real com -Executar.

    Uso:
      .\Demo-3-Encerrar.ps1                                   # so mostra o que vai fazer
      .\Demo-3-Encerrar.ps1 -Executar                         # faz
      .\Demo-3-Encerrar.ps1 -Executar -RemoverAppSettingResidual
      .\Demo-3-Encerrar.ps1 -Executar -ManterSeedLgpd          # nao apaga o registro antigo

    O que faz com -Executar:
      1  apaga a semente da cena LGPD (exclusao nomeada)
      2  rotaciona TRES segredos:
           - app setting ChaveGestor : a chave que o gestor digita na tela
           - funcao VerStatus        : foi gravada em disco antes da troca pela de host
           - chave de HOST           : abre o painel administrativo
         A masterKey nao entra, porque nunca e exibida e rotacionar quebraria o
         disparo manual do Timer.
         A chave de FUNCAO do VerRelatorio tambem nao entra: o Incremento 8A
         tornou o VerRelatorio anonimo com cookie de sessao, e essa chave deixou
         de abrir qualquer coisa.
      3  prova que a ChaveGestor antiga morreu: POST com ela deve dar 401
      4  apaga C:\demo-gpsequipe (segredo TOTP e chave do gestor)
      5  limpa o clipboard
      6  opcional: remove o app setting residual do diagnostico do 503
      7  imprime as linhas de registro para o Anotacoes-v2.txt

    NAO apaga as coordenadas gravadas durante a demonstracao: elas sao a evidencia
    real do funcionamento e ficam.

    LIMITE CONHECIDO: rotacionar a ChaveGestor NAO encerra sessao de gestor ja
    aberta. O cookie e assinado com o app setting TokenChaveHmac, nao com a
    chave digitada, e vale ate 8 horas. Encerrar sessao aberta exigiria rotacionar
    o TokenChaveHmac, o que tambem derrubaria a sessao dos colaboradores. Como o
    cookie nunca aparece na barra de enderecos e o campo da tela e do tipo
    password, a exposicao em video e muito menor que a do modelo antigo.

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
$ARQ_HOST  = Join-Path $PASTA 'chave-admin-host.txt'
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

# Incremento 8A: a porta do relatorio e um POST de formulario. SEM
# -MaximumRedirection 0 de proposito: com a chave valida o 303 e seguido e o
# resultado e 200; com a chave invalida vem 401. Assim a leitura e inequivoca.
# (-MaximumRedirection 0 no PS 5.1 levanta excecao sem objeto Response e
# devolveria 0, que nao distingue revogado de erro de rede.)
function Test-ChaveGestor($chave) {
    if ([string]::IsNullOrWhiteSpace($chave)) { return -1 }
    $ses = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    try {
        $r = Invoke-WebRequest -Uri ($API + '/verrelatorio') -Method Post `
               -Body ('chave=' + $chave) -ContentType 'application/x-www-form-urlencoded' `
               -WebSession $ses -UseBasicParsing -TimeoutSec 60
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

# Segredos antigos lidos do arquivo ANTES de qualquer exclusao ou rotacao.
# Valores nunca impressos: so o tamanho.
$chaveGestorAntiga = $null
$chaveHostAntiga = $null
if (Test-Path $ARQ_HOST) { $chaveHostAntiga = (Get-Content $ARQ_HOST -Raw).Trim() }
if (Test-Path $ARQ_SEG) {
    # O Demo-2 grava a chave do gestor na linha seguinte ao rotulo. Ela e
    # base64url de 32 bytes: 43 caracteres, sem '=' e sem espaco.
    $chaveGestorAntiga = Get-Content $ARQ_SEG |
        Where-Object { $_ -match '^[A-Za-z0-9_-]{43}$' } |
        Select-Object -First 1
}

Escrever ''
Escrever '--- 1. O QUE ESTE SCRIPT VAI ALTERAR ---'

$seed = @()
$j = az storage entity query --account-name $STO --table-name $TB_COORD --filter ("PartitionKey eq '" + $PART_LGPD + "'") --auth-mode key -o json 2>$null
if ($LASTEXITCODE -eq 0 -and $j) { $seed = @(($j | ConvertFrom-Json).items) }
Escrever ('semente LGPD na particao ' + $PART_LGPD + ': ' + @($seed).Count + ' linha(s)')
if (@($seed).Count -gt 0) { Tabela (@($seed) | Select-Object PartitionKey, RowKey, Celular) }
if ($ManterSeedLgpd) { Escrever '  -> sera MANTIDA por -ManterSeedLgpd' }

Escrever ('segredos a rotacionar em ' + $APP + ':')
Escrever '  - app setting ChaveGestor        : chave que o gestor digita na tela'
Escrever '  - funcao VerStatus (default)     : foi gravada em disco antes da troca pela de host'
Escrever '  - chave de HOST (functionKeys)   : abre o painel e fica em arquivo local na gravacao'
Escrever '  masterKey NAO entra: nunca foi exibida, e rotacionar quebraria o disparo do Timer.'
Escrever '  chave de FUNCAO do VerRelatorio NAO entra: desde o Incremento 8A ela nao'
Escrever '  abre mais o relatorio, que e anonimo com cookie de sessao.'
Escrever '  AVISO: gravar app setting REINICIA a Function App (alguns segundos fora).'
Escrever ('chave do gestor antiga localizada no arquivo local: ' + $(if ($chaveGestorAntiga) { 'sim, ' + $chaveGestorAntiga.Length + ' caracteres' } else { 'nao (a prova do 401 sera pulada)' }))
Escrever ('chave de host antiga localizada no arquivo local: ' + $(if ($chaveHostAntiga) { 'sim' } else { 'nao (a prova do 401 dela sera pulada)' }))

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

# ------------------------------------------------- 3. rotacionar os segredos
Escrever ''
Escrever '--- 3. ROTACAO DOS SEGREDOS ---'
$codRot = 0

# ChaveGestor: o valor novo e gerado AQUI, nao pela Azure. 32 bytes de
# RandomNumberGenerator em base64url, o mesmo formato do valor original.
# O valor vai de variavel direto para o comando: nunca passa pelo console.
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes); $rng.Dispose()
$nova = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
$bytes = $null
az functionapp config appsettings set --name $APP --resource-group $RG `
    --settings "ChaveGestor=$nova" -o none 2>$null
Escrever ('rotacao ChaveGestor exit        : ' + $LASTEXITCODE)
$codRot = $codRot + $LASTEXITCODE
if ($LASTEXITCODE -eq 0) {
    $conf = az functionapp config appsettings list --name $APP --resource-group $RG `
              --query "[?name=='ChaveGestor'].value | [0]" -o tsv 2>$null
    Escrever ('chave nova gravada              : ' + $(if ($conf) { $conf.Length.ToString() + ' caracteres' } else { 'NAO CONFERIDA' }))
    $igual = ($conf -eq $nova)
    Escrever ('confere com a gerada            : ' + $igual)
    $conf = $null
}
$nova | Set-Clipboard
Escrever 'CHAVE NOVA do gestor no clipboard. Entregue ao gestor ANTES de limpar o clipboard.'
$nova = $null

az functionapp function keys set --name $APP --resource-group $RG --function-name VerStatus --key-name default -o none 2>$null
Escrever ('rotacao funcao VerStatus exit   : ' + $LASTEXITCODE)
$codRot = $codRot + $LASTEXITCODE
az functionapp keys set --name $APP --resource-group $RG --key-type functionKeys --key-name default -o none 2>$null
Escrever ('rotacao chave de HOST exit      : ' + $LASTEXITCODE)
$codRot = $codRot + $LASTEXITCODE
Escrever ('soma dos exit codes da rotacao  : ' + $codRot)
if ($codRot -ne 0) {
    Escrever 'FALHA em alguma rotacao. Confira a sintaxe da sua versao da CLI:'
    Escrever '  az functionapp function keys set --help'
    Escrever '  az functionapp config appsettings set --help'
    Escrever 'O SEGREDO DO VIDEO AINDA PODE ESTAR VALIDO. Resolva antes de entregar.'
}

# ------------------------------------------------- 4. provar a revogacao
Escrever ''
Escrever '--- 4. PROVA DA REVOGACAO DA CHAVE DO GESTOR ---'
if (-not $chaveGestorAntiga) {
    Escrever 'chave antiga nao localizada em disco; teste pulado.'
    Escrever 'Confira manualmente: abra o relatorio e tente entrar com a chave antiga.'
} else {
    # A troca de app setting reinicia o app: as primeiras tentativas podem falhar
    # por indisponibilidade, nao por chave valida. Por isso o laco.
    $st = 0
    for ($i = 1; $i -le 5; $i++) {
        Start-Sleep -Seconds 12
        $st = Test-ChaveGestor $chaveGestorAntiga
        Escrever ('tentativa ' + $i + ': HTTP ' + $st + ' (espera 401; 200 = AINDA VALIDA)')
        if ($st -eq 401) { break }
    }
    if ($st -eq 401) {
        Escrever 'REVOGADA: a chave que apareceu na gravacao nao entra mais.'
    } elseif ($st -eq 200) {
        Escrever 'ATENCAO: a chave antiga AINDA ABRE o relatorio. A rotacao nao pegou.'
    } else {
        Escrever ('INCONCLUSIVO: HTTP ' + $st + '. Repita o teste em alguns minutos.')
    }
    $chaveGestorAntiga = $null
}

# ------------------------------------------------- 5. apagar os segredos locais
Escrever ''
Escrever '--- 5. SEGREDOS EM DISCO ---'
if (Test-Path $PASTA) {
    Escrever 'AVISO: a chave NOVA do gestor esta no clipboard. Entregue antes de continuar.'
    Remove-Item -Path $PASTA -Recurse -Force
    Escrever ('apagada: ' + $PASTA + ' | ainda existe? ' + (Test-Path $PASTA))
} else {
    Escrever 'nada a apagar.'
}

# --- prova extra: a chave de HOST antiga tambem morreu? ---
if ($chaveHostAntiga) {
    Escrever ''
    Escrever '--- 5b. A CHAVE DE HOST ANTIGA FOI REVOGADA? ---'
    $sh = 0
    for ($k = 1; $k -le 3; $k++) {
        Start-Sleep -Seconds 10
        $sh = Get-Status ($API + '/verstatus?code=' + $chaveHostAntiga)
        Escrever ('tentativa ' + $k + ': HTTP ' + $sh + ' (espera 401)')
        if ($sh -eq 401) { break }
    }
    if ($sh -eq 401) { Escrever 'REVOGADA: a chave de host do video nao abre mais o painel.' }
    else { Escrever ('ATENCAO: a chave de host antiga ainda responde ' + $sh + '. Repita em alguns minutos.') }
    $chaveHostAntiga = $null
}

Escrever ''
Escrever '--- 5c. CLIPBOARD ---'
Escrever 'A chave NOVA do gestor esta no clipboard. Se ja a entregou, limpe com:'
Escrever '  Set-Clipboard -Value '' '''

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
    $st2 = Get-Status ($API + '/verrelatorio')
    Escrever ('app respondendo apos o reinicio: HTTP ' + $st2 + ' (401 e o esperado, prova que o host subiu)')
}

# ------------------------------------------------- 7. registro
Escrever ''
Escrever '=================================================='
Escrever ' REGISTRO PARA O Anotacoes-v2.txt (copie as 3 linhas)'
Escrever '=================================================='
$hoje = Get-Date -Format 'yyyy-MM-dd'
Escrever ('  GRAVACAO DA DEMONSTRACAO (' + $hoje + '): roteiro executado com')
Escrever ('    Demo-1-Limpar.ps1, Demo-2-Preparar.ps1 e Demo-3-Encerrar.ps1.')
Escrever ('  Comandos efetivos: az resource list / az functionapp function list / POST iniciarsessao')
Escrever ('    / POST recebercoordenadas (1 valido e 6 invalidos) / POST verrelatorio com a chave')
Escrever ('    / POST admin/functions/AnonimizarCoordenadas.')
Escrever ('  Resultado: 403 com texto unico em todos os casos invalidos, relatorio 401 sem sessao')
Escrever ('    com a tela de entrada no corpo e 200 apos entrar com a chave, anonimizacao comprovada')
Escrever ('    na particao ' + $PART_LGPD + ', e ChaveGestor, chave de VerStatus e chave de host')
Escrever ('    rotacionadas apos a gravacao (soma dos exit codes: ' + $codRot + ').')
Escrever ''
Escrever 'CONFERENCIA DO VIDEO ANTES DE ENTREGAR:'
Escrever '  [ ] nenhuma connection string, account key ou local.settings.json visivel'
Escrever '  [ ] a chave de acesso do gestor nao foi falada em voz alta nem apareceu em texto claro'
Escrever '      (o campo da tela e do tipo password, mas o clipboard e o arquivo de segredos nao)'
Escrever '  [ ] o segredo do Authenticator nao apareceu na tela'
Escrever '  [ ] a ChaveGestor e a chave de host ja foram rotacionadas (passos 3, 4 e 5b)'
Escrever '  [ ] nenhum recurso, caminho ou documento da PRODESP na tela'
Escrever '  [ ] o audio pegou as cenas que valem nota'
Escrever ''
Escrever 'A URL do relatorio NAO e mais segredo: desde o Incremento 8A ela pode aparecer'
Escrever 'na barra de enderecos sem risco. O segredo e a chave digitada na tela.'
Escrever ''
Escrever 'Para gravar de novo: rode Demo-1 e Demo-2 outra vez. O Demo-2 le as chaves novas.'
Escrever 'Para entregar a chave ao gestor depois: consulte o ModoDeUso-GpsEquipe.md.'
