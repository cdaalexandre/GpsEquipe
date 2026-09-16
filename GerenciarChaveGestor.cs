using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace GpsEquipe;

// Incremento 8C: funcao ADMINISTRATIVA da chave de acesso do gestor. Duas acoes:
//   estado -> diz se a chave ja vive na tabela e quando foi trocada. Nao expoe
//             nada: nem a chave, nem o hash dela.
//   trocar -> gera chave NOVA e a devolve UMA VEZ. A anterior morre na hora.
//
// POR QUE ESTA FUNCAO EXISTE: antes, trocar a chave do gestor exigia gravar app
// setting, que REINICIA a Function App. A requisicao morria no restart, entao a
// operacao so era possivel pelo Admin.ps1. Agora a escrita e em tabela.
//
// NAO EXISTE ACAO DE LEITURA DA CHAVE ATUAL. A tabela guarda hash: nem esta
// funcao consegue recuperar o valor. Gestor novo entrando na equipe exige
// trocar a chave para todos, o que o ModoDeUso ja documentava como consequencia
// de haver uma unica chave compartilhada.
public class GerenciarChaveGestor
{
    private readonly ILogger<GerenciarChaveGestor> _logger;

    // Confirmacao digitada, no mesmo espirito do Admin.ps1: operacao destrutiva
    // nao acontece por clique unico nem por corpo JSON incompleto.
    private const string PalavraConfirmacao = "TROCAR";

    public GerenciarChaveGestor(ILogger<GerenciarChaveGestor> logger)
    {
        _logger = logger;
    }

    public class PedidoRecebido
    {
        public string? Acao { get; set; }
        public string? Confirmacao { get; set; }
    }

    [Function("GerenciarChaveGestor")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
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

        try
        {
            if (acao == "estado")
            {
                var (emTabela, trocadaEm) = await ChaveGestorStore.EstadoAsync();
                var corpo = new
                {
                    ok = true,
                    mensagem = emTabela
                        ? "A chave vive na tabela Configuracao."
                        : "A chave ainda nao foi trocada: vale o app setting antigo, em claro.",
                    emTabela,
                    trocadaEm = trocadaEm?.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm:ss")
                };
                return Json(200, corpo);
            }

            if (acao == "trocar")
            {
                var confirmacao = (dados?.Confirmacao ?? string.Empty).Trim();
                if (confirmacao != PalavraConfirmacao)
                {
                    return Resposta(400, false,
                        "Confirmacao ausente ou incorreta. Envie Confirmacao com a palavra " + PalavraConfirmacao + ".");
                }

                var chave = await ChaveGestorStore.RotacionarAsync();

                // O log registra o TAMANHO, nunca a chave.
                _logger.LogWarning("Chave de acesso do gestor trocada ({N} caracteres). Sessoes abertas seguem validas ate vencer.", chave.Length);

                var corpo = new
                {
                    ok = true,
                    mensagem = "Chave nova gerada. Entregue aos gestores agora: nao sera exibida de novo. A anterior parou de funcionar. Sessoes ja abertas continuam valendo ate vencer, no maximo 8 horas.",
                    chave,
                    caracteres = chave.Length
                };
                return Json(200, corpo);
            }

            return Resposta(400, false, "Acao invalida. Use estado ou trocar.");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Configuracao ausente na acao {Acao}.", acao);
            return Resposta(500, false, "Configuracao do servidor incompleta.");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Falha no Table Storage. Acao {Acao}.", acao);
            return Resposta(502, false, "Falha ao acessar o armazenamento.");
        }
    }

    // Mesmo contrato do GerenciarColaborador: sempre JSON com ok e mensagem.
    private static ContentResult Resposta(int status, bool ok, string mensagem)
    {
        return Json(status, new { ok, mensagem });
    }

    private static ContentResult Json(int status, object corpo)
    {
        return new ContentResult
        {
            Content = JsonSerializer.Serialize(corpo),
            ContentType = "application/json; charset=utf-8",
            StatusCode = status
        };
    }
}