#Requires -Version 5.1
<#
    Contexto.ps1  --  GpsEquipe v2
    Despeja o estado do projeto para colar no inicio de uma conversa nova.
    Somente LEITURA: nao toca Azure, nao escreve arquivo, nao commita.
    Usa Write-Output, nao Write-Host: assim a saida pode ir para o clipboard
    (.\Contexto.ps1 | Set-Clipboard) ou para arquivo (.\Contexto.ps1 > ctx.txt).
    O Project Knowledge nao guarda mais copia da documentacao, de proposito:
    copia que envelhece mente com autoridade. O repositorio e a fonte unica.

    Uso:  .\Contexto.ps1              # inventario + as 3 ultimas secoes das anotacoes
          .\Contexto.ps1 -Completo    # acrescenta o ModoDeUso e o README inteiros
#>
[CmdletBinding()]
param([switch]$Completo)

$dir = 'C:\repo\GpsEquipe\'
$strict = New-Object Text.UTF8Encoding($false, $true)
function Ler($f) { return ($strict.GetString([IO.File]::ReadAllBytes($dir + $f))) -split "`r?`n" }

Write-Output '===== ARQUIVOS VERSIONADOS ====='
Push-Location $dir
git ls-files | ForEach-Object {
  $f = $dir + $_
  if (Test-Path $f) { '{0,-42} {1,7} bytes' -f $_, (Get-Item $f).Length }
} | Write-Output
Write-Output ''
Write-Output '===== GIT ====='
git log --oneline -8 | Write-Output
git status --short | Write-Output
Write-Output '(vazio acima = arvore limpa)'
Pop-Location

Write-Output ''
Write-Output '===== ANOTACOES-V2: AS 3 ULTIMAS SECOES ====='
$L = Ler 'Anotacoes-v2.txt'
# Indices das linhas de titulo de secao, para cortar a partir da antepenultima.
$titulos = @()
for ($i = 0; $i -lt $L.Count; $i++) {
  if ($L[$i] -match '^\s+(SECAO|SEC' + [char]0x00C7 + [char]0x00C3 + 'O|INCREMENTO)') { $titulos += $i }
}
$de = if ($titulos.Count -ge 3) { $titulos[$titulos.Count - 3] - 1 } else { 0 }
for ($i = [Math]::Max(0, $de); $i -lt $L.Count; $i++) { $L[$i] | Write-Output }

if ($Completo) {
  foreach ($f in 'ModoDeUso-GpsEquipe.md','README.md') {
    Write-Output ''
    Write-Output ('===== ' + $f + ' =====')
    (Ler $f) | Write-Output
  }
}