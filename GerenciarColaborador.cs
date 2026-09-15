using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GpsEquipe;

// Incremento 6: funcao ADMINISTRATIVA de cadastro. Cobre apenas cadastrar e
// remover colaborador. O PIN continua a cargo do DefinirPin, que ja existe e ja
// valida formato: reimplementar PBKDF2 aqui criaria duas fontes da mesma regra.
public class GerenciarColaborador
{
    private readonly ILogger<GerenciarColaborador> _logger;
    private const string ParticaoFuncionario = "FUNCIONARIO";

    // 55 + DDD de 2 digitos + 8 ou 9 digitos de linha.
    private static readonly Regex FormatoCelular = new(@"^55\d{10,11}$", RegexOptions.Compiled);

    public GerenciarColaborador(ILogger<GerenciarColaborador> logger)
    {
        _logger = logger;
    }

    public class PedidoRecebido
    {
        public string? Acao { get; set; }
        public string? Celular { get; set; }
    }

    [Function("GerenciarColaborador")]
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

        if (!FormatoCelular.IsMatch(celular))
        {
            return Resposta(400, false, "Celular invalido. Use apenas digitos, com 55 e DDD. Exemplo: 5511982253855.");
        }

        var tabela = new TableClient(conexao, "FuncionariosPermitidos");
        var quatro = celular.Substring(celular.Length - 4);

        try
        {
            var existente = await tabela.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>(
                ParticaoFuncionario, celular);

            if (acao == "cadastrar")
            {
                if (existente.HasValue)
                {
                    return Resposta(409, false, "Este colaborador ja esta cadastrado.");
                }
                await tabela.AddEntityAsync(new FuncionarioPermitidoEntidade
                {
                    PartitionKey = ParticaoFuncionario,
                    RowKey = celular
                });
                _logger.LogInformation("Colaborador cadastrado (final {Quatro}).", quatro);
                // Falha fechada: sem PIN definido, o ReceberCoordenadas recusa com 403.
                return Resposta(201, true, "Colaborador cadastrado. Defina o PIN antes do primeiro uso.");
            }

            if (acao == "remover")
            {
                if (!existente.HasValue)
                {
                    return Resposta(404, false, "Colaborador nao encontrado.");
                }
                await tabela.DeleteEntityAsync(ParticaoFuncionario, celular, existente.Value!.ETag);
                _logger.LogInformation("Colaborador removido (final {Quatro}).", quatro);
                // Remove a autorizacao, nao o historico: as coordenadas ja gravadas
                // seguem na tabela e serao anonimizadas pelo prazo de retencao.
                return Resposta(200, true, "Colaborador removido. As coordenadas ja enviadas permanecem na base e serao anonimizadas pelo prazo de retencao.");
            }

            return Resposta(400, false, "Acao invalida. Use cadastrar ou remover.");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Falha no Table Storage. Acao {Acao}, final {Quatro}.", acao, quatro);
            return Resposta(502, false, "Falha ao acessar o armazenamento.");
        }
    }

    // Contrato unico para o painel: sempre JSON com ok e mensagem, em qualquer status.
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