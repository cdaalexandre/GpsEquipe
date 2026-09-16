#Requires -Version 5.1
<#
    Demo-2-Preparar.ps1  --  GpsEquipe v2
    Deixa o sistema pronto para a gravacao e diz, no fim, PRONTO ou PENDENTE
    item por item. Nao apaga nada: rode o Demo-1-Limpar.ps1 antes.

    Uso:
      .\Demo-2-Preparar.ps1                 # faz tudo
      .\Demo-2-Preparar.ps1 -SemSeedLgpd    # nao cria o registro antigo da cena LGPD
      .\Demo-2-Preparar.ps1 -NovoTotp       # gera segredo TOTP novo (exige recadastrar o app)

    O que faz, em ordem:
      1  guarda de tenant
      2  inventario de recursos, functions, runtime
      3  garante o colaborador cadastrado
      4  confere o segredo TOTP; gera um novo com -NovoTotp
      5  teste ponta a ponta POR TOKEN, com faxina automatica do registro
      6  falhas de identificacao nos DOIS endpoints (403 com texto unico)
      7  semente da cena LGPD: registro em particao antiga
      8  chave de acesso do gestor -> arquivo local (valor nunca no console)
      9  atalhos de cena em C:\demo-gpsequipe\cena.ps1
     10  aquecimento, tela de entrada do gestor e verificacao final

    INCREMENTO 8B: o PIN foi extirpado do sistema. Nao existe mais plano B por
    PIN, nem a function DefinirPin, nem o index.html antigo. Token de sessao e a
    unica identificacao aceita pelo ReceberCoordenadas.
    INCREMENTO 8A: a chave do relatorio saiu da URL. O gestor digita a chave de
    acesso numa tela e recebe um cookie de sessao de 8 horas.

    SEGREDO: o segredo TOTP e a chave de acesso do gestor vao para
    C:\demo-gpsequipe (fora do repo e fora do OneDrive). A URL do relatorio NAO
    e mais segredo: pode ser mostrada na tela. O Demo-3-Encerrar.ps1 apaga essa
    pasta; a chave a rotacionar agora e o app setting ChaveGestor, nao mais a
    chave de funcao do VerRelatorio.
    Somente texto ASCII: PowerShell 5.1 le arquivo sem BOM como ANSI.
#>
[CmdletBinding()]
param(
    [switch]$SemSeedLgpd,
    [switch]$NovoTotp
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- nomes vivos (Anotacoes-v2.txt, Secao 2) ----
$RG       = 'GpsEquipe-RG'
$STO      = 'gpsequipebad1'
$APP      = 'GpsEquipe-App-bad1'
$TB_COORD = 'Coordenadas'
$TB_FUNC  = 'FuncionariosPermitidos'
$TENANT   = '38ae2f02-5710-4e12-80bb-83600c3fdf1e'
$USUARIO  = 'alexandre.calzetta@cs.unicid.edu.br'

$SITE      = 'https://gpsequipebad1.z15.web.core.windows.net/'
$API       = 'https://gpsequipe-app-bad1.azurewebsites.net/api'
$ADMIN     = 'https://gpsequipe-app-bad1.azurewebsites.net/admin/functions'

$CEL       = '5511982253855'   # colaborador cadastrado
$LAT       = -23.5505
$LON       = -46.6333

$PART_LGPD = '2026-01-01'      # particao antiga da cena LGPD
$ROW_LGPD  = 'demolgpd01'
$CEL_LGPD  = 'cel5511000000000'  # PRECISA de letra: valor so com digitos faz a
                                  # CLI inferir Edm.Int32 e estourar (licao bloco H)

$PASTA     = 'C:\demo-gpsequipe'
$ARQ_SEG   = Join-Path $PASTA 'segredos-demo.txt'
$ARQ_CENA  = Join-Path $PASTA 'cena.ps1'

$estado = [ordered]@{}

function Escrever($t) { Write-Host $t }
function Tabela($obj) { ($obj | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Host }
function Marcar($chave, $ok, $detalhe) {
    $estado[$chave] = [pscustomobject]@{
        item      = $chave
        situacao  = if ($ok) { 'PRONTO' } else { 'PENDENTE' }
        detalhe   = $detalhe
    }
}

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

# HTTP com leitura do corpo tambem em erro.
# No PS 5.1 $_.ErrorDetails.Message vem vazio: e preciso ler o stream.
function Invoke-Http {
    param(
        [string]$Uri,
        [string]$Metodo = 'GET',
        $Corpo = $null,
        [hashtable]$Cabecalhos = $null,
        [int]$Timeout = 90
    )
    $p = @{ Uri = $Uri; Method = $Metodo; UseBasicParsing = $true; TimeoutSec = $Timeout }
    if ($null -ne $Corpo)      { $p.Body = $Corpo; $p.ContentType = 'application/json' }
    if ($null -ne $Cabecalhos) { $p.Headers = $Cabecalhos }
    try {
        $r = Invoke-WebRequest @p
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Texto = [string]$r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) {
            return [pscustomobject]@{ Status = 0; Texto = $_.Exception.Message }
        }
        # Verificado em 15/09/2026 nesta maquina: o Invoke-WebRequest do PS 5.1
        # consome o stream de erro para popular ErrorDetails.Message, e ler o
        # stream depois devolve vazio. Ordem correta: ErrorDetails primeiro.
        $txt = [string]$_.ErrorDetails.Message
        if (-not [string]::IsNullOrEmpty($txt)) { return [pscustomobject]@{ Status = [int]$resp.StatusCode; Texto = $txt } }
        $txt = ''
        try {
            $sr  = New-Object IO.StreamReader($resp.GetResponseStream())
            $txt = $sr.ReadToEnd()
            $sr.Close()
        } catch { $txt = '' }
        return [pscustomobject]@{ Status = [int]$resp.StatusCode; Texto = $txt }
    }
}

function Get-Linhas($tabela, $filtro) {
    if ($filtro) {
        $j = az storage entity query --account-name $STO --table-name $tabela --filter $filtro --auth-mode key -o json 2>$null
    } else {
        $j = az storage entity query --account-name $STO --table-name $tabela --auth-mode key -o json 2>$null
    }
    if ($LASTEXITCODE -ne 0 -or -not $j) { return @() }
    return @(($j | ConvertFrom-Json).items)
}

# Incremento 7: TOTP calculado localmente, para o script abrir sessao sem
# depender do celular. Mesmo algoritmo do SegurancaTotp.cs, conferido contra os
# vetores da RFC 6238 antes de virar codigo.
function ConvertFrom-Base32([string]$texto) {
    $abc = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $bits = ''
    foreach ($c in $texto.ToUpperInvariant().ToCharArray()) {
        $i = $abc.IndexOf($c)
        if ($i -ge 0) { $bits += [Convert]::ToString($i, 2).PadLeft(5, '0') }
    }
    $bytes = New-Object System.Collections.Generic.List[byte]
    for ($p = 0; $p + 8 -le $bits.Length; $p += 8) { $bytes.Add([Convert]::ToByte($bits.Substring($p, 8), 2)) }
    return $bytes.ToArray()
}

function Get-TotpCodigo([string]$segredoBase32) {
    $s = ConvertFrom-Base32 $segredoBase32
    $c = [long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
    $b = [BitConverter]::GetBytes($c)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($b) }
    $h = New-Object Security.Cryptography.HMACSHA1
    $h.Key = $s
    $hash = $h.ComputeHash($b)
    $h.Dispose()
    $o = $hash[$hash.Length - 1] -band 0x0f
    $bin = (($hash[$o] -band 0x7f) -shl 24) -bor (($hash[$o + 1] -band 0xff) -shl 16) -bor (($hash[$o + 2] -band 0xff) -shl 8) -bor ($hash[$o + 3] -band 0xff)
    return ($bin % 1000000).ToString('D6')
}

function Get-SegredoLocal([string]$caminho) {
    if (-not (Test-Path $caminho)) { return $null }
    return (Get-Content $caminho | Where-Object { $_ -match '^[A-Z2-7]{32}$' } | Select-Object -First 1)
}

# ------------------------------------------------------------------ inicio
Escrever '=================================================='
Escrever ' Demo-2-Preparar :: GpsEquipe v2'
Escrever (' data/hora: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Escrever '=================================================='
Escrever ''

Escrever '--- 1. GUARDA DE TENANT ---'
if (-not (Test-Guarda)) { return }

# ---------------------------------------------------------------- 2. inventario
Escrever ''
Escrever '--- 2. INVENTARIO ---'
$rec = az resource list --resource-group $RG --query "[].{nome:name, tipo:type}" -o json 2>$null | ConvertFrom-Json
Tabela @($rec)
Marcar 'recursos no RG' (@($rec).Count -ge 4) (@($rec).Count.ToString() + ' recursos')

$fnJson = az functionapp function list --name $APP --resource-group $RG -o json 2>$null
$fn = @()
if ($LASTEXITCODE -eq 0 -and $fnJson) {
        # PS 5.1: ConvertFrom-Json emite um array JSON como UM objeto no pipeline.
    # Sem a variavel intermediaria, o ForEach-Object itera UMA vez com $_ igual
    # ao array inteiro - e o resultado mistura o nome do ultimo item com o
    # binding do primeiro. A variavel desembala o array.
    $fnObj = $fnJson | ConvertFrom-Json
    $fn = @($fnObj) | ForEach-Object {
        [pscustomobject]@{
            funcao  = ($_.name -split '/')[-1]
            trigger = $_.config.bindings[0].type
            auth    = $_.config.bindings[0].authLevel
            cron    = $_.config.bindings[0].schedule
        }
    }
}
Tabela $fn
# Incremento 8B: sete functions. DefinirPin saiu de producao e a ausencia dela
# e conferida abaixo: se voltar, o PIN voltou por tras e a extirpacao regrediu.
$esperadas = @('ReceberCoordenadas','VerRelatorio','IniciarSessao','DefinirTotp',
               'GerenciarColaborador','VerStatus','AnonimizarCoordenadas')
$faltando  = @($esperadas | Where-Object { $_ -notin @($fn.funcao) })
Marcar 'functions em producao' ($faltando.Count -eq 0) ($(if ($faltando.Count) { 'faltando: ' + ($faltando -join ', ') } else { '7 de 7' }))

$pinViva = 'DefinirPin' -in @($fn.funcao)
Escrever ('DefinirPin em producao? ' + $pinViva + '  (espera False: extirpada no 8B)')
Marcar 'PIN extirpado (functions)' (-not $pinViva) ($(if ($pinViva) { 'REGRESSAO: DefinirPin voltou' } else { 'DefinirPin ausente, correto' }))

$fx = az functionapp config show --name $APP --resource-group $RG --query linuxFxVersion -o tsv 2>$null
$st = az functionapp show --name $APP --resource-group $RG --query state -o tsv 2>$null
Escrever ('runtime: ' + $fx + ' | state: ' + $st)
Marcar 'app rodando' ($st -eq 'Running') ($fx + ' / ' + $st)

# ------------------------------------------------------------- 3. cadastro
Escrever ''
Escrever '--- 3. COLABORADOR CADASTRADO ---'
$func = Get-Linhas $TB_FUNC ("RowKey eq '" + $CEL + "'")
if (@($func).Count -eq 0) {
    Escrever 'nao cadastrado; inserindo agora'
    az storage entity insert --account-name $STO --table-name $TB_FUNC --auth-mode key `
        --entity PartitionKey="FUNCIONARIO" RowKey="$CEL" -o none 2>$null
    Escrever ('exit code: ' + $LASTEXITCODE)
    $func = Get-Linhas $TB_FUNC ("RowKey eq '" + $CEL + "'")
}
# Incremento 8B: PinDefinidoEm foi eliminado da tabela. A coluna que diz se o
# colaborador esta habilitado agora e TotpDefinidoEm.
Tabela (@($func) | Select-Object PartitionKey, RowKey, TotpDefinidoEm)
Marcar 'cadastro do colaborador' (@($func).Count -eq 1) $CEL

# Regressao do 8B: nenhum campo de PIN pode ter voltado para a linha.
$campos = @()
if (@($func).Count -eq 1) { $campos = @(@($func)[0].PSObject.Properties.Name | Where-Object { $_ -like 'Pin*' }) }
Escrever ('campos Pin* na linha do colaborador: ' + $campos.Count + '  (espera 0)')
Marcar 'PIN extirpado (tabela)' ($campos.Count -eq 0) ($(if ($campos.Count) { 'REGRESSAO: ' + ($campos -join ', ') } else { 'nenhum campo de PIN' }))

# ------------------------------------------------ 4. TOTP (Authenticator)
Escrever ''
Escrever '--- 4. TOTP (Microsoft Authenticator) ---'
$ARQ_TOTP = Join-Path $PASTA ('totp-' + $CEL + '.txt')
$temTotp = -not [string]::IsNullOrEmpty((@($func)[0].TotpSegredo))
Escrever ('segredo na tabela : ' + $temTotp + ' | definido em: ' + (@($func)[0].TotpDefinidoEm))
$segredoLocal = Get-SegredoLocal $ARQ_TOTP
Escrever ('segredo em disco  : ' + $(if ($segredoLocal) { 'sim (' + $ARQ_TOTP + ')' } else { 'nao' }))

if ($NovoTotp -or -not $temTotp) {
    New-Item -ItemType Directory -Path $PASTA -Force | Out-Null
    $kTotp = az functionapp function keys list --name $APP --resource-group $RG `
               --function-name DefinirTotp --query default -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($kTotp)) {
        Escrever 'FALHA: nao consegui ler a chave de DefinirTotp.'
    } else {
        $r = Invoke-Http -Uri ($API + '/definirtotp?code=' + $kTotp) -Metodo Post `
               -Corpo (@{ Acao = 'cadastrar'; Celular = $CEL } | ConvertTo-Json -Compress)
        $kTotp = $null
        Escrever ('DefinirTotp cadastrar -> HTTP ' + $r.Status)
        if ($r.Status -eq 201) {
            $segredoLocal = ($r.Texto | ConvertFrom-Json).segredo
            $txtTotp = @(
                ('GpsEquipe - segredo TOTP de ' + $CEL),
                ('gerado em: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
                '',
                'Chave para o Microsoft Authenticator (inserir chave manualmente):',
                $segredoLocal,
                '',
                'APAGUE a entrada GpsEquipe antiga no app antes de cadastrar esta.'
            ) -join [Environment]::NewLine
            [IO.File]::WriteAllText($ARQ_TOTP, $txtTotp, [Text.UTF8Encoding]::new($false))
            Escrever ('segredo NOVO de ' + $segredoLocal.Length + ' caracteres gravado em ' + $ARQ_TOTP)
            Escrever 'ATENCAO: recadastre a entrada GpsEquipe no Authenticator ANTES de gravar.'
            $func = Get-Linhas $TB_FUNC ("RowKey eq '" + $CEL + "'")
            $temTotp = $true
        }
    }
}
Marcar 'TOTP cadastrado' ($temTotp -and $null -ne $segredoLocal) ('tabela: ' + $temTotp + ', segredo em disco: ' + ($null -ne $segredoLocal))

# --------------------------------- 5. teste ponta a ponta por TOKEN
Escrever ''
Escrever '--- 5. TESTE PONTA A PONTA POR TOKEN (com faxina do registro) ---'
$tokenTeste = $null
if ($null -eq $segredoLocal) {
    Escrever 'sem segredo em disco: nao consigo calcular codigo. Rode com -NovoTotp.'
    Marcar 'caminho feliz (token)' $false 'sem segredo local para calcular o codigo'
} else {
    $codigo = Get-TotpCodigo $segredoLocal
    $rs = Invoke-Http -Uri ($API + '/iniciarsessao') -Metodo Post `
            -Corpo (@{ Celular = $CEL; Codigo = $codigo } | ConvertTo-Json -Compress)
    Escrever ('IniciarSessao -> HTTP ' + $rs.Status)
    if ($rs.Status -ne 200) {
        Escrever ('   corpo: ' + $rs.Texto)
        Marcar 'caminho feliz (token)' $false ('IniciarSessao HTTP ' + $rs.Status)
    } else {
        $tokenTeste = ($rs.Texto | ConvertFrom-Json).token
        Escrever ('token de ' + $tokenTeste.Length + ' caracteres obtido')
        $hojePart = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $antes = @(Get-Linhas $TB_COORD ("PartitionKey eq '" + $hojePart + "'")) | Select-Object -ExpandProperty RowKey
        $r = Invoke-Http -Uri ($API + '/recebercoordenadas') -Metodo Post `
               -Corpo (@{ Celular = $CEL; Token = $tokenTeste; Latitude = $LAT; Longitude = $LON } | ConvertTo-Json -Compress)
        Escrever ('POST com token -> HTTP ' + $r.Status + '  ' + $r.Texto)
        Start-Sleep -Seconds 3
        $novos = @(Get-Linhas $TB_COORD ("PartitionKey eq '" + $hojePart + "'") | Where-Object { $_.RowKey -notin $antes })
        Escrever ('linhas novas: ' + $novos.Count + ' -> apagando para nao poluir a gravacao')
        foreach ($n in $novos) {
            az storage entity delete --account-name $STO --table-name $TB_COORD `
                --partition-key $n.PartitionKey --row-key $n.RowKey --auth-mode key -o none 2>$null
        }
        $restam = @(Get-Linhas $TB_COORD ("PartitionKey eq '" + $hojePart + "'")).Count
        Escrever ('linhas na particao de hoje agora: ' + $restam)
        Marcar 'caminho feliz (token)' (($r.Status -eq 200) -and ($novos.Count -ge 1)) 'sessao aberta e 1 registro gravado e removido'
        Escrever 'ATENCAO: a janela de 30s deste codigo foi QUEIMADA pelo anti-replay.'
        Escrever '         Espere o codigo trocar no Authenticator antes de usar o app.'
    }
}

# --------------------------- 6. falhas de identificacao nos 2 endpoints
Escrever ''
Escrever '--- 6. FALHAS DE IDENTIFICACAO (esperado: 403 com texto identico) ---'
$tokenFalso = 'aaaa.bbbb'
if ($tokenTeste) {
    # A assinatura tem 32 bytes = 43 caracteres base64 sem padding, e 43x6=258
    # bits: os 2 ultimos bits do ULTIMO caractere nao codificam nada. Trocar o
    # ultimo caractere entre A e B decodifica para os MESMOS bytes, e o token
    # continua valido. Adulterar o PRIMEIRO caractere da assinatura, cujos 6
    # bits sao todos significativos.
    $partes = $tokenTeste.Split('.')
    $pri = $partes[1].Substring(0, 1)
    $tokenFalso = $partes[0] + '.' + $(if ($pri -eq 'A') { 'Z' } else { 'A' }) + $partes[1].Substring(1)
}
# Incremento 8B, caso 6: manda o campo Pin que o sistema antigo aceitava. O 403
# prova que o campo nao e mais LIDO - nenhum cliente antigo consegue enviar.
$casos = @(
    @{ n = '1. codigo TOTP errado';   rota = 'iniciarsessao';      c = @{ Celular = $CEL; Codigo = '000001' } },
    @{ n = '2. numero fora da lista'; rota = 'iniciarsessao';      c = @{ Celular = '5511900000000'; Codigo = '123456' } },
    @{ n = '3. sem codigo';           rota = 'iniciarsessao';      c = @{ Celular = $CEL } },
    @{ n = '4. token adulterado';     rota = 'recebercoordenadas'; c = @{ Celular = $CEL; Token = $tokenFalso; Latitude = $LAT; Longitude = $LON } },
    @{ n = '5. sem token';            rota = 'recebercoordenadas'; c = @{ Celular = $CEL; Latitude = $LAT; Longitude = $LON } },
    @{ n = '6. Pin em vez de token';  rota = 'recebercoordenadas'; c = @{ Celular = $CEL; Pin = '135791'; Latitude = $LAT; Longitude = $LON } }
)
$textos = @()
foreach ($k in $casos) {
    $r = Invoke-Http -Uri ($API + '/' + $k.rota) -Metodo Post -Corpo ($k.c | ConvertTo-Json -Compress)
    Escrever (('{0,-24} {1,-20} -> {2}  [{3}]' -f $k.n, $k.rota, $r.Status, $r.Texto))
    $textos += ('' + $r.Status + '|' + $r.Texto)
}
$unico = (@($textos | Select-Object -Unique).Count -eq 1)
# Textos VAZIOS tambem seriam "resposta unica". Exigir corpo legivel, senao a
# Cena 4 grava um 403 sem mensagem na tela.
$comTexto = (@($textos | Where-Object { $_ -match '\|\S' }).Count -eq $textos.Count)
Marcar 'falha fechada (cena 4)' ($unico -and $comTexto -and $textos[0] -like '403*') ($textos.Count.ToString() + ' casos em 2 endpoints, resposta unica: ' + $unico + ', com texto: ' + $comTexto)

if ($tokenTeste) {
    $r = Invoke-Http -Uri ($API + '/recebercoordenadas') -Metodo Post `
           -Corpo (@{ Celular = '5511900000002'; Token = $tokenTeste; Latitude = $LAT; Longitude = $LON } | ConvertTo-Json -Compress)
    Escrever (('{0,-24} {1,-20} -> {2}  [{3}]' -f '7. token de outro numero', 'recebercoordenadas', $r.Status, $r.Texto))
    Marcar 'celular amarrado ao token' ($r.Status -eq 403) 'token de um numero recusado para outro'
}

# Faxina defensiva: nenhum caso da secao 6 deveria gravar nada. GUARDA: sem
# $antes definido a comparacao -notin seria verdadeira para TODAS as linhas e
# varreria a particao do dia. Foi o que aconteceu em 16/09, por ancora casada
# na primeira ocorrencia de $tokenTeste = $null, no inicio da secao 5.
if ($null -eq $antes) {
    Escrever 'faxina defensiva PULADA: $antes nao definido nesta execucao.'
} else {
    $hojeFax = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $sobrando = @(Get-Linhas $TB_COORD ("PartitionKey eq '" + $hojeFax + "'") | Where-Object { $_.RowKey -notin $antes })
    if ($sobrando.Count -gt 0) {
        Escrever ('ATENCAO: ' + $sobrando.Count + ' registro(s) gravado(s) pelos testes de falha -> apagando')
        foreach ($s in $sobrando) {
            az storage entity delete --account-name $STO --table-name $TB_COORD `
                --partition-key $s.PartitionKey --row-key $s.RowKey --auth-mode key -o none 2>$null
        }
    } else {
        Escrever 'nenhum teste de falha gravou registro (correto)'
    }
}
$tokenTeste = $null
# --------------------------------------------------- 7. semente da cena LGPD
Escrever ''
Escrever '--- 7. SEMENTE DA CENA LGPD ---'
if ($SemSeedLgpd) {
    Escrever 'pulado por -SemSeedLgpd'
    Marcar 'semente LGPD' $false 'pulada por parametro'
} else {
    az storage entity delete --account-name $STO --table-name $TB_COORD `
        --partition-key $PART_LGPD --row-key $ROW_LGPD --auth-mode key -o none 2>$null
    # Somente Celular e DataHoraUtc. Latitude/Longitude ficam de fora de proposito:
    # o tipo teria de bater com double no C#, e a anonimizacao nao precisa deles.
    az storage entity insert --account-name $STO --table-name $TB_COORD --auth-mode key `
        --entity PartitionKey="$PART_LGPD" RowKey="$ROW_LGPD" Celular="$CEL_LGPD" DataHoraUtc="2026-01-01T12:00:00.000Z" -o none 2>$null
    Escrever ('exit code do insert: ' + $LASTEXITCODE)
    $seed = Get-Linhas $TB_COORD ("PartitionKey eq '" + $PART_LGPD + "'")
    Tabela (@($seed) | Select-Object PartitionKey, RowKey, Celular, DataHoraUtc)
    $okSeed = (@($seed | Where-Object { $_.RowKey -eq $ROW_LGPD -and $_.Celular -eq $CEL_LGPD }).Count -eq 1)
    Marcar 'semente LGPD' $okSeed ($PART_LGPD + ' / ' + $ROW_LGPD + ' com celular legivel')
}

# ------------------------------------------------- 8. chave de acesso do gestor
Escrever ''
Escrever '--- 8. CHAVE DE ACESSO DO GESTOR ---'
New-Item -ItemType Directory -Path $PASTA -Force | Out-Null
# Incremento 8A: a porta do relatorio nao e mais a chave de FUNCAO do
# VerRelatorio, e sim o app setting ChaveGestor, digitado numa tela. O filtro
# nomeia a chave exata: filtro por !contains e case-sensitive e foi o que
# vazou a AccountKey no incidente da Secao 8.
$kGestor = az functionapp config appsettings list --name $APP --resource-group $RG `
             --query "[?name=='ChaveGestor'].value | [0]" -o tsv 2>$null
$urlRel = $API + '/verrelatorio'
if ([string]::IsNullOrWhiteSpace($kGestor)) {
    Escrever 'FALHA: app setting ChaveGestor nao lido.'
    Marcar 'chave do gestor' $false 'ChaveGestor nao lida'
} else {
    $txt = @(
        'GpsEquipe - segredos TEMPORARIOS da gravacao',
        ('gerado em: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
        '',
        'Chave de acesso do gestor (digitar na tela de entrada do relatorio):',
        $kGestor,
        '',
        'URL do relatorio (NAO e segredo, pode aparecer na tela):',
        $urlRel,
        '',
        'O segredo TOTP do colaborador esta em totp-<celular>.txt, nesta pasta.',
        'Apague esta pasta com Demo-3-Encerrar.ps1 -Executar.'
    ) -join "`r`n"
    [IO.File]::WriteAllText($ARQ_SEG, $txt, [Text.UTF8Encoding]::new($false))
    Escrever ('arquivo gravado : ' + $ARQ_SEG)
    Escrever ('chave lida      : ' + $kGestor.Length + ' caracteres (valor nunca impresso)')
    Marcar 'chave do gestor' $true ('no arquivo local, ' + $kGestor.Length + ' caracteres')
}

# ------------------------------------------------- 9. atalhos de cena
Escrever ''
Escrever '--- 9. ATALHOS DE CENA ---'
$modelo = @'
# Atalhos da gravacao - gerado por Demo-2-Preparar.ps1. NAO vai para o git.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$SITE      = '__SITE__'
$API       = '__API__'
$ADMIN     = '__ADMIN__'
$RELATORIO = '__RELATORIO__'
$STO = '__STO__'; $RG = '__RG__'; $APP = '__APP__'
$PART_ANTIGA = '__PART__'
$CEL         = '__CEL__'
$TOKEN       = ''

function Post-Coordenada($corpo) {
  try {
    $r = Invoke-WebRequest "$API/recebercoordenadas" -Method Post -ContentType 'application/json' -Body ($corpo | ConvertTo-Json -Compress) -UseBasicParsing
    '{0}  {1}' -f [int]$r.StatusCode, $r.Content
  } catch {
    $resp = $_.Exception.Response
    if ($null -eq $resp) { return ('sem resposta: ' + $_.Exception.Message) }
    $t = [string]$_.ErrorDetails.Message
    if (-not [string]::IsNullOrEmpty($t)) { return ('{0}  {1}' -f [int]$resp.StatusCode, $t) }
    $sr = New-Object IO.StreamReader($resp.GetResponseStream())
    $t = $sr.ReadToEnd(); $sr.Close()
    '{0}  {1}' -f [int]$resp.StatusCode, $t
  }
}

function Iniciar-Sessao($codigo) {
  if (-not $codigo) { $codigo = (Read-Host 'codigo de 6 digitos do Authenticator').Trim() }
  try {
    $r = Invoke-WebRequest "$API/iniciarsessao" -Method Post -ContentType 'application/json' -Body ((@{ Celular = $CEL; Codigo = $codigo } | ConvertTo-Json -Compress)) -UseBasicParsing -TimeoutSec 90
    $j = $r.Content | ConvertFrom-Json
    $global:TOKEN = $j.token
    'sessao aberta: HTTP {0} | token de {1} caracteres | expira {2}' -f [int]$r.StatusCode, $j.token.Length, $j.expiraEmUtc
  } catch {
    $resp = $_.Exception.Response
    $t = [string]$_.ErrorDetails.Message
    if ([string]::IsNullOrEmpty($t) -and $resp) { $sr = New-Object IO.StreamReader($resp.GetResponseStream()) ; $t = $sr.ReadToEnd() ; $sr.Close() }
    'sessao recusada: {0}  {1}' -f $(if ($resp) { [int]$resp.StatusCode } else { 0 }), $t
  }
}

function Enviar-Posicao {
  Post-Coordenada @{ Celular = $CEL; Token = $TOKEN; Latitude = -23.5505; Longitude = -46.6333 }
}

function Ver-Antigos {
  $j = az storage entity query --account-name $STO --table-name Coordenadas --filter "PartitionKey eq '$PART_ANTIGA'" --auth-mode key -o json 2>$null
  @(($j | ConvertFrom-Json).items) | Select-Object PartitionKey, RowKey, Celular, DataHoraUtc | Format-Table -AutoSize
}

function Disparar-Timer {
  $mk = az functionapp keys list --name $APP --resource-group $RG --query masterKey -o tsv 2>$null
  try {
    $r = Invoke-WebRequest "$ADMIN/AnonimizarCoordenadas" -Method Post -Headers @{ 'x-functions-key' = $mk; 'Content-Type' = 'application/json' } -Body '{"input":""}' -UseBasicParsing -TimeoutSec 120
    'disparo aceito: HTTP {0}' -f [int]$r.StatusCode
  } catch { 'disparo falhou: {0}' -f [int]$_.Exception.Response.StatusCode }
  $mk = $null
}

function Ver-LogLgpd {
  az monitor app-insights query --app $APP --resource-group $RG --analytics-query "traces | where timestamp > ago(20m) | where message has 'Anonimizacao LGPD' | project timestamp, message | order by timestamp desc | take 3" --query "tables[0].rows" -o tsv
}

# Incremento 8A: a chave do gestor NAO fica escrita neste arquivo, para nao
# aparecer na tela se ele for aberto na gravacao. Le do Azure na hora.
function Copiar-ChaveGestor {
  $k = az functionapp config appsettings list --name $APP --resource-group $RG --query "[?name=='ChaveGestor'].value | [0]" -o tsv 2>$null
  if ([string]::IsNullOrWhiteSpace($k)) { return 'FALHA: ChaveGestor nao lida.' }
  $k | Set-Clipboard
  $n = $k.Length; $k = $null
  'chave de ' + $n + ' caracteres no clipboard: cole na tela de entrada do relatorio.'
}

function Abrir-Relatorio { Start-Process $RELATORIO }
function Abrir-Site      { Start-Process $SITE }
'@
$modelo = $modelo.Replace('__SITE__', $SITE).
                  Replace('__API__', $API).
                  Replace('__ADMIN__', $ADMIN).
                  Replace('__RELATORIO__', [string]$urlRel).
                  Replace('__STO__', $STO).
                  Replace('__RG__', $RG).
                  Replace('__APP__', $APP).
                  Replace('__PART__', $PART_LGPD).
                  Replace('__CEL__', $CEL)
[IO.File]::WriteAllText($ARQ_CENA, $modelo, [Text.UTF8Encoding]::new($false))
Escrever ('gravado: ' + $ARQ_CENA)
Escrever 'Na gravacao, carregue com:  . C:\demo-gpsequipe\cena.ps1'
Marcar 'atalhos de cena' (Test-Path $ARQ_CENA) 'Iniciar-Sessao, Enviar-Posicao, Post-Coordenada, Ver-Antigos, Disparar-Timer, Ver-LogLgpd, Copiar-ChaveGestor'

# ------------------------------------- 10. aquecimento e verificacao final
Escrever ''
Escrever '--- 10. ENDPOINTS (aquecimento + verificacao) ---'
$rSite = Invoke-Http -Uri $SITE
$sitePin = $rSite.Texto -match 'PIN|Pin'
Escrever ('site                 -> HTTP ' + $rSite.Status + ' (espera 200) | ' + $rSite.Texto.Length + ' bytes | menciona PIN: ' + $sitePin + ' (espera False)')
Marcar 'site do colaborador' (($rSite.Status -eq 200) -and (-not $sitePin)) ($SITE + ' servindo a pagina do autenticador')

# Incremento 8A: sem cookie, o relatorio devolve 401 E a tela de entrada no
# corpo. Os dois importam: 401 sozinho poderia ser pagina de erro do host.
$rSem = Invoke-Http -Uri $urlRel
$temTela = $rSem.Texto -match 'Chave de acesso'
Escrever ('relatorio sem sessao -> HTTP ' + $rSem.Status + ' (espera 401) | tela de entrada no corpo: ' + $temTela)
Marcar 'tela de entrada do gestor' (($rSem.Status -eq 401) -and $temTela) '401 com o formulario de chave'

if (-not [string]::IsNullOrWhiteSpace($kGestor)) {
    # POST da chave: o 303 e seguido automaticamente pelo Invoke-WebRequest, e a
    # sessao fica no cookie da WebSession. O resultado ja e o relatorio.
    $ses = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $okRel = $false; $bytesRel = 0; $temMapa = $false; $stRel = 0
    try {
        $rCom = Invoke-WebRequest -Uri $urlRel -Method Post -Body ('chave=' + $kGestor) `
                  -ContentType 'application/x-www-form-urlencoded' -WebSession $ses `
                  -UseBasicParsing -TimeoutSec 90
        $stRel = [int]$rCom.StatusCode
        $bytesRel = $rCom.Content.Length
        $temMapa = $rCom.Content -match 'leaflet'
        $ck = @($ses.Cookies.GetCookies($urlRel) | Where-Object { $_.Name -eq 'gpsequipe_gestor' })
        Escrever ('entrar + relatorio   -> HTTP ' + $stRel + ' (espera 200) | ' + $bytesRel + ' bytes | leaflet: ' + $temMapa + ' | cookie: ' + $ck.Count)
        $okRel = ($stRel -eq 200) -and $temMapa -and ($ck.Count -eq 1)
    } catch {
        Escrever ('entrar + relatorio   -> FALHOU: ' + $_.Exception.Message)
    }
    Marcar 'relatorio abre com a chave' $okRel ($bytesRel.ToString() + ' bytes de HTML, cookie de sessao emitido')
    $kGestor | Set-Clipboard
    Escrever 'CHAVE DE ACESSO do gestor COPIADA para o clipboard: cole na tela de entrada.'
}
$kGestor = $null

Escrever ''
Escrever '=================================================='
Escrever ' CHECKLIST'
Escrever '=================================================='
Tabela ($estado.Values)
$pendentes = @($estado.Values | Where-Object { $_.situacao -eq 'PENDENTE' }).Count
if ($pendentes -eq 0) {
    Escrever 'PRONTO PARA GRAVAR.'
} else {
    Escrever ('ATENCAO: ' + $pendentes + ' item(ns) PENDENTE(S). Resolva antes de gravar.')
}
Escrever ''
Escrever 'Antes de ligar a camera:'
Escrever '  - abrir o Microsoft Authenticator na entrada GpsEquipe e deixar a mao'
Escrever '  - se o segredo TOTP foi regerado agora, RECADASTRAR a entrada no app'
Escrever '  - abrir o relatorio, colar a chave de acesso e ENTRAR antes de gravar:'
Escrever '    a sessao vale 8h e evita digitar a chave na frente da camera'
Escrever '  - favorito do relatorio pode ser salvo: a URL nao tem mais segredo'
Escrever '  - fechar local.settings.json e qualquer aba com connection string'
Escrever '  - Clear-Host e fonte do terminal em 18 ou mais'
Escrever '  - notificacoes do Windows e do Teams em silencio'
Escrever ''
Escrever 'DEPOIS de gravar: .\Demo-3-Encerrar.ps1  (previa) e depois -Executar'
Escrever 'ATENCAO: o Demo-3 rotaciona a chave de funcao do VerRelatorio, que o'
Escrever '         Incremento 8A tornou irrelevante. A chave a rotacionar agora'
Escrever '         e o app setting ChaveGestor. Corrigir o Demo-3.'
