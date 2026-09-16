using Azure;
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

// Incremento 7a: funcao ADMINISTRATIVA do TOTP. Duas acoes:
//   cadastrar -> gera segredo novo e devolve UMA VEZ para o admin mostrar ao
//                colaborador cadastrar no Microsoft Authenticator.
//   conferir  -> valida um codigo do app, para provar a integracao. NAO consome
//                o anti-replay, senao o primeiro login real falharia.
// O PIN NAO e removido aqui: ele segue autorizando os envios ate o fluxo de
// sessao (7c) entrar no ar.
public class DefinirTotp
{
    private readonly ILogger<DefinirTotp> _logger;
    private const string ParticaoFuncionario = "FUNCIONARIO";

    public DefinirTotp(ILogger<DefinirTotp> logger)
    {
        _logger = logger;
    }

    public class PedidoRecebido
    {
        public string? Acao { get; set; }
        public string? Celular { get; set; }
        public string? Codigo { get; set; }
    }

    [Function("DefinirTotp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        PedidoRecebido? dados;
        try
        {
            dados = await JsonSerializer.DeserializeAsync<PedidoRecebido>(
                req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corpo JSON invalido.");
            return Resposta(400, false, "Corpo da requisicao invalido.");
        }

        var acao = (dados?.Acao ?? string.Empty).Trim().ToLowerInvariant();
        var celular = new string((dados?.Celular ?? string.Empty).Where(char.IsDigit).ToArray());
        if (celular.Length < 12)
        {
            return Resposta(400, false, "Celular invalido. Use apenas digitos, com 55 e DDD.");
        }
        var quatro = celular.Substring(celular.Length - 4);

        var tabela = new TableClient(conexao, "FuncionariosPermitidos");
        try
        {
            var achado = await tabela.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>(
                ParticaoFuncionario, celular);
            if (!achado.HasValue)
            {
                return Resposta(404, false, "Colaborador nao encontrado. Cadastre antes de definir o TOTP.");
            }
            var func = achado.Value!;

            if (acao == "cadastrar")
            {
                var segredo = SegurancaTotp.GerarSegredoBase32();
                await tabela.UpdateEntityAsync(new FuncionarioPermitidoEntidade
                {
                    PartitionKey = ParticaoFuncionario,
                    RowKey = celular,
                    TotpSegredo = segredo,
                    TotpDefinidoEm = DateTimeOffset.UtcNow,
                    TotpUltimaJanela = 0
                }, ETag.All, TableUpdateMode.Merge);

                _logger.LogInformation("Segredo TOTP gerado (final {Quatro}).", quatro);
                var corpo = new
                {
                    ok = true,
                    mensagem = "Segredo gerado. Cadastre no Microsoft Authenticator agora: nao sera exibido de novo.",
                    segredo,
                    uri = SegurancaTotp.MontarUri("GpsEquipe", celular, segredo),
                    periodoSegundos = SegurancaTotp.Periodo,
                    digitos = SegurancaTotp.Digitos
                };
                return new ContentResult
                {
                    Content = JsonSerializer.Serialize(corpo),
                    ContentType = "application/json; charset=utf-8",
                    StatusCode = 201
                };
            }

            if (acao == "conferir")
            {
                if (string.IsNullOrWhiteSpace(func.TotpSegredo))
                {
                    return Resposta(409, false, "Este colaborador ainda nao tem TOTP cadastrado.");
                }
                // Anti-replay nao e consumido nesta acao: passa 0 em vez da
                // ultima janela gravada, porque conferir e diagnostico, nao login.
                var janela = SegurancaTotp.Validar(func.TotpSegredo, dados?.Codigo, 0);
                if (janela < 0)
                {
                    _logger.LogInformation("Codigo TOTP recusado (final {Quatro}).", quatro);
                    return Resposta(403, false, "Codigo invalido.");
                }
                var desvio = janela - SegurancaTotp.JanelaAtual();
                _logger.LogInformation("Codigo TOTP aceito (final {Quatro}, desvio {Desvio}).", quatro, desvio);
                return Resposta(200, true, "Codigo valido. Desvio de janela: " + desvio + " (0 e relogio sincronizado).");
            }

            return Resposta(400, false, "Acao invalida. Use cadastrar ou conferir.");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Falha no Table Storage. Acao {Acao}, final {Quatro}.", acao, quatro);
            return Resposta(502, false, "Falha ao acessar o armazenamento.");
        }
    }

    private static ContentResult Resposta(int status, bool ok, string mensagem)
    {
        return new ContentResult
        {
            Content = JsonSerializer.Serialize(new { ok, mensagem }),
            ContentType = "application/json; charset=utf-8",
            StatusCode = status
        };
    }
}