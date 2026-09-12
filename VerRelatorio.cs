using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        var fuso = TimeSpan.FromHours(-3);
        var inv = CultureInfo.InvariantCulture;
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html><html lang='pt-BR'><head><meta charset='utf-8'>");
        html.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        html.Append("<meta http-equiv='refresh' content='30'>");
        html.Append("<title>Relatorio de Rastreamento</title>");
        // Leaflet e OpenStreetMap: sem chave de API e sem custo.
        html.Append("<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'>");
        html.Append("<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>");
        html.Append("<style>");
        html.Append("body{font-family:Arial,Helvetica,sans-serif;margin:16px;color:#222}");
        html.Append("h1{font-size:20px;margin:0 0 4px}h2{font-size:16px;margin:24px 0 8px}");
        html.Append("#mapa{height:60vh;min-height:320px;border:1px solid #bbb;border-radius:6px;margin:12px 0}");
        html.Append("table{border-collapse:collapse;width:100%;max-width:760px}");
        html.Append("th,td{border:1px solid #ccc;padding:6px 8px;font-size:14px;text-align:left}");
        html.Append("th{background:#f0f0f0}.rodape{margin-top:24px;font-size:12px;color:#666}");
        html.Append(".legenda span{display:inline-block;margin-right:14px;font-size:13px}");
        html.Append(".bolinha{width:11px;height:11px;border-radius:50%;display:inline-block;margin-right:5px;vertical-align:middle}");
        html.Append("</style></head><body>");
        html.Append("<h1>Diretoria de Ensino Centro Oeste - SEDUC/SP</h1>");
        html.Append("<p>Relatorio de rastreamento de colaboradores. Atualiza automaticamente a cada 30 segundos.</p>");

        if (registros.Count == 0)
        {
            html.Append("<p>Nenhuma coordenada registrada ate o momento.</p></body></html>");
            return new ContentResult { Content = html.ToString(), ContentType = MediaTypeNames.Text.Html, StatusCode = 200 };
        }

        html.Append("<div id='mapa'></div><div class='legenda' id='legenda'></div>");

        // Serie de pontos entregue ao JavaScript. O celular ja vem so com digitos
        // do ReceberCoordenadas, entao nao ha risco de quebrar a string.
        html.Append("<script>var dados=[");
        foreach (var r in registros.OrderBy(x => x.DataHoraUtc))
        {
            html.Append("{c:'").Append(r.Celular)
                .Append("',lat:").Append(r.Latitude.ToString(inv))
                .Append(",lon:").Append(r.Longitude.ToString(inv))
                .Append(",t:'").Append(r.DataHoraUtc.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
                .Append("'},");
        }
        html.Append("];");
        html.Append(@"
var cores=['#1565c0','#c62828','#2e7d32','#6a1b9a','#ef6c00','#00838f','#4e342e'];
var mapa=L.map('mapa');
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',{
  maxZoom:19, attribution:'&copy; OpenStreetMap'
}).addTo(mapa);

var grupos={};
for(var i=0;i<dados.length;i++){
  var d=dados[i];
  if(!grupos[d.c]){grupos[d.c]=[];}
  grupos[d.c].push(d);
}

var todos=[];
var chaves=Object.keys(grupos).sort();
var legenda=document.getElementById('legenda');

for(var k=0;k<chaves.length;k++){
  var cel=chaves[k];
  var cor=cores[k%cores.length];
  var pontos=grupos[cel];
  var linha=[];

  for(var p=0;p<pontos.length;p++){
    var pt=pontos[p];
    linha.push([pt.lat,pt.lon]);
    todos.push([pt.lat,pt.lon]);
    var ultimo=(p===pontos.length-1);
    L.circleMarker([pt.lat,pt.lon],{
      radius: ultimo?9:5,
      color: cor,
      weight: ultimo?3:1,
      fillColor: cor,
      fillOpacity: ultimo?0.95:0.5
    }).addTo(mapa).bindPopup(
      '<b>Colaborador:</b> '+cel+'<br><b>Quando:</b> '+pt.t+
      '<br><b>Coordenadas:</b> '+pt.lat.toFixed(5)+', '+pt.lon.toFixed(5)+
      (ultimo?'<br><i>posicao mais recente</i>':'')
    );
  }

  if(linha.length>1){
    L.polyline(linha,{color:cor,weight:2,opacity:0.6}).addTo(mapa);
  }

  var s=document.createElement('span');
  s.innerHTML=""<i class='bolinha' style='background:""+cor+""'></i>""+cel+' ('+pontos.length+')';
  legenda.appendChild(s);
}

if(todos.length===1){
  mapa.setView(todos[0],16);
}else{
  mapa.fitBounds(todos,{padding:[30,30]});
}
</script>");

        foreach (var grupo in registros.GroupBy(r => r.Celular).OrderBy(g => g.Key))
        {
            html.Append("<h2>Colaborador: ").Append(grupo.Key).Append("</h2>");
            html.Append("<table><tr><th>Data e hora (Brasilia)</th><th>Latitude</th><th>Longitude</th></tr>");
            foreach (var r in grupo.OrderByDescending(x => x.DataHoraUtc))
            {
                html.Append("<tr><td>").Append(r.DataHoraUtc.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
                    .Append("</td><td>").Append(r.Latitude.ToString(inv))
                    .Append("</td><td>").Append(r.Longitude.ToString(inv))
                    .Append("</td></tr>");
            }
            html.Append("</table>");
        }

        html.Append("<p class='rodape'>Total de registros: ").Append(registros.Count)
            .Append(" | Gerado em ").Append(DateTimeOffset.UtcNow.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
            .Append(" | Mapa: Leaflet + OpenStreetMap</p></body></html>");

        return new ContentResult
        {
            Content = html.ToString(),
            ContentType = MediaTypeNames.Text.Html,
            StatusCode = 200
        };
    }
}