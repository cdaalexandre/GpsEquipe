# Reflexões — Projeto GpsEquipe (v2)

Alexandre Calzetta Dias Alves
Engenharia de Software — Cruzeiro do Sul / UNICID
Setembro de 2026

---

## 1. Por que o modelo isolated worker

Na v1 o meu código rodava dentro do mesmo processo do motor do Azure
Functions. Na v2 ele roda em processo separado.

Isso muda a natureza da comunicação. Processos distintos não compartilham
memória, então não trocam referência de objeto: trocam mensagem. O que chega
ao meu método não é o objeto original do host, é uma tradução feita pelo
worker.

Na prática isso apareceu no código. O atributo mudou de `[FunctionName]` para
`[Function]`, surgiu um `Program.cs` que hospeda o worker, e o parâmetro do
HTTP trigger passou a ser `HttpRequest` do ASP.NET Core.

A Microsoft encerra o in-process em 10/11/2026. O ganho da separação é
desacoplar a versão do .NET do meu código da versão que o host usa — eu
atualizo um sem depender do outro.

---

## 2. O diagnóstico do 503: ausência de log como evidência

O deploy concluía com sucesso e o app respondia 503 em todas as rotas. Cinco
hipóteses foram descartadas com evidência antes de achar a causa.

A evidência decisiva foi o Application Insights vazio. Zero traces, zero
exceptions, zero requests.

Isso não era falta de informação, era informação. Se o meu código tivesse
executado e quebrado, a exceção apareceria ali. Como não havia nada, a falha
era anterior ao meu código: o worker nunca inicializava.

Aprendi que log vazio precisa ser interpretado, não ignorado. Ele delimita
onde o problema não está, e isso vale tanto quanto apontar onde ele está.

---

## 3. Quando o catálogo da plataforma não corresponde à realidade

O comando `az functionapp list-runtimes --os linux` listava
`DOTNET-ISOLATED|10.0` como suportado. Eu travei o projeto em .NET 10 com base
nisso. O plano Linux Consumption simplesmente não inicia esse worker.

Achei o episódio absurdo. Se o .NET 10 fosse requisito rígido do projeto, eu
ficaria travado sem alternativa nenhuma, seguindo uma documentação correta em
teoria e falsa na prática.

O que resolveu foi o teste, não a documentação. Publicar o mesmo código em dois
frameworks diferentes provou o que nenhum `--help` diria.

A folga que eu tive foi acidental. O .NET 8 morre em novembro de 2026 e o
.NET 9 na mesma data — se o Functions não oferecesse o 9, as opções seriam
trocar de plano de hospedagem ou entregar sobre framework sem suporte.

Se começasse hoje, faria um deploy mínimo em cada framework candidato antes de
fixar a decisão. A escolha era defensável; a ordem de verificação estava
errada.

---

## 4. O vazamento da chave de acesso

A chave da conta de armazenamento foi impressa em texto claro no console por
causa de um filtro que não funcionou. O filtro ignorava nomes contendo
"STORAGE", mas a comparação diferencia maiúsculas de minúsculas e o campo se
chamava `AzureWebJobsStorage`.

O filtro foi a causa imediata. A causa real é anterior: eu pedi os valores dos
app settings quando o objetivo era apenas conferir se um deles existia. Nome
bastava.

Na v1 o caminho foi outro. A connection string com a chave foi colada no
arquivo de anotações e ficou registrada ali.

Caminhos diferentes, causa comum: o segredo só vaza depois que alguém o traz à
superfície. Ele estava seguro no Azure; foi buscado, exibido e então gravado
onde não devia.

O `.gitignore` funcionou nos dois casos — nenhum segredo entrou em commit. Ele
protege o repositório, não protege console, transcript de terminal nem
documento colado à mão.

A regra que passei a aplicar: valor sensível vai de comando direto para
variável ou arquivo, sem passar pelo console. Conferência usa nome, tamanho ou
comparação booleana, nunca o conteúdo.

A correção do incidente foi rotacionar as duas chaves da conta, reescrever os
app settings afetados e comprovar a revogação tentando usar a chave antiga.

---

## 5. A escolha da chave de partição

Defini `PartitionKey` como a data do envio, no formato `yyyy-MM-dd`, e `RowKey`
como um GUID. O celular ficou como propriedade comum, fora das chaves.

Essa decisão foi tomada na Fase 1 e pagou duas vezes depois.

No Incremento 3, o filtro por período virou consulta por partição: uma
cláusula `PartitionKey eq` por dia do intervalo, em vez de varrer a tabela
inteira e filtrar na memória.

No Incremento 4, encontrar registros com mais de 90 dias virou uma comparação
lexicográfica no mesmo campo, `PartitionKey lt`, que funciona porque o formato
de data é ordenável como texto.

O ponto crítico é que `PartitionKey` e `RowKey` são imutáveis no Table Storage.
Se o celular fosse chave, anonimizar exigiria criar um registro novo com chave
anonimizada e apagar o original — duas operações sem transação entre elas. Uma
falha no meio deixaria o dado duplicado ou perdido.

Como o celular é campo comum, a anonimização é um único update.

---

## 6. Autenticação e autorização: o que o sistema realmente faz

`ReceberCoordenadas` é anônimo e confere o número contra a lista
`FuncionariosPermitidos`. `VerRelatorio` exige chave de função e não confere
lista nenhuma.

A conclusão é desconfortável: nenhum dos dois autentica.

A chave do relatório não identifica o portador. Quem a tiver entra, e o sistema
não sabe quem é. A lista de permitidos também não identifica ninguém — ela não
verifica se quem digitou o número é o dono dele.

Os dois fazem autorização, por caminhos distintos: um pela posse de um segredo,
outro pela presença de um registro numa lista.

A consequência prática é que eu posso digitar o celular de um colega no site e
gravar coordenadas como se fossem dele. O sistema aceita.

Autenticar exigiria provar identidade — login, token vinculado a uma pessoa,
validação do número por SMS. Nada disso existe aqui, e o escopo acadêmico não
pedia. Mas é uma limitação que eu reconheço e nomeio, não uma que eu ignoro.

*Esta seção descreve o sistema até o Incremento 4. O Incremento 5 respondeu a
ela — ver seção 7.*

---

## 7. O Incremento 5: de autorização para autenticação fraca

A seção 6 terminava numa limitação nomeada e aceita. O Incremento 5 existe
porque aceitar não é o mesmo que resolver, e a limitação era a mais séria do
sistema: qualquer pessoa podia gravar coordenada em nome de um colaborador
conhecendo apenas o número dele.

O mecanismo escolhido foi um PIN de 6 dígitos por colaborador. O PIN não é
guardado: guarda-se um valor aleatório por pessoa, o salt, e o resultado de
100.000 iterações de PBKDF2-SHA256 sobre o PIN com esse salt. A conta é de mão
única. Nem eu recupero o PIN a partir do que está gravado.

Isso muda a natureza do que o sistema faz. A lista de permitidos autoriza um
número; o PIN vincula o envio a alguém que conhece um segredo pessoal. Passei
de autorização pura para autenticação — fraca, de fator único, mas
autenticação.

O que o PIN não faz: não há bloqueio por tentativas erradas. Seis dígitos são
um milhão de combinações e o endereço de envio é público. O PBKDF2 encarece
cada tentativa em alguns décimos de milissegundo; encarecer não é impedir.
Declarar isso é parte do trabalho, não um defeito a esconder.

### A mensagem de erro como falha de segurança

A decisão menos óbvia do incremento não foi criptográfica. Era a mensagem de
erro.

A versão anterior respondia "Celular nao cadastrado" quando o número não estava
na lista. Isso é excelente para o usuário legítimo e péssimo para o sistema:
transformava o endereço público num consultor. Qualquer pessoa poderia varrer
números e descobrir, um por um, quem é colaborador da Diretoria — sem chave,
sem cadastro, sem deixar rastro que alguém fosse olhar.

As quatro falhas passaram a devolver o mesmo texto. O motivo real vai para o
log, que só o administrador lê.

Aprendi que utilidade da mensagem e segurança do sistema são objetivos que se
opõem, e que a escolha entre eles depende de quem pode ler a resposta. Num
endpoint autenticado, mensagem específica é boa prática. Num endpoint público,
é vazamento.

### O que ficou arquiteturalmente errado

O PIN é conferido a cada envio, ou seja, a cada dez segundos por colaborador. O
desenho correto seria conferir uma vez e emitir um token de sessão curto. O que
eu fiz funciona e custa alguns décimos de milissegundo por coordenada, o que é
irrelevante no plano Consumption — mas é errado, e registrar isso vale mais do
que a economia de esforço de fingir que não é.

### O aviso de tratamento

Enquanto mexia na identificação, percebi uma ausência que não tinha relação com
ela: o site coletava localização de pessoa sem informar nada a quem a fornecia.
Nem finalidade, nem base legal, nem prazo de retenção, nem quem acessa.

A anonimização automática após 90 dias já existia desde o Incremento 4. Ela
cumpre a minimização do dado. Não cumpre o dever de informar o titular — são
obrigações distintas, e eu tinha tratado uma como se cobrisse a outra.

O aviso entrou no site em bloco recolhido, para cumprir a informação sem
empurrar sete parágrafos na frente de quem só quer tocar num botão.

*Esta seção descreve o PIN, que foi extirpado do sistema no Incremento 8B — ver
seção 9. A conferência a cada dez segundos, apontada acima como erro
arquitetural, foi resolvida pelo token de sessão do Incremento 7.*

---

## 8. A porta do gestor: quando a proteção muda de dono

**Tirei a chave da URL e, no mesmo movimento, transferi para o meu código uma
responsabilidade que era do Azure.**

O relatório mostra a localização de pessoas. Até o Incremento 8A ele era
protegido por chave de função, que viajava na query string: `?code=...`. Isso
punha o segredo no histórico do navegador, nos favoritos e nos logs do próprio
Azure.

Agora a chave é digitada numa tela e trocada por um cookie de sessão.

O que mudou não foi a interface.

✔ **Proteção de plataforma:** o host do Azure Functions recusa a requisição
antes de o meu código existir nela. Se eu escrever uma bobagem dentro da função,
a porta continua fechada.

✘ **Proteção de aplicação:** o host deixa passar, e quem decide é uma linha que
eu escrevi. Se `ValidarGestor` tiver um defeito, o relatório fica público.

O critério que separa os dois é *quem recusa* — e, por consequência, de quem é a
culpa quando falha.

Escolhi o segundo. Ganhei o segredo fora da URL; paguei assumindo a
responsabilidade de barrar.

### O ganho que eu não havia previsto

A chave de host deixou de abrir o relatório.

Antes, quem administrava o sistema via de graça a localização de toda a equipe —
limitação que eu havia registrado no modo de uso sem saber como resolver. A
separação entre administrar e vigiar saiu como efeito colateral de outra decisão.

### O papel dentro da estrutura, não escrito no crachá

Havia um risco que nenhum compilador pegaria.

Eu já tinha um token de sessão, do Incremento 7, assinado com uma chave HMAC. Se
eu reaproveitasse o mesmo formato para o gestor, qualquer colaborador com sessão
aberta poderia apresentar o token dele na porta do relatório — assinatura
válida, prazo válido — e entrar.

O jeito comum de evitar isso é escrever um campo no token: `papel=gestor`. Aí o
validador precisa **lembrar de ler** esse campo.

Analogia: plugue de três pinos não entra em tomada de dois. Ninguém confere a
voltagem escrita no aparelho; a forma impede o encaixe.

Fiz pela forma:

| Papel | Payload | Campos |
| --- | --- | --- |
| Colaborador | `v1\|celular\|expira\|carimbo` | 4 |
| Gestor | `g1\|expira` | 2 |

Cada validador exige o seu prefixo e o seu número de campos. A chave que assina
é a mesma, mas um token de colaborador não *encaixa* no validador do gestor: é
recusado por não ter a forma, antes de qualquer conferência de conteúdo.

✔ **Papel estrutural:** está dentro da assinatura e é impossível de ignorar,
porque é a própria forma daquilo que se está lendo.

✘ **Campo de papel:** é um dado a mais que alguém precisa conferir — e "alguém
esqueceu de conferir" descreve boa parte das falhas de autorização que existem.

### A prova

Não bastava eu achar que funcionava.

Forjei, em PowerShell, um token de colaborador **perfeito**: assinatura válida
calculada com a chave HMAC real do sistema, prazo válido, celular cadastrado.
Apresentei como cookie de gestor.

401. O relatório não vazou.

Esse foi o único dos cinco aceites que provou algo que eu não sabia de antemão.
Os outros recusaram lixo; este recusou um crachá legítimo do tipo errado.

### O que continua aberto

A sessão do gestor não pode ser revogada antes de vencer. Ela se verifica
sozinha, sem consulta a banco, e por isso vale até oito horas.

E a chave é única para todos os gestores. Ela autoriza, não identifica: trocá-la
obriga todos a receberem a nova, e o log não diz quem entrou.

---

## 9. O fim do PIN: o caminho mais fraco é que define a segurança

**Enquanto o PIN e o autenticador conviveram, a segurança do sistema era a do
PIN. Ter dois caminhos não somou proteção — subtraiu.**

O Incremento 5 acrescentou o PIN: seis dígitos fixos, sem expiração e sem
bloqueio por tentativas, num endereço público.

O Incremento 7 acrescentou o TOTP: um código de seis dígitos do Microsoft
Authenticator, que muda a cada trinta segundos e só serve uma vez.

Os dois ficaram ativos em paralelo.

✔ **Posse:** o TOTP prova que a pessoa está com o aparelho em que a chave foi
cadastrada. O código morre em trinta segundos.

✘ **Conhecimento:** o PIN prova que a pessoa sabe um número de seis dígitos. Um
milhão de combinações, testáveis à vontade, sem prazo.

O critério que separa os dois é o que o atacante precisa **ter**, não o que ele
precisa saber.

E aqui está o que eu levo deste incremento: somar mecanismos não soma segurança.
Quem ataca escolhe por onde entrar, e escolhe a porta mais fraca. Enquanto o PIN
existia, o TOTP era decoração.

### A troca que o TOTP impôs, e que é uma piora

O PIN nunca foi armazenado. Guardava-se o salt e o resultado de cem mil
iterações de PBKDF2 — nem eu recuperava o PIN de ninguém.

O segredo do autenticador **é** armazenado em claro. Tem de ser: o servidor
precisa dele para recalcular o código a cada trinta segundos.

Verificação por comparação de hash × verificação por recálculo. A segunda não
admite mão única.

Consequência: um vazamento da tabela hoje expõe o segredo de todos os
colaboradores, o que não acontecia com o PIN. Troquei um mecanismo mais forte na
porta por um armazenamento mais frágil atrás dela. É uma decisão, não um
descuido — e a correção, cifrar o segredo com chave guardada fora do storage,
está declarada como trabalho futuro.

### Remover é mais difícil que acrescentar

O PIN estava em seis arquivos, numa função publicada, num site estático, num
botão do painel e em três campos da tabela.

Acrescentar mecanismo é escrever código novo. Remover é encontrar todo lugar que
o supõe — e o compilador encontra apenas parte.

O compilador achou os quatro erros de `Entidades.cs`. Não achou o botão
**Definir PIN** no painel, que continuaria chamando uma rota inexistente. Nem os
comentários que afirmavam, com autoridade, que "o PIN segue autorizando os
envios".

Comentário mentiroso em arquivo autoritativo é pior que comentário ausente.
Alguém vai lê-lo — inclusive eu, em dois meses.

### Eliminação não é anonimização

Os campos de PIN na tabela eram dado pessoal coletado para uma finalidade que
deixou de existir.

✔ **Eliminação:** o dado sai da base, porque não há mais por que guardá-lo.

✘ **Anonimização:** o registro fica e perde o vínculo com a pessoa, porque ainda
serve para algo — é o que a rotina dos noventa dias faz com as coordenadas, que
continuam úteis como trajeto.

O critério é a finalidade, não o formato.

### O detalhe técnico que quase me custou o acesso do colaborador

`TableUpdateMode.Merge` nunca apaga propriedade: ele preserva o que o payload
omite.

Apagar campo exigiu `Replace`. E `Replace` com entidade incompleta apagaria o
segredo do autenticador junto, deixando o colaborador sem conseguir enviar.

O mesmo mecanismo que protege num lugar impede no outro. Eu já havia topado com
ele pelo lado oposto no Incremento 7, quando um `Merge` com entidade parcial
apagou o segredo por causa de um inicializador de propriedade.

Testei em registro descartável antes de tocar o real. O teste pegou um erro — as
aspas do etag removidas pelo PowerShell ao chamar executável nativo — que teria
falhado no registro do único colaborador cadastrado.

### O que o fim do PIN não resolveu

O sistema agora prova posse de aparelho. Não prova identidade.

Quem está com o celular não é, necessariamente, o servidor da Diretoria. A seção
6 continua de pé nessa parte: autorização mais forte, autenticação de identidade
ainda ausente.

---

## Observação final sobre o método de trabalho

Três episódios desta reconstrução ensinaram mais que os quatro incrementos
planejados: o SDK que havia desaparecido da máquina, o 503 causado por um
runtime anunciado e não operante, e a chave exposta por um filtro
case-sensitive.

O que os três têm em comum é que nenhum estava previsto no roteiro, e em todos
o caminho foi o mesmo: ler o erro real antes de tentar corrigir, e não variar
comando às cegas.

O Incremento 5 acrescentou dois episódios do mesmo tipo, e de novo na fronteira
entre o que a ferramenta promete e o que ela entrega. Um `Read-Host` que exibe o
prompt e não captura nada, dentro de bloco colado no console, produziu um erro
400 no servidor cuja causa estava no cliente. E um método de geração de número
aleatório que existe no .NET 9 que compila o projeto, mas não no .NET Framework
que executa o PowerShell 5.1 — a mesma forma do 503, uma camada acima.

A lição que se repete é sobre onde procurar. Nos cinco casos a mensagem de erro
apontava para um lugar e a causa estava em outro: no host, não no código; no
cliente, não no servidor; no runtime que executa, não no que compila. Ler a
mensagem é o primeiro passo. Desconfiar de onde ela aponta é o segundo.

A rodada dos Incrementos 8A e 8B acrescentou uma lição de natureza diferente, e
ela é sobre mim, não sobre a ferramenta. Três vezes eu escrevi uma verificação
que afirmou sucesso lendo um estado que não havia mudado: três `True` conferidos
depois de um `Replace` que falhou, um "arquivo gravado, UTF-8 válido" depois de
uma inserção que gravou linha vazia, e uma contagem esperando zero menções a PIN
num arquivo cuja função é justamente vigiar a palavra PIN. Nenhuma causou dano,
porque o teste em registro descartável e as âncoras conferidas antes de escrever
seguraram. Mas o padrão é o mesmo nos três: eu escrevi o teste de cabeça, a
partir do que esperava, em vez de derivá-lo do texto real.

A regra que fica é curta: conferência olha o conteúdo, não o veículo.
"Compilou", "gravou" e "UTF-8 válido" dizem que o transporte funcionou, não que
a carga é a certa.
