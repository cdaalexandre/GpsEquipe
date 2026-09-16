# GpsEquipe

Sistema de rastreamento de localização de colaboradores em campo, construído
sobre arquitetura serverless no Microsoft Azure.

Projeto acadêmico — Engenharia de Software, Cruzeiro do Sul / UNICID.
Reconstrução completa de uma versão anterior, migrada do modelo *in-process* do
Azure Functions para o modelo **isolated worker**.

---

## O que o sistema faz

O colaborador abre uma página no navegador do celular, informa seu número e o
código de 6 dígitos do Microsoft Authenticator. A página troca esse código por
uma autorização de sessão válida por 8 horas e passa a enviar a coordenada do
GPS a cada 10 segundos. A API valida a sessão, confere o número contra a lista
de autorizados e grava o ponto.

O gestor abre o relatório, digita a chave de acesso numa tela e vê mapa,
trajeto e filtro por período.

O administrador usa um painel no navegador para cadastrar e remover
colaboradores e acompanhar o estado do sistema.

Coordenadas com mais de 90 dias têm o número do celular anonimizado
automaticamente, por rotina agendada.

---

## Arquitetura

```
Navegador do colaborador
  │  POST /api/iniciarsessao        (JSON: Celular, Codigo)      -> token 8h
  │  POST /api/recebercoordenadas   (JSON: Celular, Token, Latitude, Longitude)
  ▼
Azure Functions (isolated worker, .NET 9)
  ├── IniciarSessao          HTTP  anônimo  → valida TOTP, emite token assinado
  ├── ReceberCoordenadas     HTTP  anônimo  → valida token e grava a coordenada
  ├── VerRelatorio           HTTP  anônimo  → tela de entrada e relatório com mapa
  ├── DefinirTotp            HTTP  chave    → gera o segredo do autenticador
  ├── GerenciarColaborador   HTTP  chave    → cadastra e remove colaborador
  ├── VerStatus              HTTP  chave    → estado do sistema em JSON
  ├── GerenciarChaveGestor   HTTP  chave    → estado e rotação da chave do gestor
  └── AnonimizarCoordenadas  Timer          → anonimiza após 90 dias
  │
  ▼
Azure Table Storage
  ├── Coordenadas             PartitionKey = data UTC, RowKey = GUID
  ├── FuncionariosPermitidos  PartitionKey = "FUNCIONARIO", RowKey = celular
  └── Configuracao            PartitionKey = "CONFIG", RowKey = "ChaveGestor" (hash)

Azure Blob Storage ($web)
  ├── index.html        site do colaborador
  └── admin.html        painel administrativo
```

| Recurso | Nome | SKU |
| --- | --- | --- |
| Resource Group | `GpsEquipe-RG` | — |
| Storage Account | `gpsequipebad1` | Standard_LRS |
| Function App | `GpsEquipe-App-bad1` | Consumption (Y1) |
| Região | brazilsouth | — |

Todos os recursos dentro da faixa gratuita do Azure for Students.

---

## Estrutura do repositório

| Arquivo | Conteúdo |
| --- | --- |
| `Program.cs` | host do worker isolado |
| `Entidades.cs` | `CoordenadaEntidade` e `FuncionarioPermitidoEntidade` (`ITableEntity`) |
| `IniciarSessao.cs` | HTTP POST — troca o código do autenticador por um token de sessão |
| `ReceberCoordenadas.cs` | HTTP POST — valida o token e grava a coordenada |
| `VerRelatorio.cs` | HTTP GET/POST — tela de entrada do gestor e relatório com mapa Leaflet |
| `DefinirTotp.cs` | HTTP POST — gera e confere o segredo do Microsoft Authenticator |
| `GerenciarColaborador.cs` | HTTP POST — cadastra e remove colaborador |
| `VerStatus.cs` | HTTP GET — estado do sistema em JSON, para o painel |
| `AnonimizarCoordenadas.cs` | Timer trigger — anonimização LGPD |
| `SegurancaToken.cs` | emissão e verificação dos tokens de sessão (HMAC-SHA256) |
| `SegurancaTotp.cs` | cálculo do código TOTP (RFC 6238) |
| `ChaveGestorStore.cs` | guarda, valida e rotaciona a chave do gestor (hash em tabela) |
| `GerenciarChaveGestor.cs` | HTTP POST — estado e rotação da chave do gestor |
| `Admin.ps1` | toda a operação administrativa num script só |
| `Roteiro-Apresentacao.md` | roteiro da demonstração ao vivo |
| `index-totp.html` | site do colaborador (captura de GPS) |
| `admin.html` | painel administrativo |
| `host.json` | configuração do host do Functions |
| `Anotacoes-v2.txt` | registro de execução, decisões e lições do projeto |
| `ModoDeUso-GpsEquipe.md` | manual de operação dos três perfis |
| `Reflexoes-GpsEquipe.md` | reflexões acadêmicas sobre as decisões tomadas |
| `demo/Demo-1-Limpar.ps1` | limpa a base antes da gravação da demonstração |
| `demo/Demo-2-Preparar.ps1` | prepara e verifica o sistema para a gravação |
| `demo/Demo-3-Encerrar.ps1` | rotaciona segredos e limpa rastros depois da gravação |

`local.settings.json` está fora do versionamento.

No container `$web`, `index.html` é uma cópia do `index-totp.html`: o
`indexDocument` do site estático aponta para `index.html`, então a raiz do
endereço serve a página do autenticador.

---

## Endpoints

| Endpoint | Método | Autorização |
| --- | --- | --- |
| `/api/iniciarsessao` | POST | anônimo, protegido pelo código TOTP |
| `/api/recebercoordenadas` | POST | anônimo, protegido pelo token de sessão |
| `/api/verrelatorio` | GET, POST | anônimo, protegido por cookie de sessão de gestor |
| `/api/definirtotp` | POST | chave de função |
| `/api/gerenciarcolaborador` | POST | chave de função |
| `/api/verstatus` | GET | chave de função |
| `/api/gerenciarchavegestor` | POST | chave de função |
| `AnonimizarCoordenadas` | Timer | CRON `0 30 3 * * *` (3h30 UTC) |

As três rotas anônimas não são abertas: a proteção delas está no código, não na
plataforma. Cada uma verifica um segredo próprio antes de fazer qualquer coisa.

### Abrir sessão e enviar coordenada

```json
POST /api/iniciarsessao
{ "Celular": "5511999998888", "Codigo": "123456" }
```

Resposta `200`: `{ "ok": true, "token": "...", "expiraEmUtc": "...", "validadeHoras": 8 }`
Resposta `403`: `Identificacao invalida.`

```json
POST /api/recebercoordenadas
{ "Celular": "5511999998888", "Token": "...", "Latitude": -23.5505, "Longitude": -46.6333 }
```

Resposta `200`: `Coordenada recebida com sucesso.`
Resposta `403`: `Identificacao invalida.`

**Falha fechada com mensagem única.** Número não cadastrado, sem autenticador,
código errado, código já usado, token inválido, token expirado e token de outro
número devolvem todos o mesmo `403` com o mesmo texto. Mensagens distintas
transformariam o endpoint em oráculo de quem é servidor da Diretoria. O motivo
real vai para o log.

### Entrar no relatório

```
GET  /api/verrelatorio                        -> 401 com a tela de entrada
POST /api/verrelatorio   (form: chave=...)    -> 303 + cookie de sessão
GET  /api/verrelatorio   (com o cookie)       -> 200 com o relatório
```

O cookie é `HttpOnly`, `Secure`, `SameSite=Strict`, restrito a
`/api/verrelatorio` e válido por 8 horas.

### Filtro do relatório

```
/api/verrelatorio?inicio=2026-09-01&fim=2026-09-13
```

Bordas inclusivas nas duas pontas. Sem parâmetro, mostra o dia corrente.
Intervalo limitado a 31 dias por consulta. Datas invertidas são trocadas com
aviso; data inválida cai no dia corrente com aviso.

---

## Requisitos

- .NET SDK 9 ou superior
- Azure Functions Core Tools 4
- Azure CLI 2.x
- Assinatura Azure com permissão para criar Storage Account e Function App

Pacotes principais, conforme o `.csproj`:

| Pacote | Versão |
| --- | --- |
| `Azure.Data.Tables` | 12.12.0 |
| `Microsoft.Azure.Functions.Worker` | 2.52.0 |
| `Microsoft.Azure.Functions.Worker.Sdk` | 2.0.7 |
| `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` | 2.1.0 |
| `Microsoft.Azure.Functions.Worker.Extensions.Timer` | 4.3.1 |

---

## Provisionamento

```powershell
# 1. Resource Group
az group create --name GpsEquipe-RG --location brazilsouth

# 2. Storage Account (nome global, 3-24 chars, minúsculas e dígitos)
az storage account create --name gpsequipebad1 --resource-group GpsEquipe-RG `
  --location brazilsouth --sku Standard_LRS

# 3. Tabelas
az storage table create --name Coordenadas --account-name gpsequipebad1
az storage table create --name FuncionariosPermitidos --account-name gpsequipebad1

# 4. Site estático
az storage blob service-properties update --account-name gpsequipebad1 `
  --static-website --index-document index.html

# 5. Function App
az functionapp create --name GpsEquipe-App-bad1 --resource-group GpsEquipe-RG `
  --storage-account gpsequipebad1 --consumption-plan-location brazilsouth `
  --runtime dotnet-isolated --runtime-version 9 --functions-version 4 --os-type Linux

# 6. Connection string como app setting (sem exibir o valor no console)
$cs = (az storage account show-connection-string --name gpsequipebad1 `
  --resource-group GpsEquipe-RG --query connectionString -o tsv).Trim()
az functionapp config appsettings set --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --settings "TabelaConnectionString=$cs" -o none

# 7. Segredo da aplicação: a chave HMAC que assina os tokens de sessão.
#    Valores gerados localmente e gravados sem passar pelo console.
#    A chave do gestor NÃO entra aqui: desde o Incremento 8C ela vive na tabela
#    Configuracao, como hash, e nasce na primeira rotação.
& {
  $b = New-Object byte[] 32
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($b); $rng.Dispose()
  $hmac = [Convert]::ToBase64String($b)
  az functionapp config appsettings set --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --settings "TokenChaveHmac=$hmac" -o none
  $b = $null; $hmac = $null
}

# 7b. Chave de acesso do gestor: gerada pela primeira rotação, que também cria
#     a tabela Configuracao. A chave é exibida uma única vez.
.\Admin.ps1 -TrocarChaveGestor

# 8. CORS: apenas a origem do site estático
az functionapp cors add --name GpsEquipe-App-bad1 --resource-group GpsEquipe-RG `
  --allowed-origins "https://gpsequipebad1.z15.web.core.windows.net"
```

### App settings usados pela aplicação

| Nome | Para quê |
| --- | --- |
| `TabelaConnectionString` | acesso ao Table Storage |
| `TokenChaveHmac` | assina e verifica os tokens de sessão (32 bytes em base64) |


## Deploy

```powershell
func azure functionapp publish GpsEquipe-App-bad1

az storage blob upload --account-name gpsequipebad1 --container-name '$web' `
  --file index-totp.html --name index.html --content-type "text/html" --overwrite
az storage blob upload --account-name gpsequipebad1 --container-name '$web' `
  --file admin.html --name admin.html --content-type "text/html" --overwrite
```

## Habilitar um colaborador

Duas etapas. O cadastro sozinho não permite enviar coordenada.

```powershell
# 1. Autorizar o número (apenas dígitos, sem o sinal de +)
az storage entity insert --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --entity PartitionKey="FUNCIONARIO" RowKey="5511999998888" --auth-mode key

# 2. Gerar o segredo do Microsoft Authenticator
& {
  $chave = az functionapp keys list --name GpsEquipe-App-bad1 `
    --resource-group GpsEquipe-RG --query "functionKeys.default" -o tsv
  $corpo = @{ Acao = 'cadastrar'; Celular = '5511999998888' } | ConvertTo-Json -Compress
  $r = Invoke-WebRequest -Uri "https://gpsequipe-app-bad1.azurewebsites.net/api/definirtotp?code=$chave" `
    -Method Post -ContentType 'application/json' -Body $corpo -UseBasicParsing
  ($r.Content | ConvertFrom-Json).segredo | Set-Clipboard
  Write-Output 'Segredo no clipboard. Exibido uma unica vez.'
  $chave = $null; $corpo = $null
}
```

O passo 1 também pode ser feito pelo painel administrativo. O passo 2, não, de
propósito: o segredo é exibido uma única vez, e tela de painel é o pior lugar
para mostrar segredo de uso único.

## A chave de acesso do gestor

Não existe comando de leitura. A tabela `Configuracao` guarda apenas o hash da
chave: nem o servidor recupera o valor. Para dar acesso a quem não a tem, troque
a chave e entregue a nova.

```powershell
.\Admin.ps1 -TrocarChaveGestor
```

A rotação grava o hash novo na tabela e devolve a chave em claro uma única vez.
Não reinicia a aplicação. O painel administrativo faz o mesmo, no cartão
**Chave de acesso do gestor**.

---

## Decisões de projeto

**Isolated worker.** O suporte ao modelo in-process do .NET no Azure Functions
termina em 10/11/2026. A versão anterior usava in-process; esta nasceu no modelo
isolado, com o código portado: `[FunctionName]` → `[Function]`, host em
`Program.cs`, e HTTP trigger sobre a integração ASP.NET Core (`HttpRequest` e
`IActionResult`).

**.NET 9, não .NET 10.** O `az functionapp list-runtimes --os linux` lista
`DOTNET-ISOLATED|10.0` como suportado, mas o plano Linux Consumption não inicia
o worker: o deploy conclui e o host responde 503 permanente, sem telemetria.
Trocando apenas o target framework para `net9.0`, o mesmo código sobe. Migrar
para Flex Consumption e voltar ao .NET 10 é trabalho futuro.

**Particionamento por data.** `PartitionKey` é a data do envio no formato
`yyyy-MM-dd`. O filtro de período vira consulta por partição em vez de varredura
da tabela, e a rotina de anonimização encontra os registros antigos com
`PartitionKey lt '<data>'` — comparação lexicográfica que funciona porque o
formato de data é ordenável como texto.

**Celular como propriedade comum.** `PartitionKey` e `RowKey` são imutáveis no
Table Storage. Mantendo o celular fora das chaves, a anonimização é um update
simples em vez de copiar a entidade com chave nova e apagar a original.

**Anonimização, não pseudonimização.** O celular é substituído pelo literal
`ANONIMIZADO`, sem hash. Hash de número de telefone é reversível por força bruta
em minutos, o que manteria o dado como pessoal sob a LGPD. A rotina é idempotente:
registros já anonimizados são contados e ignorados.

**Patch mínimo no `Merge`.** A anonimização envia apenas as chaves e o campo
`Celular`. O `TableUpdateMode.Merge` toca só o que vai no payload, então
`Latitude`, `Longitude` e `DataHoraUtc` não são reenviados — o que evita
`400 OutOfRangeInput` em registros com algum campo ausente. O mesmo
comportamento tem o efeito inverso ao remover propriedade: `Merge` nunca apaga
campo, então eliminar dado exige `Replace` com a entidade remontada.

**Identificação por TOTP.** O colaborador prova posse do aparelho em que a chave
do autenticador foi cadastrada. O código muda a cada 30 segundos e cada um serve
uma única vez — a última janela usada fica gravada na linha do colaborador.

**Token de sessão sem estado.** O código é trocado uma vez por sessão, não a cada
envio. O token é `base64url(payload) + "." + base64url(HMACSHA256(payload))`,
verificado sem consulta a banco. Consequência assumida: não pode ser revogado
antes de vencer. Recadastrar o autenticador muda o carimbo que vai dentro do
payload e invalida todas as sessões daquele colaborador na hora.

**Separação estrutural de papéis.** O token do colaborador é
`v1|celular|expira|carimbo`, quatro campos; o do gestor é `g1|expira`, dois. Cada
validador exige o seu prefixo e o seu número de campos. A chave HMAC é a mesma,
mas não existe caminho em que um confira o token do outro — e não há campo
"papel" que alguém possa esquecer de ler, porque o papel é a própria estrutura do
payload, dentro da assinatura.

**Chave do gestor fora da URL.** Até o Incremento 8A o relatório usava chave de
função na query string, o que a colocava no histórico do navegador, nos
favoritos e nos logs do próprio Azure. Agora ela é digitada numa tela e trocada
por um cookie de sessão. O custo: a proteção do relatório saiu da plataforma e
entrou no código da aplicação. O ganho colateral: a chave de host deixou de abrir
o relatório, separando quem administra de quem vê a localização da equipe.

**PIN extirpado.** O sistema teve, nos incrementos 5 a 7, uma identificação por
PIN de 6 dígitos em paralelo ao TOTP. Enquanto os dois conviveram, a segurança
efetiva era a do caminho mais fraco: seis dígitos, sem expiração e sem limite de
tentativas, num endereço público. O Incremento 8B removeu a função, o site
antigo, os campos da tabela e o ramo de código. O campo `Pin` não é mais lido, o
que significa que nenhum cliente antigo consegue enviar.

**Chave do gestor em tabela, como hash.** Até o Incremento 8C ela era um app
setting. Gravar app setting é operação do plano de gerenciamento e reinicia a
Function App: a requisição que grava morre no restart, então a troca nunca
poderia ser feita por tela. Escrever em tabela é plano de dados, com a mesma
credencial que o código já usa, e não reinicia nada. A chave pode ser hash
porque o servidor apenas **compara** o que o gestor digitou; o segredo do
autenticador não pode, porque ele precisa **recalcular** o código a cada 30
segundos. SHA-256 sem salt basta: a chave é sorteada, 32 bytes, e não há
dicionário a percorrer. O custo assumido é que a chave em uso deixou de ser
legível — gestor novo obriga rotação para todos.

**Mapa sem chave de API.** Leaflet com tiles do OpenStreetMap: custo zero e
nenhuma credencial de terceiro no frontend.

**Autorização, não autenticação.** O sistema prova posse de um segredo — o
aparelho do colaborador, a chave do gestor —, não identidade. Quem tem o aparelho
não é, necessariamente, o servidor da Diretoria; e a chave do gestor é única para
todos os gestores, então não diz quem entrou. Login corporativo com autenticação
multifator da instituição é o caminho natural e está fora do escopo desta versão.

---

## Segurança

- Nenhum segredo no repositório. `local.settings.json` está no `.gitignore`.
- A connection string, a chave HMAC e a chave do gestor vivem apenas nos app
  settings da Function App.
- O PIN nunca foi armazenado, apenas um valor derivado dele por PBKDF2 — e esse
  valor foi eliminado da base quando o PIN saiu de operação.
- O segredo do autenticador **é** armazenado: o servidor precisa dele em claro
  para recalcular o código a cada 30 segundos. Cifrá-lo com uma chave guardada
  fora do armazenamento é melhoria planejada.
- Valores sensíveis não passam pelo console: vão de comando direto para
  variável ou arquivo. Conferência usa nome, comprimento ou comparação
  booleana, nunca o conteúdo.
- O celular aparece mascarado no painel administrativo (`5511*****3855`): a
  interface precisa identificar a linha, não o número inteiro.

---

## Licença

Projeto acadêmico, sem licença de uso definida.
