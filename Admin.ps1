#Requires -Version 5.1
<#
    Admin.ps1  --  GpsEquipe v2
    Um script para toda a operacao administrativa. Substitui os blocos picados
    que o ModoDeUso documentava um por um.

    Uso:
      .\Admin.ps1                             # estado: quem esta cadastrado e o que tem
      .\Admin.ps1 -Habilitar 5511999998888    # cadastra o numero E gera a chave do autenticador
      .\Admin.ps1 -Habilitar 5511999998888 -Forcar   # regera a chave de quem ja tem
      .\Admin.ps1 -Conferir 5511999998888     # testa o codigo que o colaborador esta vendo
      .\Admin.ps1 -Remover 5511999998888      # remove, com confirmacao
      .\Admin.ps1 -ChaveGestor                # poe a chave do gestor no clipboard
      .\Admin.ps1 -TrocarChaveGestor          # gera chave nova, com confirmacao

    REGRAS QUE ESTE SCRIPT SEGUE:
      - Guarda de tenant antes de qualquer operacao. Aborta fora do Azure for
        Students academico, sem tocar em nada.
      - Nenhum segredo vai para o console. Chave e segredo saem pelo clipboard;
        no terminal aparece apenas o tamanho.
      - Operacao destrutiva (-Remover, -TrocarChaveGestor, -Forcar) mostra o que
        sera afetado e exige confirmacao digitada.
      - Todo comando de escrita tem o exit code conferido antes do passo seguinte.

    Somente texto ASCII: PowerShell 5.1 le arquivo sem BOM como ANSI.
#>
[CmdletBinding()]
param(
    [string]$Habilitar,
    [string]$Conferir,
    [string]$Remover,
    [switch]$ChaveGestor,
    [switch]$TrocarChaveGestor,
    [switch]$Forcar
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- nomes vivos (Anotacoes-v2.txt, Secao 2) ----
$RG       = 'GpsEquipe-RG'
$STO      = 'gpsequipebad1'
$APP      = 'GpsEquipe-App-bad1'
$TB_FUNC  = 'FuncionariosPermitidos'
$TENANT   = '38ae2f02-5710-4e12-80bb-83600c3fdf1e'
$USUARIO  = 'alexandre.calzetta@cs.unicid.edu.br'
$API      = 'https://gpsequipe-app-bad1.azurewebsites.net/api'
$PART     = 'FUNCIONARIO'

# Write-HOST, nao Write-Output. Dentro de funcao que RETORNA valor, o
# Write-Output entra no pipeline e vira parte do retorno: Test-Guarda passaria
# a devolver @(mensagem, booleano), e "-not" sobre array de dois itens e FALSO.
# O efeito seria a guarda de tenant e as confirmacoes nunca abortarem nada.
# O Contexto.ps1 usa Write-Output de proposito, porque a saida dele e para
# redirecionar e as funcoes dele nao retornam valor. Aqui nao.
function Escrever($t) { Write-Host $t }

function Test-Guarda {
    $j = az account show -o json 2>$null
    if (-not $j) {
        Escrever 'ABORTADO: az account show nao respondeu.'
        Escrever ("Rode: az login --tenant {0}" -f $TENANT)
        return $false
    }
    $c = $j | ConvertFrom-Json
    if ($c.tenantId -ne $TENANT -or $c.user.name -ne $USUARIO) {
        Escrever ('ABORTADO: conta errada -> ' + $c.name + ' | ' + $c.user.name)
        Escrever ("Corrija: az logout ; az login --tenant {0}" -f $TENANT)
        return $false
    }
    Escrever ('guarda OK -> ' + $c.name)
    return $true
}

# So digitos. Aceita +55 11 98225-3855 e devolve 5511982253855.
function Normalizar($numero) {
    $d = -join ([char[]]$numero | Where-Object { $_ -ge '0' -and $_ -le '9' })
    if ($d.Length -lt 12) { return $null }
    return $d
}

function Mascarar($d) {
    if ($d.Length -lt 8) { return $d }
    return $d.Substring(0,4) + ('*' * ($d.Length - 8)) + $d.Substring($d.Length - 4)
}

# A chave de host fica em variavel local e nunca e impressa.
function Get-ChaveHost {
    $k = az functionapp keys list --name $APP --resource-group $RG --query "functionKeys.default" -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($k)) { return $null }
    return $k
}

# HTTP com leitura do corpo tambem em erro. No PS 5.1 o ErrorDetails vem
# primeiro; ler o stream depois devolve vazio.
function Invoke-Api {
    param([string]$Rota, [string]$Chave, $Corpo)
    $uri = $API + '/' + $Rota + '?code=' + $Chave
    try {
        $r = Invoke-WebRequest -Uri $uri -Method Post -ContentType 'application/json' `
               -Body ($Corpo | ConvertTo-Json -Compress) -UseBasicParsing -TimeoutSec 60
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Texto = [string]$r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return [pscustomobject]@{ Status = 0; Texto = $_.Exception.Message } }
        $txt = [string]$_.ErrorDetails.Message
        if ([string]::IsNullOrEmpty($txt)) {
            try { $sr = New-Object IO.StreamReader($resp.GetResponseStream()); $txt = $sr.ReadToEnd(); $sr.Close() } catch { $txt = '' }
        }
        return [pscustomobject]@{ Status = [int]$resp.StatusCode; Texto = $txt }
    }
}

function Get-Colaboradores {
    $j = az storage entity query --account-name $STO --table-name $TB_FUNC --auth-mode key -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $j) { return $null }
    return @(($j | ConvertFrom-Json).items)
}

function Get-Colaborador($d) {
    $j = az storage entity show --account-name $STO --table-name $TB_FUNC `
           --partition-key $PART --row-key $d --auth-mode key -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $j) { return $null }
    return ($j | ConvertFrom-Json)
}

function Confirmar($pergunta, $esperado) {
    Escrever ''
    Escrever $pergunta
    $r = Read-Host ("Para confirmar, digite exatamente: " + $esperado)
    if ($r.Trim() -ne $esperado) { Escrever 'CANCELADO: confirmacao nao casou. Nada foi alterado.'; return $false }
    return $true
}

# ------------------------------------------------------------------ inicio
Escrever '=================================================='
Escrever ' GpsEquipe :: Admin'
Escrever (' ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Escrever '=================================================='
if (-not (Test-Guarda)) { return }

# ------------------------------------------------------- HABILITAR
if ($Habilitar) {
    $d = Normalizar $Habilitar
    if (-not $d) { Escrever 'ABORTADO: numero invalido. Use 55 + DDD + numero, ao menos 12 digitos.'; return }
    Escrever ''
    Escrever ('--- HABILITAR ' + (Mascarar $d) + ' ---')

    $chave = Get-ChaveHost
    if (-not $chave) { Escrever 'ABORTADO: chave de host nao lida.'; return }

    # Passo 1: autorizar o numero, se ainda nao estiver.
    $e = Get-Colaborador $d
    if ($null -eq $e) {
        Escrever '1. numero nao autorizado; cadastrando'
        az storage entity insert --account-name $STO --table-name $TB_FUNC --auth-mode key `
            --entity PartitionKey="$PART" RowKey="$d" -o none
        if ($LASTEXITCODE -ne 0) { Escrever ('ABORTADO: insert falhou, exit ' + $LASTEXITCODE); $chave = $null; return }
        $e = Get-Colaborador $d
        if ($null -eq $e) { Escrever 'ABORTADO: cadastrei e nao consegui reler a linha.'; $chave = $null; return }
        Escrever '   cadastrado'
    } else {
        Escrever '1. numero ja autorizado'
    }

    # Passo 2: a chave do autenticador. Regerar invalida a anterior e as sessoes.
    $temTotp = -not [string]::IsNullOrEmpty($e.TotpSegredo)
    Escrever ('2. autenticador cadastrado: ' + $temTotp + $(if ($temTotp) { ' | em: ' + $e.TotpDefinidoEm } else { '' }))
    if ($temTotp -and -not $Forcar) {
        Escrever ''
        Escrever 'NADA A FAZER: este colaborador ja esta habilitado.'
        Escrever 'Para gerar uma chave NOVA (a antiga para de funcionar e as sessoes'
        Escrever ('abertas dele morrem), repita com -Forcar:')
        Escrever ('  .\Admin.ps1 -Habilitar ' + $d + ' -Forcar')
        $chave = $null
        return
    }
    if ($temTotp -and $Forcar) {
        if (-not (Confirmar ('Regerar a chave de ' + (Mascarar $d) + ' invalida a atual e derruba as sessoes dele.') $d.Substring($d.Length - 4))) { $chave = $null; return }
    }

    $r = Invoke-Api 'definirtotp' $chave @{ Acao = 'cadastrar'; Celular = $d }
    if ($r.Status -ne 201 -and $r.Status -ne 200) {
        Escrever ('ABORTADO: definirtotp devolveu HTTP ' + $r.Status + ' -> ' + $r.Texto)
        $chave = $null
        return
    }
    $seg = ($r.Texto | ConvertFrom-Json).segredo
    $chave = $null
    if ([string]::IsNullOrWhiteSpace($seg)) { Escrever 'ABORTADO: resposta sem segredo.'; return }

    $seg | Set-Clipboard
    $n = $seg.Length
    $seg = $null
    Escrever ''
    Escrever ('3. CHAVE DE ' + $n + ' CARACTERES NO CLIPBOARD.')
    Escrever ''
    Escrever 'ENTREGUE AGORA, por canal privado. Ela nao sera exibida de novo.'
    Escrever ''
    Escrever 'Diga ao colaborador para cadastrar no Microsoft Authenticator:'
    Escrever '  1. toque no + no canto superior'
    Escrever '  2. Outra conta (Google, Facebook etc.)'
    Escrever '  3. inserir chave manualmente'
    Escrever '  4. Nome da conta: GpsEquipe'
    Escrever '  5. Chave secreta: os caracteres que voce enviou'
    Escrever '  6. apagar a mensagem depois de cadastrar'
    Escrever '  7. data e hora do celular no AUTOMATICO'
    Escrever ''
    Escrever 'Depois, teste com ele:'
    Escrever ('  .\Admin.ps1 -Conferir ' + $d)
    return
}

# ------------------------------------------------------- CONFERIR
if ($Conferir) {
    $d = Normalizar $Conferir
    if (-not $d) { Escrever 'ABORTADO: numero invalido.'; return }
    Escrever ''
    Escrever ('--- CONFERIR ' + (Mascarar $d) + ' ---')
    Escrever 'Peca ao colaborador o codigo que o aplicativo mostra AGORA.'
    Escrever 'Esta conferencia NAO consome o codigo: e diagnostico, nao login.'

    $cod = ''
    for ($i = 1; $i -le 3; $i++) {
        $cod = (Read-Host 'codigo de 6 digitos').Trim()
        Escrever ('   recebi ' + $cod.Length + ' caractere(s)')
        if ($cod -match '^\d{6}$') { break }
        Escrever '   formato invalido: preciso de exatamente 6 digitos.'
        $cod = ''
    }
    if (-not $cod) { Escrever 'ABORTADO: codigo nao informado em 3 tentativas.'; return }

    $chave = Get-ChaveHost
    if (-not $chave) { Escrever 'ABORTADO: chave de host nao lida.'; return }
    $r = Invoke-Api 'definirtotp' $chave @{ Acao = 'conferir'; Celular = $d; Codigo = $cod }
    $chave = $null; $cod = $null
    Escrever ''
    Escrever ('HTTP ' + $r.Status + ' -> ' + $r.Texto)
    if ($r.Status -eq 200) {
        Escrever ''
        Escrever 'Leia o desvio de janela na resposta:'
        Escrever '  0            relogios sincronizados'
        Escrever '  diferente    relogio do celular desajustado; funciona, mas a margem fica curta'
    } else {
        Escrever ''
        Escrever 'RECUSADO. Causas possiveis: codigo errado, codigo ja trocado enquanto'
        Escrever 'voce digitava, relogio do celular desajustado, ou chave cadastrada no'
        Escrever 'aparelho diferente da que esta no sistema.'
    }
    return
}

# ------------------------------------------------------- REMOVER
if ($Remover) {
    $d = Normalizar $Remover
    if (-not $d) { Escrever 'ABORTADO: numero invalido.'; return }
    Escrever ''
    Escrever ('--- REMOVER ' + (Mascarar $d) + ' ---')
    $e = Get-Colaborador $d
    if ($null -eq $e) { Escrever 'NADA A FAZER: este numero nao esta cadastrado.'; return }
    Escrever ('autenticador cadastrado: ' + (-not [string]::IsNullOrEmpty($e.TotpSegredo)) + ' | em: ' + $e.TotpDefinidoEm)
    Escrever ''
    Escrever 'Sera removida a autorizacao E a chave do autenticador.'
    Escrever 'As coordenadas JA ENVIADAS permanecem na base.'
    if (-not (Confirmar 'Confirma a remocao?' $d.Substring($d.Length - 4))) { return }

    az storage entity delete --account-name $STO --table-name $TB_FUNC `
        --partition-key $PART --row-key $d --auth-mode key -o none
    if ($LASTEXITCODE -ne 0) { Escrever ('FALHA: exit ' + $LASTEXITCODE); return }
    $depois = Get-Colaborador $d
    Escrever ''
    Escrever ('removido: ' + ($null -eq $depois) + ' (espera True)')
    return
}

# ------------------------------------------------------- CHAVE DO GESTOR
if ($ChaveGestor) {
    Escrever ''
    Escrever '--- CHAVE DE ACESSO DO GESTOR ---'
    $k = az functionapp config appsettings list --name $APP --resource-group $RG `
           --query "[?name=='ChaveGestor'].value | [0]" -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($k)) { Escrever 'ABORTADO: ChaveGestor nao lida.'; return }
    $k | Set-Clipboard
    $n = $k.Length
    $k = $null
    Escrever ('chave de ' + $n + ' caracteres no clipboard (espera 43).')
    Escrever ''
    Escrever 'Entregue por canal privado. Ela NAO identifica quem a usa: qualquer'
    Escrever 'pessoa que a tenha ve a localizacao de todos os colaboradores.'
    Escrever ''
    Escrever 'O gestor cola essa chave na tela de entrada de:'
    Escrever ('  ' + $API + '/verrelatorio')
    return
}

# ------------------------------------------------------- TROCAR A CHAVE DO GESTOR
if ($TrocarChaveGestor) {
    Escrever ''
    Escrever '--- TROCAR A CHAVE DE ACESSO DO GESTOR ---'
    Escrever 'Uma unica chave serve a TODOS os gestores: trocar obriga todos a'
    Escrever 'receberem a nova.'
    Escrever 'Sessoes ja abertas continuam valendo ate vencer, no maximo 8 horas.'
    Escrever 'Gravar app setting REINICIA a aplicacao: alguns segundos fora do ar.'
    if (-not (Confirmar 'Confirma a troca?' 'TROCAR')) { return }

    $b = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($b); $rng.Dispose()
    $v = [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_')
    $b = $null
    az functionapp config appsettings set --name $APP --resource-group $RG `
        --settings "ChaveGestor=$v" -o none
    if ($LASTEXITCODE -ne 0) { Escrever ('FALHA ao gravar: exit ' + $LASTEXITCODE); $v = $null; return }

    $conf = az functionapp config appsettings list --name $APP --resource-group $RG `
              --query "[?name=='ChaveGestor'].value | [0]" -o tsv 2>$null
    $igual = ($conf -eq $v)
    $n = $v.Length
    $v | Set-Clipboard
    $v = $null; $conf = $null
    Escrever ''
    Escrever ('gravada: ' + $n + ' caracteres | confere com a gerada: ' + $igual)
    Escrever 'CHAVE NOVA NO CLIPBOARD. Entregue aos gestores antes de usar o clipboard'
    Escrever 'para outra coisa.'
    return
}

# ------------------------------------------------------- ESTADO (padrao)
Escrever ''
Escrever '--- COLABORADORES ---'
$todos = Get-Colaboradores
if ($null -eq $todos) { Escrever 'FALHA ao consultar a tabela.'; return }
if (@($todos).Count -eq 0) {
    Escrever 'nenhum colaborador cadastrado: o sistema nao aceita envio de ninguem.'
} else {
    $lista = @($todos) | ForEach-Object {
        [pscustomobject]@{
            celular      = Mascarar ([string]$_.RowKey)
            autenticador = $(if ([string]::IsNullOrEmpty($_.TotpSegredo)) { 'NAO' } else { 'sim' })
            cadastrado   = $_.TotpDefinidoEm
        }
    }
    ($lista | Format-Table -AutoSize | Out-String).TrimEnd() | Write-Output
    $sem = @($lista | Where-Object { $_.autenticador -eq 'NAO' }).Count
    Escrever ''
    Escrever ('total: ' + @($lista).Count + ' | sem autenticador: ' + $sem)
    if ($sem -gt 0) {
        Escrever ''
        Escrever 'ATENCAO: quem esta sem autenticador recebe 403 ao tentar enviar.'
        Escrever 'Habilite com:  .\Admin.ps1 -Habilitar <numero>'
    }
}

Escrever ''
Escrever '--- O QUE ESTE SCRIPT FAZ ---'
Escrever '  .\Admin.ps1 -Habilitar <numero>           cadastra e gera a chave do autenticador'
Escrever '  .\Admin.ps1 -Habilitar <numero> -Forcar   regera a chave de quem ja tem'
Escrever '  .\Admin.ps1 -Conferir  <numero>           testa o codigo que o colaborador ve'
Escrever '  .\Admin.ps1 -Remover   <numero>           remove, com confirmacao'
Escrever '  .\Admin.ps1 -ChaveGestor                  chave do gestor no clipboard'
Escrever '  .\Admin.ps1 -TrocarChaveGestor            gera chave nova, com confirmacao'
Escrever ''
Escrever 'O painel no navegador mostra o mesmo estado, mais coordenadas e LGPD:'
Escrever '  https://gpsequipebad1.z15.web.core.windows.net/admin.html'
