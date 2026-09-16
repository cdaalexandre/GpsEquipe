# GpsEquipe - Roteiro de apresentacao

Demonstracao ao vivo, 12 a 15 minutos. Projeto de extensao, Cruzeiro do Sul.

A ordem abaixo conta uma historia: um problema real, tres papeis, uma decisao
de engenharia. Nao e um passeio pelas telas.

---

## ANTES DE COMECAR (na vespera, nao na hora)

| Preparar | Por que |
| --- | --- |
| Um colaborador habilitado, com o Authenticator no seu proprio celular | voce precisa gerar um codigo ao vivo |
| Coordenadas na base, do MESMO dia | relatorio vazio nao demonstra nada |
| Tres abas abertas: colaborador, relatorio, painel | trocar de aba e mais rapido que digitar URL |
| A chave do gestor e a de host coladas num bloco de notas | 43 e 56 caracteres nao se digitam ao vivo |
| Uma chamada previa a cada endereco | tira o cold start do caminho |

⚠️ Faca as tres chamadas previas 5 minutos antes. O plano Consumption
hiberna, e o primeiro acesso demora.

---

## 1. O PROBLEMA (1 min, sem tela)

A Diretoria de Ensino Centro Oeste tem servidores que visitam escolas em campo.
Nao havia registro de onde a equipe esteve. Sem isso nao se planeja rota, nao se
comprova visita e nao se dimensiona equipe.

Uma frase para fechar: o sistema responde onde a equipe esteve, nao vigia quem
e a pessoa.

---

## 2. O COLABORADOR (3 min)

Abra o endereco do colaborador no CELULAR, espelhado na tela se possivel.

1. Mostre os dois campos: celular e codigo de 6 digitos.
2. Abra o Microsoft Authenticator. Mostre o codigo girando.
3. Digite os dois, toque em Iniciar rastreamento, autorize a localizacao.
4. Aponte a mensagem: Sessao valida ate HH:MM:SS (8h).
5. Espere um ciclo. Aponte: Coordenada recebida com sucesso, e o contador.

O que dizer enquanto envia: o codigo e pedido uma vez por sessao, nao a cada
envio. O envio e automatico, a cada 10 segundos, com a pagina aberta.

DEMONSTRE A FALHA. Toque em Parar, apague um digito do codigo e tente de novo:
aparece Identificacao invalida. Diga que a mensagem e a MESMA para numero nao
cadastrado, codigo errado ou codigo repetido - de proposito, para ninguem
descobrir quem e colaborador testando numeros.

---

## 3. O GESTOR (3 min)

Troque para a aba do relatorio, ja com a sessao aberta.

1. Mostre o mapa: uma cor por colaborador, o ponto maior e a posicao mais recente.
2. Clique num ponto: colaborador, data, hora, coordenadas.
3. Filtre um periodo. Aponte que a URL muda junto e pode ser compartilhada.
4. Diga: quem receber o link precisa da chave; o link sozinho nao mostra nada.

Se sobrar tempo, abra uma aba anonima no mesmo endereco: aparece a tela de
entrada, nao o relatorio. E a prova visual de que o acesso e restrito.

---

## 4. O ADMINISTRADOR E A DECISAO DE ENGENHARIA (4 min)

Esta e a parte que diferencia o trabalho. Nao corra.

Abra o painel com a chave de host.

1. Cartao Alertas: derivados do estado, nao sao numeros soltos.
2. Cartao Colaboradores: celular sempre mascarado, 5511*****3855.
3. Cartao Retencao e LGPD: 90 dias, proxima execucao, quantos ja anonimizados.

Entao pare no cartao da chave do gestor e conte a historia:

- Ate ontem essa chave era uma configuracao da aplicacao. Trocar exigia gravar
  configuracao, o que REINICIA o servico. A requisicao que grava morria no
  restart, entao a troca so era possivel por linha de comando.
- Mudei o lugar: a chave passou a viver numa tabela, guardada como hash.
  Escrever em tabela nao reinicia nada.
- Resultado: troca por tela, sem indisponibilidade.

Se quiser demonstrar ao vivo: digite TROCAR, clique, confirme. A chave nova vai
para a area de transferencia e o cartao atualiza na hora.

⚠️ So troque ao vivo se tiver onde colar a chave nova imediatamente. A
anterior morre na hora, e a sua sessao de gestor continua aberta - mas a chave
que voce tinha anotada deixa de servir.

---

## 5. FECHAMENTO (1 min)

Duas frases:

O sistema prova POSSE DO APARELHO, nao identidade. Quem tem o celular envia
coordenada em nome daquele cadastro. Login corporativo com a autenticacao da
instituicao resolveria, e e o proximo passo natural.

Os dados antigos perdem o vinculo com a pessoa apos 90 dias, automaticamente. O
trajeto continua util; a identificacao, nao.

---

## PERGUNTAS PROVAVEIS

| Pergunta | Resposta curta |
| --- | --- |
| Quanto custa? | Nada. Consumption e Standard_LRS ficaram na faixa gratuita |
| E se o funcionario nao quiser ser rastreado? | O rastreamento e do expediente e ele inicia e para. A pagina traz o aviso de tratamento de dados, com finalidade e base legal |
| Isso fere a LGPD? | Coleta minima, finalidade declarada, retencao de 90 dias e anonimizacao automatica depois |
| Por que nao um app nativo? | Navegador dispensa instalacao e loja. A geolocalizacao do navegador resolve |
| E se a pessoa passar o celular para outra? | O sistema nao impede. Esta documentado como limitacao conhecida, nao como descuido |
| Por que o codigo de 6 digitos e nao uma senha? | Senha nao expira e pode ser repassada. O codigo vale 30 segundos e uma unica vez |
| Quem ve a localizacao? | Quem tem a chave do gestor. A chave nao identifica quem entrou: e a limitacao 3 |

---

## PLANO B

| Se | Faca |
| --- | --- |
| A internet cair | mostre as capturas de tela do ModoDeUso e narre a mesma sequencia |
| O GPS nao pegar dentro da sala | diga que dentro de predios a precisao cai; mostre pontos de outro dia |
| O primeiro acesso demorar | fale do plano Consumption e da hibernacao enquanto carrega; e conteudo, nao desculpa |
| O codigo for recusado | espere trocar e tente de novo; explique que codigo usado nao se repete |
| A troca da chave falhar | mostre o cartao com Onde vive e Trocada em; a historia se conta sem a demonstracao |

---

## O QUE NAO FAZER

- Nao mostre a chave de host nem a do gestor na tela projetada.
- Nao abra o Admin.ps1 durante a demo: a guarda de tenant imprime o nome da conta.
- Nao cadastre colaborador novo ao vivo: gera segredo de uso unico em publico.
- Nao prometa o que esta em LIMITACOES CONHECIDAS. Cite como proximo passo.
