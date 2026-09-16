using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace GpsEquipe;

// Incremento 6: painel administrativo. Esta funcao e SOMENTE LEITURA e devolve
// JSON para a pagina admin.html. Nenhum celular sai completo: o painel precisa
// identificar a linha, nao precisa do numero inteiro (minimizacao de dado).
public class VerStatus
{
    private readonly ILogger<VerStatus> _logger;
    private const int JanelaDias = 7;
    private const int DiasRetencao = 90;
    private const string ValorAnonimo = "ANONIMIZADO";

    public VerStatus(ILogger<VerStatus> logger)
    {
        _logger = logger;
    }

    [Function("VerStatus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        var inv = CultureInfo.InvariantCulture;
        var fuso = TimeSpan.FromHours(-3);
        var agoraUtc = DateTimeOffset.UtcNow;

        // A PartitionKey de Coordenadas e a data UTC do envio, entao a janela e
        // calculada em UTC. A hora de Brasilia vai na resposta apenas para exibicao.
        var fimParticao = agoraUtc.UtcDateTime.ToString("yyyy-MM-dd", inv);
        var inicioParticao = agoraUtc.UtcDateTime.AddDays(-(JanelaDias - 1)).ToString("yyyy-MM-dd", inv);
        var limiteLgpd = agoraUtc.UtcDateTime.AddDays(-DiasRetencao).ToString("yyyy-MM-dd", inv);

        var servico = new TableServiceClient(conexao);
        var tbCoord = servico.GetTableClient("Coordenadas");
        var tbFunc = servico.GetTableClient("FuncionariosPermitidos");

        var colaboradores = new List<object>();
        int comTotp = 0, semTotp = 0;
        try
        {
            await foreach (var f in tbFunc.QueryAsync<FuncionarioPermitidoEntidade>("PartitionKey eq 'FUNCIONARIO'"))
            {
                // Incremento 8B: a identificacao agora e o TOTP. Com o PIN extirpado,
                // ter identificacao definida passou a significar ter segredo do autenticador.
                var tem = f.TotpDefinidoEm.HasValue && !string.IsNullOrEmpty(f.TotpSegredo);
                if (tem) { comTotp++; } else { semTotp++; }
                colaboradores.Add(new
                {
                    celular = Mascarar(f.RowKey),
                    temTotp = tem,
                    totpDefinidoEm = f.TotpDefinidoEm.HasValue
                        ? f.TotpDefinidoEm.Value.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm", inv)
                        : (string?)null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler FuncionariosPermitidos.");
            return new StatusCodeResult(502);
        }

        // Consulta por FAIXA de particao: o mesmo truque lexicografico do filtro
        // por periodo. Nao varre a tabela inteira.
        var porDia = new SortedDictionary<string, int>();
        int total = 0;
        object? ultima = null;
        var maiorData = DateTimeOffset.MinValue;
        try
        {
            var filtro = $"PartitionKey ge '{inicioParticao}' and PartitionKey le '{fimParticao}'";
            await foreach (var c in tbCoord.QueryAsync<CoordenadaEntidade>(filtro))
            {
                total++;
                porDia[c.PartitionKey] = porDia.TryGetValue(c.PartitionKey, out var n) ? n + 1 : 1;
                if (c.DataHoraUtc > maiorData)
                {
                    maiorData = c.DataHoraUtc;
                    ultima = new
                    {
                        celular = Mascarar(c.Celular),
                        dataHora = c.DataHoraUtc.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss", inv),
                        latitude = c.Latitude.ToString("F6", inv),
                        longitude = c.Longitude.ToString("F6", inv)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler Coordenadas.");
            return new StatusCodeResult(502);
        }

        // Contagem de conformidade: este filtro NAO usa particao, e varredura.
        // Aceitavel no volume deste projeto, e registro anonimizado nao tem
        // particao propria que permitisse consulta dirigida.
        int anonimizados = 0;
        try
        {
            await foreach (var a in tbCoord.QueryAsync<CoordenadaEntidade>(
                $"Celular eq '{ValorAnonimo}'", select: new[] { "PartitionKey" }))
            {
                anonimizados++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao contar registros anonimizados.");
        }

        // Proxima execucao derivada do CRON conhecido (0 30 3 * * *). Nao e leitura
        // do host: se o CRON mudar no AnonimizarCoordenadas, mude aqui tambem.
        var hojeTresTrinta = new DateTimeOffset(agoraUtc.UtcDateTime.Date.AddHours(3).AddMinutes(30), TimeSpan.Zero);
        var proxima = agoraUtc < hojeTresTrinta ? hojeTresTrinta : hojeTresTrinta.AddDays(1);

        var resposta = new
        {
            servidor = new
            {
                utc = agoraUtc.ToString("yyyy-MM-dd HH:mm:ss", inv) + " UTC",
                brasilia = agoraUtc.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss", inv),
                framework = RuntimeInformation.FrameworkDescription
            },
            colaboradores = new
            {
                total = comTotp + semTotp,
                comTotp,
                semTotp,
                lista = colaboradores
            },
            coordenadas = new
            {
                janelaDias = JanelaDias,
                particaoInicio = inicioParticao,
                particaoFim = fimParticao,
                total,
                porDia = porDia.Select(p => new { dia = p.Key, quantidade = p.Value }).ToArray(),
                ultima
            },
            lgpd = new
            {
                diasRetencao = DiasRetencao,
                limiteAnonimizacao = limiteLgpd,
                registrosAnonimizados = anonimizados,
                cron = "0 30 3 * * *",
                proximaExecucaoUtc = proxima.ToString("yyyy-MM-dd HH:mm", inv) + " UTC",
                proximaExecucaoBrasilia = proxima.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm", inv)
            }
        };

        var json = JsonSerializer.Serialize(resposta, new JsonSerializerOptions { WriteIndented = true });
        return new ContentResult
        {
            Content = json,
            ContentType = "application/json; charset=utf-8",
            StatusCode = 200
        };
    }

    // Mantem os 4 primeiros e os 4 ultimos digitos. Texto que nao e telefone,
    // como ANONIMIZADO, passa inalterado.
    private static string Mascarar(string celular)
    {
        if (string.IsNullOrEmpty(celular)) { return "(vazio)"; }
        if (celular.Length < 9 || !celular.All(char.IsDigit)) { return celular; }
        return celular.Substring(0, 4)
             + new string('*', celular.Length - 8)
             + celular.Substring(celular.Length - 4);
    }
}