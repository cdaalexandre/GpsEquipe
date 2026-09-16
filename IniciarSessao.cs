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

// Incremento 7b: troca um codigo do Microsoft Authenticator por um token de
// sessao. AuthorizationLevel.Anonymous pelo mesmo motivo do ReceberCoordenadas:
// quem chama e o index.html publico. A protecao e o proprio TOTP.
// FALHA FECHADA: celular malformado, nao cadastrado, sem TOTP, codigo errado e
// codigo reusado devolvem TODOS o mesmo 403 com o mesmo texto. O motivo real vai
// so para o log, como no Incremento 5.
public class IniciarSessao
{
    private readonly ILogger<IniciarSessao> _logger;
    private const string ParticaoFuncionario = "FUNCIONARIO";
    private const string MensagemUnica = "Identificacao invalida.";

    public IniciarSessao(ILogger<IniciarSessao> logger)
    {
        _logger = logger;
    }

    public class PedidoRecebido
    {
        public string? Celular { get; set; }
        public string? Codigo { get; set; }
    }

    [Function("IniciarSessao")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        var chave = SegurancaToken.LerChave(Environment.GetEnvironmentVariable("TokenChaveHmac"));
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }
        if (chave.Length < 32)
        {
            _logger.LogError("App setting TokenChaveHmac ausente ou curta demais.");
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
            return Recusar("corpo JSON invalido", "----");
        }

        var celular = new string((dados?.Celular ?? string.Empty).Where(char.IsDigit).ToArray());
        if (celular.Length < 12)
        {
            return Recusar("celular malformado", "----");
        }
        var quatro = celular.Substring(celular.Length - 4);

        var tabela = new TableClient(conexao, "FuncionariosPermitidos");
        try
        {
            var achado = await tabela.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>(
                ParticaoFuncionario, celular);
            if (!achado.HasValue)
            {
                return Recusar("celular nao cadastrado", quatro);
            }
            var func = achado.Value!;
            if (string.IsNullOrWhiteSpace(func.TotpSegredo))
            {
                return Recusar("colaborador sem TOTP cadastrado", quatro);
            }

            // A ultima janela usada entra aqui: o mesmo codigo nao serve duas vezes.
            var janela = SegurancaTotp.Validar(func.TotpSegredo, dados?.Codigo ?? string.Empty, func.TotpUltimaJanela);
            if (janela < 0)
            {
                return Recusar("codigo invalido ou ja usado", quatro);
            }

            await tabela.UpdateEntityAsync(new FuncionarioPermitidoEntidade
            {
                PartitionKey = ParticaoFuncionario,
                RowKey = celular,
                TotpUltimaJanela = janela
            }, ETag.All, TableUpdateMode.Merge);

            var carimbo = func.TotpDefinidoEm.HasValue ? func.TotpDefinidoEm.Value.ToUnixTimeSeconds() : 0;
            var emitido = SegurancaToken.Emitir(chave, celular, carimbo);

            _logger.LogInformation("Sessao iniciada (final {Quatro}, janela {Janela}, expira {Expira}).",
                quatro, janela, emitido.ExpiraEm);

            var corpo = new
            {
                ok = true,
                token = emitido.Token,
                expiraEmUtc = emitido.ExpiraEm.ToString("o"),
                validadeHoras = SegurancaToken.HorasValidade
            };
            return new ContentResult
            {
                Content = JsonSerializer.Serialize(corpo),
                ContentType = "application/json; charset=utf-8",
                StatusCode = 200
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Falha no Table Storage ao iniciar sessao (final {Quatro}).", quatro);
            return new StatusCodeResult(502);
        }
    }

    private IActionResult Recusar(string motivo, string quatro)
    {
        _logger.LogInformation("Sessao recusada (final {Quatro}): {Motivo}.", quatro, motivo);
        return new ContentResult
        {
            Content = MensagemUnica,
            ContentType = "text/plain; charset=utf-8",
            StatusCode = 403
        };
    }
}