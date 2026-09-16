# GpsEquipe — Modo de uso

Sistema de rastreamento de colaboradores em campo
Diretoria de Ensino Centro Oeste - SEDUC/SP

Atualizado em 16/09/2026 — Incremento 8A (acesso do gestor por tela) e
Incremento 8B (PIN extirpado).

---

# CARTÃO DE EMERGÊNCIA

**Os três endereços:**

| Quem | Endereço |
| --- | --- |
| Colaborador | https://gpsequipebad1.z15.web.core.windows.net/ |
| Gestor | https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio |
| Administrador | https://gpsequipebad1.z15.web.core.windows.net/admin.html |

**O que cada um precisa ter em mão:**

| Quem | Precisa de | Onde consegue |
| --- | --- | --- |
| Colaborador | o celular com o Microsoft Authenticator | chave de 32 caracteres, do administrador |
| Gestor | a chave de acesso, 43 caracteres | do administrador |
| Administrador | o `Admin.ps1` e login no Azure | o script está no repositório |

Os três endereços são públicos. Nenhum mostra dado nenhum sem identificação.

O endereço `index-totp.html` também responde e serve exatamente a mesma página
da raiz. Use a raiz: é mais curta e é a que está na documentação.

---

# O QUE MUDOU NESTA VERSÃO

**Três coisas. Leia se você já usava o sistema.**

**1. O PIN não existe mais.**
O site antigo pedia PIN de 6 dígitos. Foi desligado.
O endereço dele continua funcionando, mas agora serve a página do autenticador.
Quem tentar enviar posição sem o código do aplicativo recebe erro.
O campo de PIN não é mais lido pelo servidor.

**2. A chave do gestor saiu da URL.**
O relatório agora tem tela de entrada.
Favorito antigo com `?code=...` ainda abre — mas mostra a tela, não o relatório.
Refaça o favorito sem o `?code=`.

**3. A administração virou um script só.**
Antes eram quatro blocos de comando para habilitar um colaborador.
Agora é `.\Admin.ps1 -Habilitar <numero>`.
Os comandos antigos continuam no apêndice, no fim deste documento.

---

# COLABORADOR

**Endereço:** https://gpsequipebad1.z15.web.core.windows.net/

## Antes do primeiro uso — uma vez só

O administrador entrega a você uma **chave de 32 caracteres**.

Cadastre no **Microsoft Authenticator**:

1. Instale o Microsoft Authenticator, se ainda não tiver.
2. Toque no **+** no canto superior.
3. Escolha **Outra conta (Google, Facebook etc.)**.
4. A câmera abre para ler QR code. Toque em **inserir chave manualmente**, embaixo.
5. Nome da conta: `GpsEquipe`
6. Chave secreta: os 32 caracteres que o administrador passou.
7. Concluir.

**Confira:** deve aparecer `GpsEquipe` na lista, com código de 6 dígitos e um
contador girando.

⚠️ **Apague a mensagem com a chave depois de cadastrar.**

⚠️ **Data e hora do celular no automático.** O código depende do relógio.
Aparelho desajustado gera códigos que o servidor recusa.

## Todos os dias

1. Abra o endereço no navegador do celular.
2. Em **Seu celular, com DDD**: digite com código do país e DDD.
   Exemplo: `+5511982253855`
3. Abra o Authenticator. Em **Codigo do Microsoft Authenticator**: digite os
   6 dígitos da entrada `GpsEquipe`.
4. Toque em **Iniciar rastreamento**.
5. Autorize a localização quando o navegador pedir.
6. **Deixe a página aberta.** O envio acontece a cada 10 segundos, sozinho.
7. No fim do expediente, toque em **Parar**.

O código é pedido **uma vez por sessão**, não a cada envio.

Depois de validado, a página recebe autorização que vale **8 horas** — cobre a
jornada inteira.

## Regras do código — as cinco que importam

1. O código muda a cada **30 segundos**.
2. Cada código serve **uma única vez**.
3. Errou o número ou o código? **Espere o código trocar** antes de tentar de
   novo. Repetir o mesmo código é recusado, mesmo estando correto.
4. A autorização fica só na memória da página. Tocou em **Parar** ou fechou o
   navegador, ela é descartada — precisa de código novo.
5. Depois de 8 horas ela vence sozinha, mesmo com a página aberta.

⚠️ **Não existe mais caminho alternativo.** Sem o código do aplicativo, o
sistema não aceita envio.

## O que aparece na tela

| Mensagem | Significa |
| --- | --- |
| Sessao valida ate HH:MM:SS (8h) | código aceito, rastreamento autorizado |
| Coordenada recebida com sucesso. | envio funcionou |
| Envios: N \| ultimo as HH:MM:SS | contador de envios da sessão |
| Informe o codigo de 6 digitos do Authenticator. | campo vazio ou fora do formato; nada foi enviado |
| Identificacao invalida. Espere o codigo trocar e tente de novo. | número não autorizado, sem identificação cadastrada, código errado ou código já usado |
| GPS indisponivel | o aparelho não obteve a posição |
| Sem conexao com a API | falha de rede; tenta de novo no próximo ciclo |

**Por que a mensagem de erro é sempre a mesma?** De propósito.

Mensagens distintas deixariam qualquer pessoa descobrir, número por número, quem
é colaborador da Diretoria. O motivo real fica no log, que só o administrador lê.

## Cuidados

- Seu número precisa estar cadastrado **e** ter a chave do autenticador
  cadastrada. Sem uma das duas, o sistema recusa.
- Não mostre a tela do Authenticator a ninguém.
- Não repasse a chave de 32 caracteres. Ela é o que garante que a coordenada
  gravada em seu nome foi enviada por você.
- A página precisa ficar aberta e visível. Navegador fechado ou tela bloqueada
  por muito tempo pode interromper o envio.
- Dentro de prédios o GPS perde precisão. Ao ar livre a posição é mais exata.
- O consumo de dados é muito baixo — cada coordenada tem poucas centenas de bytes.
- **Trocou de celular?** A chave precisa ser cadastrada de novo no aparelho novo.
  Não tem mais a chave? O administrador gera outra, e a antiga para de funcionar.

## Seus dados

O site traz um aviso recolhido, **Como seus dados sao tratados**.

Ele diz: o que é coletado, a finalidade, a base legal, quem acessa, o prazo de
retenção e o tratamento dado à identificação.

Leia antes do primeiro uso.

---

# GESTOR

**Endereço:** https://gpsequipe-app-bad1.azurewebsites.net/api/verrelatorio

Guarde nos favoritos. **Este endereço não contém segredo** — pode ser
compartilhado, anotado ou mostrado em tela.

## Entrar

1. Abra o endereço.
2. Aparece a tela **Relatorio de rastreamento. Acesso restrito.**, com o campo
   **Chave de acesso**.
3. Cole a chave e clique em **Entrar**.

A chave tem 43 caracteres.

O campo é do tipo senha: os caracteres não aparecem na tela, e a chave não vai
para a barra de endereço nem para o histórico.

## A sessão

Depois de entrar, o navegador guarda uma autorização por **8 horas**.

Nesse período você navega, filtra e recarrega sem digitar a chave de novo.

Passadas as 8 horas, ou ao limpar os dados do navegador, a tela de entrada volta.

A autorização fica num cookie que o JavaScript da página não consegue ler, e que
só é enviado para o endereço do relatório.

⚠️ Ela **não identifica você**. Identifica apenas que alguém apresentou a chave
correta.

## O que a página mostra

**Mapa**, no topo.
Cada colaborador recebe uma cor. Os pontos são as coordenadas recebidas, ligados
por uma linha na ordem em que chegaram. O ponto maior e mais destacado é a
posição mais recente de cada pessoa.

Toque ou clique num ponto: aparece colaborador, data, hora e coordenadas.

**Legenda**, abaixo do mapa, com o número de pontos de cada colaborador.

**Tabela**, ao final, com data, hora, latitude e longitude de cada envio — do
mais recente para o mais antigo, agrupada por colaborador.

## Filtrar por período

Campos **De** e **Até**, no alto da página.

1. Escolha as duas datas.
2. Clique em **Filtrar**.

**Regras do filtro:**

| Situação | O que acontece |
| --- | --- |
| De 10/09 a 12/09 | traz os três dias — as duas datas entram |
| Sem filtro | mostra o dia corrente |
| Link **hoje** | volta ao dia corrente |
| Mais de 31 dias | corta em 31 e avisa na página |
| Datas invertidas | corrige automaticamente, com aviso |
| Data inválida | ignora, usa o dia corrente, com aviso |

O endereço muda junto com o filtro: um período específico pode ser copiado e
compartilhado como link.

⚠️ Quem receber esse link precisa da chave de acesso para ver o conteúdo.

**Fuso:** as datas são sempre do **horário de Brasília**. Envio feito às 22h de
um dia aparece no dia em que foi feito, não no seguinte.

## A página não atualiza sozinha

Recarregue manualmente para ver novos pontos.

O recarregamento automático foi retirado de propósito: ele apagaria o filtro de
período escolhido.

---

# ADMINISTRADOR — O SCRIPT

**Toda a operação administrativa é um script: `Admin.ps1`, na raiz do
repositório.**

## Preparar — uma vez por janela de PowerShell

```powershell
cd C:\repo\GpsEquipe
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
```

A política desta máquina é travada por Group Policy. O escopo `Process` vale só
para a janela aberta e não exige privilégio.

Se não estiver logado no Azure:

```powershell
az login --tenant 38ae2f02-5710-4e12-80bb-83600c3fdf1e
```

## Os cinco comandos

| Comando | O que faz |
| --- | --- |
| `.\Admin.ps1` | mostra quem está cadastrado e quem está sem autenticador |
| `.\Admin.ps1 -Habilitar <numero>` | cadastra o número **e** gera a chave do autenticador |
| `.\Admin.ps1 -Habilitar <numero> -Forcar` | regera a chave de quem já tem |
| `.\Admin.ps1 -Conferir <numero>` | testa o código que o colaborador está vendo |
| `.\Admin.ps1 -Remover <numero>` | remove, com confirmação |
| `.\Admin.ps1 -TrocarChaveGestor` | gera chave **nova** do gestor; a atual morre na hora |

O número pode ir de qualquer jeito: `+55 11 98225-3855` ou `5511982253855`. O
script normaliza.

## O que o script garante, sem você pedir

| Garantia | Como |
| --- | --- |
| Não opera na conta errada | guarda de tenant no início; aborta fora do Azure for Students acadêmico |
| Segredo não aparece na tela | chave e segredo saem pelo clipboard; no terminal aparece só o tamanho |
| Não apaga por acidente | `-Remover`, `-Forcar` e `-TrocarChaveGestor` exigem confirmação digitada |
| Não continua depois de falha | todo comando de escrita tem o exit code conferido |

---

# ADMINISTRADOR — HABILITAR UM COLABORADOR

**Um comando. Ele faz os quatro passos que antes eram quatro blocos.**

```powershell
.\Admin.ps1 -Habilitar 5511999998888
```

O que acontece, em ordem:

1. confere a conta do Azure;
2. autoriza o número na tabela, se ainda não estiver;
3. gera a chave do autenticador e a põe no clipboard;
4. imprime as instruções para você repassar ao colaborador.

⚠️ **A chave fica no clipboard.** Cole no canal privado **antes** de usar o
clipboard para outra coisa. Ela não será exibida de novo.

## Se o colaborador já estiver habilitado

O script **não** regera por acidente. Ele avisa e para.

Para gerar chave nova de propósito:

```powershell
.\Admin.ps1 -Habilitar 5511999998888 -Forcar
```

⚠️ Isso invalida a chave antiga **e derrube as sessões abertas** daquele
colaborador. Ele precisa recadastrar no Authenticator.

A confirmação pedida são os **quatro últimos dígitos** do celular.

## Testar com o colaborador

Peça o código que o aplicativo mostra **naquele momento**:

```powershell
.\Admin.ps1 -Conferir 5511999998888
```

O script pede o código, repete a pergunta até receber 6 dígitos, e mostra o
resultado.

**Leia o desvio de janela na resposta:**

| Valor | Significa |
| --- | --- |
| `0` | relógios sincronizados |
| diferente de `0` | relógio do celular desajustado — ainda funciona, mas a margem fica curta |

Esta conferência **não consome** o código. É diagnóstico, não login.

## Remover um colaborador

```powershell
.\Admin.ps1 -Remover 5511999998888
```

Remove a autorização e a chave do autenticador juntas.
As coordenadas já enviadas **permanecem**.

Confirmação: os quatro últimos dígitos do celular.

---

# ADMINISTRADOR — A CHAVE DO GESTOR

A chave que o gestor digita fica na tabela `Configuracao`, guardada como
**hash**. Tem 43 caracteres.

⚠️ **Nao ha como ler a chave em uso.** O sistema guarda so o hash dela: nem o
servidor recupera o valor. Perdida, resta trocar.

Isso muda a rotina: gestor novo na equipe obriga a trocar a chave para todos.

## Entregar a um gestor

Nao existe comando de leitura. Ao trocar a chave, voce a recebe uma unica vez.
Entregue naquele momento e guarde uma copia em local seguro.

## Trocar a chave

Dois caminhos, mesmo efeito. Pelo painel, no cartao **Chave de acesso do
gestor**: digite `TROCAR` no campo e clique no botao. Ou pelo terminal:

```powershell
.\Admin.ps1 -TrocarChaveGestor
```

Nos dois casos a chave nova vai para o clipboard, nunca para a tela.

⚠️ Uma unica chave serve a todos os gestores. Troca-la obriga todos a
receberem a nova.

⚠️ A anterior para de funcionar imediatamente.

⚠️ Sessoes ja abertas continuam valendo ate vencer, no maximo 8 horas.

✔ **Nao reinicia a aplicacao.** Desde o Incremento 8C a escrita e na tabela,
nao em app setting: nenhuma indisponibilidade.

Confirmacao: digitar a palavra `TROCAR`.

---

# ADMINISTRADOR — PAINEL NO NAVEGADOR

**Endereço:** https://gpsequipebad1.z15.web.core.windows.net/admin.html

O painel é a alternativa visual ao script, e mostra mais: coordenadas recebidas
e o estado da rotina de LGPD, que o script não traz.

O painel pede uma **chave de acesso**. Use a **chave de host**.

⚠️ A chave do gestor **não** serve aqui. A chave de host **não** serve no
relatório. São segredos diferentes, para finalidades diferentes.

**Obter a chave de host:**

```powershell
az functionapp keys list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv | Set-Clipboard
```

A chave fica apenas na memória da aba e desaparece ao fechar a página. Não é
gravada no navegador.

## O que o painel mostra

| Cartão | Conteúdo |
| --- | --- |
| **Alertas** | derivados do estado, não são só números. Ex.: colaborador cadastrado sem identificação, nenhum envio nas últimas 24h |
| **Colaboradores** | total, quantos têm autenticador, quantos não têm, e a lista com celular mascarado (`5511*****3855`) e data do cadastro da chave |
| **Coordenadas recebidas** | total dos últimos 7 dias, quantidade por dia, última posição registrada |
| **Retenção e LGPD** | prazo, data-limite da anonimização, quantos já foram anonimizados, agendamento, próxima execução |
| **Servidor** | hora de Brasília, hora UTC, versão do runtime |

O celular aparece sempre mascarado. O painel precisa identificar a linha, não
precisa do número inteiro.

## O que o painel faz

No cartão **Gestao de colaboradores**, com o número em dígitos:

| Botão | O que faz |
| --- | --- |
| **Cadastrar** | autoriza o número. **Não** cadastra o autenticador: o colaborador ainda não consegue enviar |
| **Remover** | retira a autorização, com confirmação. As coordenadas já enviadas permanecem |

⚠️ **O painel não cadastra a chave do Microsoft Authenticator, de propósito.**

Essa chave é exibida uma única vez. Tela de painel é o pior lugar para exibir
segredo de uso único. Use `.\Admin.ps1 -Habilitar`, que faz o cadastro **e** a
chave num passo.

## O cartao da chave de acesso do gestor

Mostra onde a chave vive e quando foi trocada pela ultima vez.

| Campo | O que diz |
| --- | --- |
| **Onde vive** | tabela `Configuracao` (hash), desde o Incremento 8C |
| **Trocada em** | data e hora da ultima troca, em Brasilia |

Para trocar: digite `TROCAR` no campo, clique em **Trocar chave** e confirme o
alerta. O botao so libera com a palavra exata.

⚠️ A chave nova vai para a AREA DE TRANSFERENCIA, nao para a tela. Cole em
local seguro ANTES de copiar qualquer outra coisa: ela nao e exibida de novo.

⚠️ A anterior morre na hora. Todos os gestores precisam receber a nova.

Se o navegador bloquear a area de transferencia, a chave aparece num campo de
texto, com aviso. Copie dali.

---

# AS TRÊS CHAVES NÃO SE CONFUNDEM

| Chave | Tamanho | Para quem | Abre | Usada onde |
| --- | --- | --- | --- | --- |
| **de acesso do gestor** (tabela `Configuracao`) | 43 | gestor | so o relatorio | digitada na tela de entrada |
| **de host** | 56 | administrador | painel, status, cadastro, remoção e TOTP | colada no campo do painel |
| **de função** do `VerStatus` | 56 | ninguém, na prática | só a leitura de status | não usada pelo painel |

⚠️ **A de host e a de função têm o mesmo tamanho e se parecem.** Usar a de
função no painel carrega o status e recusa todas as operações com erro 401.

O `Admin.ps1` lê a chave de host sozinho. Você só precisa dela para o painel.

✔ **A chave de host abre:** painel, cadastro, remoção, TOTP e status.

✘ **A chave de host NÃO abre mais:** o relatório.

Antes do Incremento 8A ela abria — o que significava que quem administrava
também via a localização da equipe. Agora as duas funções estão separadas por
segredos distintos.

---

# RETENÇÃO DE DADOS

**Coordenadas com mais de 90 dias têm o celular substituído por `ANONIMIZADO`,
todos os dias às 00h30 de Brasília.**

A troca é **irreversível**.

Latitude, longitude e horário permanecem. Só o vínculo com a pessoa é removido.

**Consequência para o gestor:** relatórios de períodos com mais de 90 dias
mostram trajetos sem identificação de quem os percorreu.

## Anonimização e eliminação são coisas diferentes

✔ **Anonimização:** o registro fica e perde o vínculo com a pessoa, porque ainda
serve para algo. É o que acontece com as coordenadas antigas — o trajeto
continua útil.

✘ **Eliminação:** o dado sai da base, porque não há mais por que guardá-lo. É o
que aconteceu com os campos de PIN no Incremento 8B: a finalidade terminou.

O critério é a finalidade, não o formato.

## O que é guardado, e o que não é

| Dado | Guardado? |
| --- | --- |
| Chave do Microsoft Authenticator | **Sim**, em claro. O servidor precisa dela para recalcular o código a cada 30 segundos. Está nas limitações conhecidas |
| Dados do antigo PIN | **Não.** Eliminados no Incremento 8B, não apenas ignorados pelo código |

---

# PROBLEMAS COMUNS

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| Site mostra "Informe o codigo de 6 digitos" | campo vazio ou incompleto | digitar o código completo |
| Site mostra "Identificacao invalida" | número não autorizado, sem autenticador cadastrado, código errado, ou código já usado | esperar o código trocar e tentar de novo; se persistir, rodar `.\Admin.ps1` e conferir a lista |
| Código correto e ainda assim recusado | o mesmo código já foi usado nesta janela de 30 segundos | esperar o próximo código |
| Códigos nunca funcionam, desde o início | relógio do celular desajustado, ou chave cadastrada errada | ativar data e hora automáticas; rodar `.\Admin.ps1 -Conferir <numero>` |
| Colaborador recém-cadastrado não consegue enviar | cadastro feito, autenticador não cadastrado | `.\Admin.ps1 -Habilitar <numero>` |
| Trocou de celular e não envia mais | a chave ficou no aparelho antigo | `.\Admin.ps1 -Habilitar <numero> -Forcar` |
| `Admin.ps1` não executa | política de execução travada por GPO | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` |
| `Admin.ps1` aborta com "conta errada" | login em outro tenant | `az logout` e `az login --tenant 38ae2f02-...` |
| Favorito antigo do relatório abre a tela de entrada | comportamento esperado desde o Incremento 8A | digitar a chave e refazer o favorito sem o `?code=` |
| Relatorio mostra "Chave invalida" | chave errada, ou a chave de host em vez da do gestor | nao ha como reler a chave: se ninguem tiver a atual, `.\Admin.ps1 -TrocarChaveGestor` |
| Relatório volta a pedir a chave | a sessão de 8 horas venceu, ou os dados do navegador foram limpos | digitar a chave de novo |
| Painel devolve 401 nos botões | chave do gestor ou de função em vez da chave de host | usar a chave de host |
| Relatório vazio | não há envios no período filtrado | clicar em **hoje** ou ampliar o intervalo |
| Mapa em branco, tabela preenchida | falha ao carregar a biblioteca do mapa | recarregar a página; verificar a conexão |
| Primeiro acesso muito lento | o serviço estava inativo e precisa iniciar | aguardar alguns segundos e repetir |
| Pontos empilhados no mesmo lugar | colaborador parado | normal; o trajeto aparece com deslocamento |
| Painel sem os botões de gestão | versão em cache no navegador | recarregar com Ctrl+F5 |

---

# LIMITAÇÕES CONHECIDAS

**O sistema prova posse do aparelho. Não prova identidade.**

Só recebe o código quem está com o celular em que a chave foi cadastrada. Mas
quem tem o aparelho não é, necessariamente, o servidor da Diretoria.

## O que o Incremento 8B resolveu

Enquanto o PIN existia em paralelo, a segurança efetiva era a do caminho mais
fraco: 6 dígitos, sem expiração e sem limite de tentativas, num endereço
público.

**Esse caminho não existe mais.**

## O que continua em aberto

**1. Sessão não pode ser revogada de imediato.**
Ela se verifica sozinha, sem consulta a banco, e vale até vencer — no máximo 8
horas. Vale para as duas: colaborador e gestor.

- Para cortar a de um **colaborador** antes: `.\Admin.ps1 -Habilitar <numero>
  -Forcar`. Invalida todas as sessões dele na hora.
- Para cortar a de um **gestor** antes: não há como. Só esperar vencer.

**2. A chave do autenticador é guardada no sistema.**
Não pode ser transformada em hash, porque o servidor precisa do valor original
para recalcular o código. Cifrá-la com chave guardada fora do armazenamento é
melhoria planejada.

**3. A chave do gestor e unica para todos os gestores.**
Nao identifica quem entrou, e troca-la obriga todos a receberem a nova. Desde o
Incremento 8C ela e guardada como hash: nao ha como rele-la, entao gestor novo
tambem obriga rotacao para todos.

**4. A proteção do relatório saiu da plataforma e entrou no código.**
Antes do Incremento 8A, o Azure Functions recusava a requisição antes de o
código do projeto rodar. Agora quem decide é a verificação de sessão escrita no
projeto.

Troca assumida: o ganho foi tirar o segredo da URL; o custo é que uma falha
nessa verificação abriria o relatório.

**5. A autorização de sessão admite variação inofensiva.**
Alterar o último caractere dela pode produzir um texto diferente que o servidor
ainda aceita, porque aqueles bits não carregam informação.

Não permite falsificar nada sem a chave do servidor: é imprecisão de
representação, verificada e documentada, não brecha de acesso.

## O caminho natural, fora do escopo desta versão

Login corporativo com autenticação multifator da instituição — que diria
**quem** entrou, e não apenas que a pessoa tem a chave.

---

# APÊNDICE — COMANDO A COMANDO

**Use esta seção só se o `Admin.ps1` não estiver disponível.** Ela existe para
que o sistema possa ser operado de outra máquina, ou depurado passo a passo.

São os mesmos comandos que o script executa por dentro.

## Obter a chave de host

Necessária em todos os blocos desta seção.

```powershell
az functionapp keys list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv | Set-Clipboard
```

## Cadastrar o número na tabela

O número entra **apenas com dígitos**, sem `+` e sem espaços.
`+55 11 98225-3855` vira `5511982253855`.

```powershell
az storage entity insert --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --entity PartitionKey="FUNCIONARIO" RowKey="5511982253855" --auth-mode key
```

⚠️ O cadastro sozinho **não** habilita o envio. Cadastre a chave do autenticador
em seguida.

## Cadastrar a chave do autenticador

Este é o passo que habilita o colaborador.

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

**Três regras que o servidor aplica:**

1. O celular precisa existir em `FuncionariosPermitidos`, senão a resposta é 404.
2. Cada chamada gera chave **nova** e invalida a anterior — junto com qualquer
   sessão aberta daquele colaborador.
3. A chave é exibida uma única vez. Perdida, só resta gerar outra.

## Conferir o código do colaborador

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

## Ver quem está cadastrado

```powershell
az storage entity query --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos --query "items[].RowKey" -o tsv --auth-mode key
```

## Conferir o que um colaborador tem definido

Sem expor a chave:

```powershell
& {
  $e = az storage entity show --account-name gpsequipebad1 --table-name FuncionariosPermitidos `
    --partition-key FUNCIONARIO --row-key 5511982253855 --auth-mode key -o json | ConvertFrom-Json
  Write-Output ("autenticador cadastrado: {0} | em: {1}" -f (-not [string]::IsNullOrEmpty($e.TotpSegredo)), $e.TotpDefinidoEm)
}
```

## Remover um colaborador

```powershell
az storage entity delete --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --partition-key "FUNCIONARIO" --row-key "5511982253855" --auth-mode key
## Obter a chave de acesso do gestor

Nao existe. A tabela `Configuracao` guarda apenas o hash da chave. Para dar
acesso a quem nao a tem, troque a chave e entregue a nova.

## Trocar a chave de acesso do gestor

```powershell
.\Admin.ps1 -TrocarChaveGestor
```

O script chama a API, que grava o hash novo na tabela `Configuracao` e devolve
a chave em claro uma unica vez. Nao reinicia a aplicacao.

Pelo painel: cartao **Chave de acesso do gestor**, digitar `TROCAR` e clicar no
botao. Mesmo efeito.

}
```
