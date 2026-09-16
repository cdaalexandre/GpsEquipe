using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GpsEquipe;

public class ReceberCoordenadas
{
    private readonly ILogger<ReceberCoordenadas> _logger;

    // Incremento 5: resposta unica para toda falha de identificacao.
    // Mensagens distintas transformariam o endpoint em oraculo de quem e
    // colaborador. O motivo real fica no log, nao na resposta HTTP.
    private const string FalhaIdentificacao = "Identificacao invalida.";

    public ReceberCoordenadas(ILogger<ReceberCoordenadas> logger)
    {
        _logger = logger;
    }

    // Formato do JSON enviado pelo index.html. Incremento 5 acrescentou o Pin.
    public class CoordenadaRecebida
    {
        public string? Celular { get; set; }
        public string? Pin { get; set; }
        // Incremento 7c: token de sessao emitido pelo IniciarSessao.
        public string? Token { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    [Function("ReceberCoordenadas")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        CoordenadaRecebida? dados;
        try
        {
            dados = await JsonSerializer.DeserializeAsync<CoordenadaRecebida>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("JSON invalido.");
        }

        if (dados is null || string.IsNullOrWhiteSpace(dados.Celular))
        {
            return new BadRequestObjectResult("Campo Celular e obrigatorio.");
        }

        // Incremento 5: PIN ausente ja falha como identificacao, nao como
        // erro de formato - para nao distinguir "faltou PIN" de "PIN errado".
        // Incremento 7c: o token de sessao tem PRECEDENCIA sobre o PIN. Com token
        // valido o PIN nao e exigido. Enquanto os dois convivem, a seguranca real
        // e a do mais fraco: o PIN sai num passo proprio, depois da gravacao.
        var porToken = false;
        var celularToken = string.Empty;
        long carimboToken = 0;
        if (!string.IsNullOrWhiteSpace(dados.Token))
        {
            var chaveToken = SegurancaToken.LerChave(Environment.GetEnvironmentVariable("TokenChaveHmac"));
            if (chaveToken.Length < 32)
            {
                _logger.LogError("App setting TokenChaveHmac ausente ou curta demais.");
                return new StatusCodeResult(500);
            }
            if (!SegurancaToken.Validar(chaveToken, dados.Token, out celularToken, out carimboToken))
            {
                _logger.LogWarning("Token de sessao invalido ou expirado.");
                return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
            }
            porToken = true;
        }

        if (!porToken && string.IsNullOrWhiteSpace(dados.Pin))
        {
            _logger.LogWarning("Envio sem PIN.");
            return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
        }

        // Normaliza para o formato da RowKey da v1: somente digitos, sem o "+".
        var celular = new string(dados.Celular.Where(char.IsDigit).ToArray());

        // GetEntityIfExistsAsync devolve NullableResponse: testa-se HasValue.
        // Foi o uso de GetEntityAsync aqui que gerou o CS0266 na v1.
        // O celular vai assinado dentro do token: token de um numero nao serve
        // para enviar posicao de outro.
        if (porToken && celularToken != celular)
        {
            _logger.LogWarning("Token de final {a} usado para enviar como final {b}.",
                celularToken.Length >= 4 ? celularToken.Substring(celularToken.Length - 4) : "----",
                celular.Length >= 4 ? celular.Substring(celular.Length - 4) : "----");
            return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
        }

        var permitidos = new TableClient(conexao, "FuncionariosPermitidos");
        var consulta = await permitidos.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>("FUNCIONARIO", celular);
        if (!consulta.HasValue)
        {
            _logger.LogWarning("Celular nao cadastrado: {celular}", celular);
            return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
        }

        var funcionario = consulta.Value!;

        // Falha fechada: cadastro sem PIN definido NAO envia.
        // Unica revogacao que este desenho permite: se o TOTP foi recadastrado
        // depois da emissao, o carimbo muda e o token antigo morre. A leitura da
        // linha ja acontecia para o PIN, entao a verificacao sai de graca.
        if (porToken)
        {
            var carimboAtual = funcionario.TotpDefinidoEm.HasValue ? funcionario.TotpDefinidoEm.Value.ToUnixTimeSeconds() : 0;
            if (carimboAtual == 0 || carimboAtual != carimboToken)
            {
                _logger.LogWarning("Token com carimbo de segredo antigo (final {q}).", celular.Substring(celular.Length - 4));
                return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
            }
        }

        if (!porToken && (string.IsNullOrWhiteSpace(funcionario.PinSalt) || string.IsNullOrWhiteSpace(funcionario.PinHash)))
        {
            _logger.LogWarning("Celular {celular} cadastrado sem PIN definido.", celular);
            return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
        }

        if (!porToken && !SegurancaPin.Conferir(dados.Pin!.Trim(), funcionario.PinSalt, funcionario.PinHash))
        {
            _logger.LogWarning("PIN incorreto para {celular}.", celular);
            return new ObjectResult(FalhaIdentificacao) { StatusCode = 403 };
        }

        var agora = DateTimeOffset.UtcNow;
        var coordenadas = new TableClient(conexao, "Coordenadas");
        await coordenadas.AddEntityAsync(new CoordenadaEntidade
        {
            PartitionKey = agora.ToString("yyyy-MM-dd"),
            RowKey = Guid.NewGuid().ToString("N"),
            Celular = celular,
            Latitude = dados.Latitude,
            Longitude = dados.Longitude,
            DataHoraUtc = agora
        });

        return new OkObjectResult("Coordenada recebida com sucesso.");
    }
}