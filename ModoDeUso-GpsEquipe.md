# GpsEquipe — Modo de uso

Sistema de rastreamento de colaboradores em campo
Diretoria de Ensino Centro Oeste - SEDUC/SP

Atualizado em 16/09/2026 — Incremento 8A (acesso do gestor por tela) e
Incremento 8B (PIN extirpado).

---

## O que mudou nesta versão

Duas mudanças alteram o modo de usar o sistema.

**O PIN não existe mais.** O site antigo, que pedia um PIN de 6 dígitos, foi
desligado. O endereço dele continua funcionando, mas agora serve a página do
autenticador. Quem tentar enviar posição sem o código do aplicativo recebe
erro, e o campo de PIN não é mais lido pelo servidor.

**O gestor não usa mais chave na URL.** O relatório passou a ter uma tela de
entrada. A chave é digitada num campo, não colada no endereço. Favoritos antigos
com `?code=...` continuam abrindo, mas mostram a tela de entrada em vez do
relatório.

---

## Quem usa o quê

| Perfil | O que faz | Onde |
| --- | --- | --- |
| Colaborador | envia a própria localização | site no navegador do celular |
| Gestor | acompanha o mapa e o histórico | relatório no navegador, com tela de entrada |
| Administrador | cadastra colaboradores, define identificação, vê o status | painel no navegador e Azure CLI |

---

## Endereços do sistema

| O quê | Endereço |
| --- | --- |
| Site do colaborador | https://gpsequipebad1.z15.web.core.windows.net/ |
| Painel administrativo | https://gpsequipebad1.z15.web.core.windows.net/admin.html |
| Relatório do gestor | https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio |

O endereço `index-totp.html` continua respondendo e serve exatamente a mesma
página da raiz. Use a raiz: é mais curta e é a que está na documentação.

As três páginas são públicas no endereço, mas nenhuma mostra dado nenhum sem
identificação. O painel exige a chave de host; o relatório exige a chave de
acesso do gestor; o site do colaborador exige o código do aplicativo
autenticador.

---

## Colaborador — enviar localização

**Endereço:** https://gpsequipebad1.z15.web.core.windows.net/

### Antes do primeiro uso, uma vez só

O administrador entrega a você uma **chave de 32 caracteres**. Cadastre-a no
**Microsoft Authenticator**:

1. Instale o Microsoft Authenticator, se ainda não tiver.
2. Toque no **+** no canto superior.
3. Escolha **Outra conta (Google, Facebook etc.)**.
4. A câmera abre para ler QR code. Toque em **inserir chave manualmente**,
   embaixo.
5. Nome da conta: `GpsEquipe`.
6. Chave secreta: os 32 caracteres que o administrador passou.
7. Concluir.

Deve aparecer `GpsEquipe` na lista, com um código de 6 dígitos e um contador
girando. **Apague a mensagem com a chave depois de cadastrar.**

O celular precisa estar com **data e hora automáticas**. O código depende do
relógio: aparelho desajustado gera códigos que o servidor recusa.

### Todos os dias

1. Abra o endereço no navegador do celular.
2. Em **Seu celular, com DDD**, digite o número com código do país e DDD.
   Exemplo: `+5511982253855`
3. Abra o Microsoft Authenticator e digite, em **Codigo do Microsoft
   Authenticator**, o código de 6 dígitos da entrada `GpsEquipe`.
4. Toque em **Iniciar rastreamento**.
5. Autorize o acesso à localização quando o navegador pedir.
6. **Deixe a página aberta.** O envio acontece a cada 10 segundos, sozinho.
7. Ao terminar o expediente, toque em **Parar**.

O código é pedido **uma vez por sessão**, não a cada envio. Depois de validado,
a página recebe uma autorização temporária que vale **8 horas** e cobre a
jornada inteira.

### Regras do código

- O código muda a cada **30 segundos**.
- Cada código serve **uma única vez**. Se você errar o número ou o código,
  **espere o código trocar** antes de tentar de novo: repetir o mesmo código é
  recusado, mesmo estando correto.
- A autorização fica só na memória da página. Ao tocar em **Parar**, ou ao
  fechar o navegador, ela é descartada e é preciso um código novo.
- Depois de 8 horas a autorização vence sozinha, mesmo com a página aberta.
- Não existe mais caminho alternativo. Sem o código do aplicativo, o sistema
  não aceita envio.

### O que aparece na tela

| Mensagem | Significado |
| --- | --- |
| Sessao valida ate HH:MM:SS (8h) | código aceito, rastreamento autorizado |
| Coordenada recebida com sucesso. | envio funcionou |
| Envios: N \| ultimo as HH:MM:SS | contador de envios da sessão |
| Informe o codigo de 6 digitos do Authenticator. | campo vazio ou fora do formato; nada foi enviado |
| Identificacao invalida. Espere o codigo trocar e tente de novo. | número não autorizado, sem identificação cadastrada, código errado ou código já usado |
| GPS indisponivel | o aparelho não conseguiu obter a posição |
| Sem conexao com a API | falha de rede; o sistema tenta de novo no próximo ciclo |

A mensagem de identificação inválida é **a mesma** para todos os motivos, de
propósito. Mensagens distintas permitiriam a qualquer pessoa descobrir, número
por número, quem é colaborador da Diretoria. O motivo real fica no log,
acessível só ao administrador.

### Cuidados

- O número precisa estar cadastrado **e** ter a chave do autenticador
  cadastrada antes do primeiro uso. Sem uma das duas coisas, o sistema recusa
  o envio.
- Não mostre a tela do Authenticator a ninguém e não repasse a chave de 32
  caracteres. Ela é o que garante que a coordenada gravada em seu nome foi
  enviada por você.
- A página precisa ficar aberta e visível. Se o navegador for fechado ou o
  celular bloquear a tela por muito tempo, o envio pode ser interrompido.
- Dentro de prédios o GPS perde precisão. Ao ar livre a posição é mais exata.
- O envio consome dados móveis, em volume muito baixo — cada coordenada tem
  poucas centenas de bytes.
- Trocou de celular? A chave precisa ser cadastrada de novo no aparelho novo.
  Se você não a tiver mais, o administrador gera outra — e a antiga deixa de
  funcionar.

### Como seus dados são tratados

O site traz um aviso recolhido, **Como seus dados sao tratados**, com o que é
coletado, a finalidade, a base legal, quem acessa, o prazo de retenção e o
tratamento dado à identificação. Leia antes do primeiro uso.

---

## Gestor — acompanhar a equipe

**Endereço:** https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio

Guarde este endereço nos favoritos. Ele não contém segredo nenhum: pode ser
compartilhado, anotado ou mostrado em tela sem risco.

### Entrar

1. Abra o endereço.
2. A tela **Relatorio de rastreamento. Acesso restrito.** aparece, com o campo
   **Chave de acesso**.
3. Cole a chave que o administrador forneceu e clique em **Entrar**.

A chave tem 43 caracteres. O campo é do tipo senha: os caracteres não aparecem
na tela, e a chave não vai para a barra de endereço nem para o histórico do
navegador.

### A sessão

Depois de entrar, o navegador guarda uma autorização que vale **8 horas**. Nesse
período você navega no relatório, filtra períodos e recarrega a página sem
digitar a chave de novo.

Passadas as 8 horas, ou ao limpar os dados do navegador, a tela de entrada volta
a aparecer.

A autorização fica num cookie que o JavaScript da página não consegue ler, e que
só é enviado para o endereço do relatório. Ela não identifica você: identifica
apenas que alguém apresentou a chave correta.

### O que a página mostra

**Mapa**, no topo. Cada colaborador recebe uma cor. Os pontos são as coordenadas
recebidas, ligados por uma linha na ordem em que chegaram. O ponto maior e mais
destacado é a posição mais recente de cada pessoa.

Toque ou clique em qualquer ponto para ver colaborador, data, hora e
coordenadas.

**Legenda**, abaixo do mapa, com o número de pontos de cada colaborador.

**Tabela**, ao final, com data, hora, latitude e longitude de cada envio, do mais
recente para o mais antigo, agrupada por colaborador.

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

O endereço muda junto com o filtro, então um período específico pode ser copiado
e compartilhado como link. Quem receber esse link precisa da chave de acesso
para ver o conteúdo.

As datas são sempre do **horário de Brasília**. Envio feito às 22h de um dia
aparece no dia em que foi feito, não no seguinte.

### A página não atualiza sozinha

Recarregue manualmente para ver novos pontos. O recarregamento automático foi
retirado de propósito: ele apagaria o filtro de período escolhido.

---

## Administrador — painel no navegador

**Endereço:** https://gpsequipebad1.z15.web.core.windows.net/admin.html

O painel pede uma **chave de acesso**. Use a **chave de host** da aplicação, que
atende todas as operações administrativas. A chave do gestor **não** serve aqui,
e a chave de host **não** serve no relatório: são segredos diferentes, para
finalidades diferentes.

Obter a chave de host:

```powershell
az functionapp keys list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv | Set-Clipboard
```

A chave fica apenas na memória da aba e desaparece ao fechar a página. Não é
gravada no navegador.

### O que o painel mostra

**Alertas**, no topo, derivados do estado — não são só números. Exemplos:
colaborador cadastrado sem autenticador, ou nenhum envio nas últimas 24 horas.

**Colaboradores**: total, quantos têm autenticador cadastrado, quantos não têm,
e a lista com o celular mascarado (`5511*****3855`) e a data em que a chave do
autenticador foi cadastrada.

**Coordenadas recebidas**: total dos últimos 7 dias, quantidade por dia e a
última posição registrada.

**Retenção e LGPD**: prazo de retenção, data-limite da anonimização, quantos
registros já foram anonimizados, o agendamento e a próxima execução.

**Servidor**: hora de Brasília, hora UTC e a versão do runtime.

O celular aparece sempre mascarado. O painel precisa identificar a linha, não
precisa do número inteiro.

### O que o painel faz

No cartão **Gestao de colaboradores**, com o número em dígitos:

| Botão | O que faz |
| --- | --- |
| **Cadastrar** | autoriza o número. Não cadastra o autenticador: o colaborador ainda não consegue enviar |
| **Remover** | retira a autorização, com confirmação. As coordenadas já enviadas permanecem |

⚠️ O painel **não** cadastra a chave do Microsoft Authenticator, de propósito.
Essa chave é exibida uma única vez, e tela de painel é o pior lugar para exibir
segredo de uso único. O cadastro é por bloco PowerShell, na seção seguinte.

---

## Administrador — cadastrar a identificação por TOTP

Este é o passo que habilita o colaborador. O bloco gera a chave de 32
caracteres, grava no sistema e a mostra para você repassar.

```powershell
& {
  $celular = '5511982253855'

  $chave = az functionapp keys list --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv
  $corpo = @{ Acao = 'cadastrar'; Celular = $celular } | ConvertTo-Json -Compress
  try {
    $r = Invoke-WebRequest -Uri "https://gpsequipe-app-bad1.azurewebsites.net/api/definirtotp?code=$chave" `
      -Method Post -ContentType 'application/json' -Body $corpo -UseBasicParsing -TimeoutSec 60
    $j = $r.Content | ConvertFrom-Json
    Write-Output ("HTTP {0} -> chave de {1} caracteres gerada" -f [int]$r.StatusCode, $j.segredo.Length)
    $j.segredo | Set-Clipboard
    Write-Output "Chave no clipboard. Repasse ao colaborador AGORA, por canal privado."
    Write-Output "Ela nao sera exibida de novo."
  } catch {
    $resp = $_.Exception.Response
    $t = [string]$_.ErrorDetails.Message
    if ([string]::IsNullOrEmpty($t) -and $resp) {
      $sr = New-Object IO.StreamReader($resp.GetResponseStream()); $t = $sr.ReadToEnd(); $sr.Close()
    }
    Write-Output ("FALHA HTTP {0} -> {1}" -f $(if ($resp) { [int]$resp.StatusCode } else { 0 }), $t)
  }
  $chave = $null; $corpo = $null; $j = $null
}
```

Regras que o servidor aplica:

- o celular precisa existir em `FuncionariosPermitidos`, senão a resposta é 404;
- cada chamada gera uma chave **nova** e invalida a anterior, junto com qualquer
  sessão aberta daquele colaborador;
- a chave é exibida uma única vez. Perdida, só resta gerar outra.

### Conferir se a chave do colaborador funciona

Peça a ele o código que o aplicativo mostra **naquele momento** e confira:

```powershell
& {
  $celular = '5511982253855'
  $codigo  = Read-Host 'codigo de 6 digitos que o colaborador esta vendo'

  $chave = az functionapp keys list --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv
  $corpo = @{ Acao = 'conferir'; Celular = $celular; Codigo = $codigo.Trim() } | ConvertTo-Json -Compress
  try {
    $r = Invoke-WebRequest -Uri "https://gpsequipe-app-bad1.azurewebsites.net/api/definirtotp?code=$chave" `
      -Method Post -ContentType 'application/json' -Body $corpo -UseBasicParsing -TimeoutSec 60
    Write-Output ("HTTP {0} -> {1}" -f [int]$r.StatusCode, $r.Content)
  } catch {
    Write-Output ("RECUSADO HTTP {0}" -f $(if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }))
  }
  $chave = $null; $codigo = $null
}
```

A resposta traz o **desvio de janela**. `0` significa relógios sincronizados.
Valores diferentes de zero indicam relógio do celular desajustado — ainda
funciona, mas a margem fica curta.

Esta conferência **não** consome o código: ela é diagnóstico, não login.

---

## Administrador — a chave de acesso do gestor

A chave que o gestor digita na tela é um app setting da aplicação, chamado
`ChaveGestor`. Ela tem 43 caracteres.

### Obter a chave, para entregar ao gestor

```powershell
az functionapp config appsettings list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG `
  --query "[?name=='ChaveGestor'].value | [0]" -o tsv | Set-Clipboard
```

Entregue por canal privado. A chave não identifica quem a usa: qualquer pessoa
que a tenha vê a localização de todos os colaboradores.

### Trocar a chave

Uma única chave serve a todos os gestores, então trocá-la obriga todos a
receberem a nova. As sessões abertas continuam valendo até vencer.

```powershell
& {
  $c = az account show -o json | ConvertFrom-Json
  if ($c.tenantId -ne '38ae2f02-5710-4e12-80bb-83600c3fdf1e') { Write-Output "ABORTADO"; return }
  $b = New-Object byte[] 32
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($b); $rng.Dispose()
  $v = [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_')
  az functionapp config appsettings set --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --settings "ChaveGestor=$v" -o none
  if ($LASTEXITCODE -eq 0) { $v | Set-Clipboard; Write-Output "Chave nova no clipboard. Entregue aos gestores." }
  else { Write-Output "FALHA ao gravar." }
  $v = $null; $b = $null
}
```

Gravar app setting reinicia a aplicação: alguns segundos de indisponibilidade.

---

## Administrador — outras operações

### Cadastrar colaborador pela linha de comando

Alternativa ao botão do painel. O número entra apenas com dígitos, sem o sinal
de `+` e sem espaços. `+55 11 98225-3855` vira `5511982253855`.

```powershell
az storage entity insert --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --entity PartitionKey="FUNCIONARIO" RowKey="5511982253855" --auth-mode key
```

O cadastro sozinho **não** habilita o envio. É preciso cadastrar a chave do
autenticador em seguida.

### Ver quem está cadastrado

```powershell
az storage entity query --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos --query "items[].RowKey" -o tsv --auth-mode key
```

### Conferir o que um colaborador já tem definido

Sem expor a chave:

```powershell
& {
  $e = az storage entity show --account-name gpsequipebad1 --table-name FuncionariosPermitidos `
    --partition-key FUNCIONARIO --row-key 5511982253855 --auth-mode key -o json | ConvertFrom-Json
  Write-Output ("autenticador cadastrado: {0} | em: {1}" -f (-not [string]::IsNullOrEmpty($e.TotpSegredo)), $e.TotpDefinidoEm)
}
```

O painel mostra o mesmo para todos os colaboradores de uma vez, na lista de
colaboradores.

### Remover um colaborador

Pelo painel, com o botão **Remover**, ou por comando. Remove a autorização e a
chave do autenticador juntas. As coordenadas já enviadas permanecem.

```powershell
az storage entity delete --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --partition-key "FUNCIONARIO" --row-key "5511982253855" --auth-mode key
```

---

## As três chaves não se confundem

| Chave | Para quem | Abre | Onde é usada |
| --- | --- | --- | --- |
| **de acesso do gestor** (`ChaveGestor`, 43 caracteres) | gestor | só o relatório | digitada na tela de entrada |
| **de host** (56 caracteres) | administrador | painel, status, cadastro, remoção e TOTP | colada no campo do painel |
| **de função** do `VerStatus` (56 caracteres) | ninguém, na prática | só a leitura de status | não usada pelo painel |

A chave de host e a de função têm o mesmo tamanho e se parecem. Usar a de função
no painel carrega o status e recusa todas as operações com erro 401.

A chave de host **não abre mais o relatório**. Antes do Incremento 8A ela abria,
o que significava que quem administrava também via a localização da equipe.
Agora as duas funções estão separadas por segredos distintos.

---

## Retenção de dados

Coordenadas com mais de 90 dias têm o número do celular substituído por
`ANONIMIZADO`, todos os dias às 00h30 de Brasília.

A troca é irreversível. Latitude, longitude e horário permanecem; apenas o
vínculo com a pessoa é removido.

Consequência para o gestor: relatórios de períodos com mais de 90 dias mostram
trajetos sem identificação de quem os percorreu.

A chave do Microsoft Authenticator **é** armazenada — o servidor precisa dela
para recalcular o código a cada 30 segundos. Está entre as limitações
conhecidas, no fim deste documento.

Os dados que o PIN deixava na base — o valor derivado dele e a data em que foi
definido — foram **eliminados** no Incremento 8B, não apenas ignorados pelo
código. A finalidade que justificava guardá-los deixou de existir.

---

## Problemas comuns

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| Site mostra "Informe o codigo de 6 digitos" | campo vazio ou incompleto | digitar o código completo |
| Site mostra "Identificacao invalida" | número não autorizado, sem autenticador cadastrado, código errado, ou código já usado | esperar o código trocar e tentar de novo; se persistir, conferir o número com o administrador |
| Código correto e ainda assim recusado | o mesmo código já foi usado nesta janela de 30 segundos | esperar o próximo código |
| Códigos nunca funcionam, desde o início | relógio do celular desajustado, ou chave cadastrada errada | ativar data e hora automáticas; pedir nova chave |
| Colaborador recém-cadastrado não consegue enviar | cadastro feito, autenticador não cadastrado | rodar o bloco de cadastrar TOTP |
| Trocou de celular e não envia mais | a chave ficou no aparelho antigo | pedir nova chave ao administrador |
| Favorito antigo do relatório abre a tela de entrada | comportamento esperado desde o Incremento 8A | digitar a chave e refazer o favorito sem o `?code=` |
| Relatório mostra "Chave invalida" | chave errada, ou a chave de host em vez da do gestor | usar a chave de acesso do gestor, de 43 caracteres |
| Relatório volta a pedir a chave | a sessão de 8 horas venceu, ou os dados do navegador foram limpos | digitar a chave de novo |
| Painel administrativo devolve 401 nos botões | chave do gestor ou de função em vez da chave de host | usar a chave de host |
| Relatório vazio | não há envios no período filtrado | clicar em **hoje** ou ampliar o intervalo |
| Mapa em branco, tabela preenchida | falha ao carregar a biblioteca do mapa | recarregar a página; verificar a conexão |
| Primeiro acesso muito lento | o serviço estava inativo e precisa iniciar | aguardar alguns segundos e repetir |
| Pontos empilhados no mesmo lugar | colaborador parado | normal; o trajeto aparece com deslocamento |
| Página do painel sem os botões de gestão | versão em cache no navegador | recarregar com Ctrl+F5 |

---

## Limitações conhecidas

O sistema prova **posse do aparelho**: só recebe o código quem está com o
celular em que a chave foi cadastrada. Isso não é autenticação de identidade:
quem tem o aparelho não é, necessariamente, o servidor da Diretoria.

O que o Incremento 8B resolveu: enquanto o PIN existia em paralelo, a segurança
efetiva era a do caminho mais fraco — 6 dígitos, sem expiração e sem limite de
tentativas, num endereço público. Esse caminho não existe mais.

O que continua em aberto:

- **A autorização de sessão não pode ser revogada de imediato.** Ela se verifica
  sozinha, sem consulta a banco, e por isso vale até vencer — no máximo 8 horas.
  Isso se aplica às duas: a do colaborador e a do gestor. Para cortar a de um
  colaborador antes, gere uma chave nova do autenticador para ele, o que
  invalida todas as sessões dele na hora. Para a do gestor, não há como cortar
  antes de vencer.
- **A chave do autenticador é guardada no sistema.** Ela não pode ser
  transformada em hash, porque o servidor precisa do valor original para
  recalcular o código. Cifrá-la com uma chave guardada fora do armazenamento é
  melhoria planejada.
- **A chave de acesso do gestor é única para todos os gestores.** Ela não
  identifica quem entrou, e trocá-la obriga todos a receberem a nova.
- **A proteção do relatório saiu da plataforma e entrou no código.** Antes do
  Incremento 8A, o Azure Functions recusava a requisição antes de o código do
  projeto rodar. Agora quem decide é a verificação de sessão escrita no
  projeto. É uma troca assumida: o ganho foi tirar o segredo da URL, o custo é
  que uma falha nessa verificação abriria o relatório.
- **A autorização de sessão admite variação inofensiva.** Alterar o último
  caractere dela pode produzir um texto diferente que o servidor ainda aceita,
  porque aqueles bits não carregam informação. Não permite falsificar nada sem a
  chave do servidor: é imprecisão de representação, verificada e documentada,
  não brecha de acesso.

Login corporativo com autenticação multifator da instituição — que diria **quem**
entrou, e não apenas que a pessoa tem a chave — é o caminho natural e está fora
do escopo desta versão.
