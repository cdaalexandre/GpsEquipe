using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace GpsEquipe;

public class VerRelatorio
{
    private readonly ILogger<VerRelatorio> _logger;

    public VerRelatorio(ILogger<VerRelatorio> logger)
    {
        _logger = logger;
    }

    [Function("VerRelatorio")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        var tabela = new TableClient(conexao, "Coordenadas");
        var registros = new List<CoordenadaEntidade>();
        await foreach (var item in tabela.QueryAsync<CoordenadaEntidade>())
        {
            registros.Add(item);
        }

        // Fuso de Brasilia aplicado na exibicao; o armazenamento permanece em UTC.
        var fuso = TimeSpan.FromHours(-3);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<meta http-equiv=\"refresh\" content=\"30\">");
        html.Append("<title>Relatorio de Rastreamento</title><style>");
        html.Append("body{font-family:Arial,Helvetica,sans-serif;margin:16px;color:#222}");
        html.Append("h1{font-size:20px}h2{font-size:16px;margin:24px 0 8px}");
        html.Append("table{border-collapse:collapse;width:100%;max-width:720px}");
        html.Append("th,td{border:1px solid #ccc;padding:6px 8px;font-size:14px;text-align:left}");
        html.Append("th{background:#f0f0f0}.rodape{margin-top:24px;font-size:12px;color:#666}");
        html.Append("</style></head><body>");
        html.Append("<h1>Diretoria de Ensino Centro Oeste - SEDUC/SP</h1>");
        html.Append("<p>Relatorio de rastreamento de colaboradores. Atualiza automaticamente a cada 30 segundos.</p>");

        if (registros.Count == 0)
        {
            html.Append("<p>Nenhuma coordenada registrada ate o momento.</p>");
        }
        else
        {
            foreach (var grupo in registros.GroupBy(r => r.Celular).OrderBy(g => g.Key))
            {
                html.Append("<h2>Colaborador: ").Append(grupo.Key).Append("</h2>");
                html.Append("<table><tr><th>Data e hora (Brasilia)</th><th>Latitude</th><th>Longitude</th><th>Mapa</th></tr>");
                foreach (var r in grupo.OrderByDescending(x => x.DataHoraUtc))
                {
                    var local = r.DataHoraUtc.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss");
                    var lat = r.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lon = r.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    html.Append("<tr><td>").Append(local).Append("</td><td>").Append(lat)
                        .Append("</td><td>").Append(lon).Append("</td><td>")
                        .Append("<a target=\"_blank\" href=\"https://www.google.com/maps?q=")
                        .Append(lat).Append(",").Append(lon).Append("\">abrir</a></td></tr>");
                }
                html.Append("</table>");
            }
        }

        html.Append("<p class=\"rodape\">Total de registros: ").Append(registros.Count)
            .Append(" | Gerado em ").Append(DateTimeOffset.UtcNow.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
            .Append("</p></body></html>");

        return new ContentResult
        {
            Content = html.ToString(),
            ContentType = MediaTypeNames.Text.Html,
            StatusCode = 200
        };
    }
}