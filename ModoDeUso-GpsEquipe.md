# GpsEquipe — Modo de uso

Sistema de rastreamento de colaboradores em campo
Diretoria de Ensino Centro Oeste - SEDUC/SP

Atualizado em 13/09/2026 — Incremento 5 (identificação por PIN).

---

## Quem usa o quê

| Perfil | O que faz | Onde |
| --- | --- | --- |
| Colaborador | envia a própria localização | site no navegador do celular |
| Gestor | acompanha o mapa e o histórico | relatório no navegador, com chave |
| Administrador | cadastra colaboradores e define PINs | Azure CLI e chamada HTTP |

---

## Colaborador — enviar localização

**Endereço:** https://gpsequipebad1.z15.web.core.windows.net/

1. Abra o endereço no navegador do celular.
2. Digite o seu número no campo, com código do país e DDD.
   Exemplo: `+5511982253855`
3. Digite o seu **PIN de 6 dígitos**, fornecido pelo gestor.
4. Toque em **Iniciar rastreamento**.
5. Autorize o acesso à localização quando o navegador pedir.
6. **Deixe a página aberta.** O envio acontece a cada 10 segundos, sozinho.
7. Ao terminar o expediente, toque em **Parar**.

O campo do PIN é apagado da tela no momento em que o rastreamento começa. O PIN
fica apenas na memória da página, durante a sessão. Ao tocar em **Parar** ele é
descartado: para retomar o rastreamento é preciso digitá-lo de novo.

### O que aparece na tela

| Mensagem | Significado |
| --- | --- |
| Coordenada recebida com sucesso. | envio funcionou |
| Envios: N \| ultimo as HH:MM:SS | contador de envios da sessão |
| Informe o PIN de 6 digitos. | o campo do PIN está vazio ou fora do formato; nada foi enviado |
| Identificacao invalida. Procure o gestor. | número não autorizado, PIN não definido ou PIN errado; o rastreio para |
| GPS indisponivel | o aparelho não conseguiu obter a posição |
| Sem conexao com a API | falha de rede; o sistema tenta de novo no próximo ciclo |

A mensagem de identificação inválida é **a mesma** para os três motivos, de
propósito. Mensagens distintas permitiriam a qualquer pessoa descobrir, número
por número, quem é colaborador da Diretoria. O motivo real fica no log, acessível
só ao administrador.

### Cuidados

- O número precisa estar cadastrado **e** ter PIN definido antes do primeiro uso.
  Sem uma das duas coisas, o sistema recusa o envio.
- Não compartilhe o PIN. Ele é o que garante que a coordenada gravada em seu nome
  foi enviada por você.
- A página precisa ficar aberta e visível. Se o navegador for fechado ou o
  celular bloquear a tela por muito tempo, o envio pode ser interrompido.
- Dentro de prédios o GPS perde precisão. Ao ar livre a posição é mais exata.
- O envio consome dados móveis, em volume muito baixo — cada coordenada tem
  poucas centenas de bytes.

### Como seus dados são tratados

O site traz um aviso recolhido, **Como seus dados são tratados**, com o que é
coletado, a finalidade, a base legal, quem acessa, o prazo de retenção e o
tratamento dado ao PIN. Leia antes do primeiro uso.

---

## Gestor — acompanhar a equipe

**Endereço:** https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio?code=CHAVE

Substitua `CHAVE` pela chave de acesso fornecida pelo administrador. Sem ela a
página responde erro 401.

Guarde o endereço completo nos favoritos do navegador. A chave faz parte da
URL.

### O que a página mostra

**Mapa**, no topo. Cada colaborador recebe uma cor. Os pontos são as
coordenadas recebidas, ligados por uma linha na ordem em que chegaram. O ponto
maior e mais destacado é a posição mais recente de cada pessoa.

Toque ou clique em qualquer ponto para ver colaborador, data, hora e
coordenadas.

**Legenda**, abaixo do mapa, com o número de pontos de cada colaborador.

**Tabela**, ao final, com data, hora, latitude e longitude de cada envio, do
mais recente para o mais antigo, agrupada por colaborador.

### Filtrar por período

Os campos **De** e **Até**, no alto da página, definem o intervalo.

1. Escolha as duas datas.
2. Clique em **Filtrar**.

Regras do filtro:

- As duas datas entram no resultado. De 10/09 a 12/09 traz os três dias.
- Sem filtro, a página mostra o dia corrente.
- O link **hoje** volta ao dia corrente.
- O limite é 31 dias por consulta. Intervalos maiores são cortados e a página
  avisa.
- Datas invertidas são corrigidas automaticamente, com aviso.
- Data inválida é ignorada e a página usa o dia corrente, com aviso.

O endereço muda junto com o filtro, então um período específico pode ser
copiado e compartilhado como link.

### A página não atualiza sozinha

Recarregue manualmente para ver novos pontos. O recarregamento automático foi
retirado de propósito: ele apagaria o filtro de período escolhido.

---

## Administrador — cadastrar colaborador

O número entra apenas com dígitos, sem o sinal de `+` e sem espaços.
`+55 11 98225-3855` vira `5511982253855`.

```powershell
az storage entity insert --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --entity PartitionKey="FUNCIONARIO" RowKey="5511982253855"
```

O cadastro sozinho **não** habilita o envio. É preciso definir o PIN em seguida.

### Definir o PIN de um colaborador

O PIN nunca deve ser digitado no console: a máquina tem transcript do PowerShell
ativo. O bloco abaixo gera o PIN, envia e devolve o valor pelo clipboard.

```powershell
& {
  $celular = '5511982253855'
  $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
  $pin = ''; $t = 0
  do {
    $t++
    $bytes = New-Object byte[] 64; $rng.GetBytes($bytes)
    $d = @(); foreach ($b in $bytes) { if ($b -lt 250 -and $d.Count -lt 6) { $d += ($b % 10) } }
    if ($d.Count -eq 6) { $pin = -join $d }
    $valido = ($pin.Length -eq 6) -and ((($pin.ToCharArray() | Select-Object -Unique).Count) -gt 1)
  } until ($valido -or $t -ge 10)
  $rng.Dispose()
  if (-not $valido) { Write-Output "ABORTADO: PIN nao gerado."; return }

  $chave = az functionapp function keys list --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --function-name DefinirPin --query default -o tsv
  $corpo = @{ Celular = $celular; Pin = $pin } | ConvertTo-Json -Compress
  try {
    $r = Invoke-WebRequest -Uri "https://gpsequipe-app-bad1.azurewebsites.net/api/definirpin?code=$chave" `
      -Method Post -ContentType 'application/json' -Body $corpo -UseBasicParsing -TimeoutSec 60
    Write-Output ("HTTP {0} -> {1}" -f [int]$r.StatusCode, $r.Content)
    if ([int]$r.StatusCode -eq 200) { $pin | Set-Clipboard; Write-Output "PIN no clipboard. Cole em local seguro AGORA." }
  } catch { Write-Output ("FALHA -> {0}" -f $_.Exception.Message) }
  $pin = $null; $chave = $null; $corpo = $null
}
```

Entregue o PIN ao colaborador por canal privado e não o guarde em planilha. O
sistema não permite recuperá-lo: só redefinir, rodando o bloco de novo.

Regras que o servidor aplica:

- exatamente 6 dígitos;
- PIN com todos os dígitos iguais é recusado;
- celular precisa existir em `FuncionariosPermitidos`, senão a resposta é 404.

### Ver quem está cadastrado

```powershell
az storage entity query --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos --query "items[].RowKey" -o tsv --auth-mode key
```

### Conferir se um colaborador já tem PIN

Sem expor o hash:

```powershell
& {
  $e = az storage entity show --account-name gpsequipebad1 --table-name FuncionariosPermitidos `
    --partition-key FUNCIONARIO --row-key 5511982253855 --auth-mode key -o json | ConvertFrom-Json
  Write-Output ("PIN definido: {0} | em: {1}" -f [bool]$e.PinHash, $e.PinDefinidoEm)
}
```

### Remover um colaborador

Remove a autorização e o PIN junto. As coordenadas já enviadas permanecem.

```powershell
az storage entity delete --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --partition-key "FUNCIONARIO" --row-key "5511982253855"
```

### Obter a chave do relatório

```powershell
az functionapp function keys list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --function-name VerRelatorio `
  --query default -o tsv | Set-Clipboard
```

Entregue a chave apenas a quem deve ver o relatório. Ela não identifica quem a
usa: qualquer pessoa que a tenha acessa os dados de todos os colaboradores.

---

## Retenção de dados

Coordenadas com mais de 90 dias têm o número do celular substituído por
`ANONIMIZADO`, todos os dias às 00h30 de Brasília.

A troca é irreversível. Latitude, longitude e horário permanecem; apenas o
vínculo com a pessoa é removido.

Consequência para o gestor: relatórios de períodos com mais de 90 dias mostram
trajetos sem identificação de quem os percorreu.

O PIN não é armazenado em nenhum momento. O sistema guarda apenas um valor
derivado dele, do qual não se recupera o PIN original.

---

## Problemas comuns

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| Site mostra "Informe o PIN de 6 digitos" | campo do PIN vazio ou com menos de 6 dígitos | digitar o PIN completo |
| Site mostra "Identificacao invalida" | número não autorizado, sem PIN definido, ou PIN errado | conferir o número; pedir cadastro ou novo PIN ao gestor |
| Colaborador recém-cadastrado não consegue enviar | cadastro feito, PIN não definido | rodar o bloco de definir PIN |
| Relatório devolve 401 | chave ausente ou incorreta na URL | usar o endereço completo com `?code=` |
| Relatório vazio | não há envios no período filtrado | clicar em **hoje** ou ampliar o intervalo |
| Mapa em branco, tabela preenchida | falha ao carregar a biblioteca do mapa | recarregar a página; verificar a conexão |
| Primeiro acesso muito lento | o serviço estava inativo e precisa iniciar | aguardar alguns segundos e repetir |
| Pontos empilhados no mesmo lugar | colaborador parado | normal; o trajeto aparece com deslocamento |

---

## Limitações conhecidas

O PIN é fator único e de 6 dígitos. Ele impede que alguém envie coordenada em
nome de um colega apenas conhecendo o número dele — limitação que existia até o
Incremento 5 —, mas não é autenticação forte.

Em detalhe:

- Não há bloqueio por tentativas erradas. Seis dígitos são um milhão de
  combinações, e o endereço de envio é público. O custo de cálculo do hash
  encarece cada tentativa, não a impede.
- O PIN é conferido a cada envio, em vez de uma vez por sessão com emissão de
  token. Funciona, mas não é o desenho correto.
- A chave do relatório não identifica quem a usa.

Autenticação de identidade plena — login corporativo, validação por SMS ou token
pessoal — está fora do escopo desta versão.
