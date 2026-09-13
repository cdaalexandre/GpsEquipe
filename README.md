# GpsEquipe
 
Sistema de rastreamento de localização de colaboradores em campo, construído
sobre arquitetura serverless no Microsoft Azure.
 
Projeto acadêmico — Engenharia de Software, Cruzeiro do Sul / UNICID.
Reconstrução completa de uma versão anterior, migrada do modelo *in-process* do
Azure Functions para o modelo **isolated worker**.
 
---
 
## O que o sistema faz
 
O colaborador abre uma página no navegador do celular, informa seu número e
inicia o rastreamento. A página envia a coordenada do GPS a cada 10 segundos
para uma API, que valida o número contra uma lista de autorizados e grava o
ponto. O gestor acessa um relatório com mapa, trajeto e filtro por período.
 
Coordenadas com mais de 90 dias têm o número do celular anonimizado
automaticamente, por rotina agendada.
 
---
 
## Arquitetura
 
```
Navegador do colaborador
  │  POST /api/recebercoordenadas   (JSON: Celular, Latitude, Longitude)
  ▼
Azure Functions (isolated worker, .NET 9)
  ├── ReceberCoordenadas    HTTP  → valida e grava
  ├── VerRelatorio          HTTP  → relatório HTML com mapa
  └── AnonimizarCoordenadas Timer → anonimiza após 90 dias
  │
  ▼
Azure Table Storage
  ├── Coordenadas             PartitionKey = data, RowKey = GUID
  └── FuncionariosPermitidos  PartitionKey = "FUNCIONARIO", RowKey = celular
 
Azure Blob Storage ($web) → site estático do colaborador
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
| `ReceberCoordenadas.cs` | HTTP POST — valida o celular e grava a coordenada |
| `VerRelatorio.cs` | HTTP GET — relatório HTML com mapa Leaflet e filtro de data |
| `AnonimizarCoordenadas.cs` | Timer trigger — anonimização LGPD |
| `index.html` | site estático do colaborador (captura de GPS) |
| `host.json` | configuração do host do Functions |
| `Anotacoes-v2.txt` | registro de execução, decisões e lições do projeto |
 
`local.settings.json` está fora do versionamento.
 
---
 
## Endpoints
 
| Endpoint | Método | Autorização |
| --- | --- | --- |
| `/api/recebercoordenadas` | POST | anônimo, protegido pela lista de permitidos |
| `/api/verrelatorio` | GET | requer chave de função (`?code=` ou header `x-functions-key`) |
| `AnonimizarCoordenadas` | Timer | CRON `0 30 3 * * *` (3h30 UTC) |
 
### Exemplo de envio
 
```json
{ "Celular": "+5511999998888", "Latitude": -23.5505, "Longitude": -46.6333 }
```
 
Resposta `200`: `Coordenada recebida com sucesso.`
Resposta `403`: celular não cadastrado.
 
### Filtro do relatório
 
```
/api/verrelatorio?inicio=2026-09-01&fim=2026-09-13&code=<CHAVE>
```
 
Bordas inclusivas nas duas pontas. Sem parâmetro, mostra o dia corrente.
Intervalo limitado a 31 dias por consulta.
 
---
 
## Requisitos
 
- .NET SDK 9 ou superior
- Azure Functions Core Tools 4
- Azure CLI 2.x
- Assinatura Azure com permissão para criar Storage Account e Function App
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
 
# 7. CORS: apenas a origem do site estático
az functionapp cors add --name GpsEquipe-App-bad1 --resource-group GpsEquipe-RG `
  --allowed-origins "https://gpsequipebad1.z15.web.core.windows.net"
```
 
## Deploy
 
```powershell
func azure functionapp publish GpsEquipe-App-bad1
 
az storage blob upload --account-name gpsequipebad1 --container-name '$web' `
  --file index.html --name index.html --content-type "text/html" --overwrite
```
 
## Cadastrar um colaborador
 
O número entra sem o sinal de `+`, apenas dígitos.
 
```powershell
az storage entity insert --account-name gpsequipebad1 `
  --table-name FuncionariosPermitidos `
  --entity PartitionKey="FUNCIONARIO" RowKey="5511999998888"
```
 
## Obter a chave do relatório
 
```powershell
az functionapp function keys list --name GpsEquipe-App-bad1 `
  --resource-group GpsEquipe-RG --function-name VerRelatorio --query default -o tsv
```
 
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
da tabela, e a busca por registros antigos na rotina de anonimização usa
comparação lexicográfica no mesmo campo.
 
**Celular como propriedade comum.** `PartitionKey` e `RowKey` são imutáveis no
Table Storage. Mantendo o celular fora das chaves, a anonimização é um update
simples em vez de copiar a entidade com chave nova e apagar a original.
 
**Anonimização, não pseudonimização.** O celular é substituído pelo literal
`ANONIMIZADO`, sem hash. Hash de número de telefone é reversível por força bruta
em minutos, o que manteria o dado como pessoal sob a LGPD.
 
**Mapa sem chave de API.** Leaflet com tiles do OpenStreetMap: custo zero e
nenhuma credencial de terceiro no frontend.
 
**Autorização, não autenticação.** `VerRelatorio` autoriza pela posse da chave;
`ReceberCoordenadas` autoriza pela presença do número na lista. Nenhum dos dois
prova identidade — um número cadastrado pode ser informado por outra pessoa.
Limitação conhecida, aceita pelo escopo acadêmico.
 
---
 
## Segurança
 
- Nenhum segredo no repositório. `local.settings.json` está no `.gitignore`.
- A connection string vive apenas nos app settings da Function App.
- A chave do relatório é gerada e guardada pelo host do Functions.
- Valores sensíveis não passam pelo console: vão de comando direto para
  variável ou arquivo. Conferência usa nome, comprimento ou comparação
  booleana, nunca o conteúdo.
---
 
## Licença
 
Projeto acadêmico, sem licença de uso definida.
