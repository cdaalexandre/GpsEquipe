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
    private const int MaximoDeDias = 31;
    private const string NomeCookie = "gpsequipe_gestor";

    public VerRelatorio(ILogger<VerRelatorio> logger)
    {
        _logger = logger;
    }

    // Incremento 8A: Anonymous porque a porta passou a ser o cookie de sessao,
    // nao a chave de funcao. A chave de funcao ficava na query string, ou seja no
    // historico do navegador, nos favoritos e nos logs do proprio Azure.
    // CONSEQUENCIA ASSUMIDA: quem protege esta rota agora e ValidarGestor, meu
    // codigo, e nao mais o host do Functions.
    [Function("VerRelatorio")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        var chaveHmac = SegurancaToken.LerChave(Environment.GetEnvironmentVariable("TokenChaveHmac"));
        // Incremento 8C: a guarda nao exige mais o app setting ChaveGestor. A
        // chave do gestor vive na tabela Configuracao; o app setting sobrevive
        // apenas como ponte de migracao, dentro do ChaveGestorStore.
        if (chaveHmac.Length < 32)
        {
            _logger.LogError("App setting TokenChaveHmac ausente ou curto.");
            return new StatusCodeResult(500);
        }

        // POST: a tela mandou a chave digitada. Confere e emite o cookie.
        if (HttpMethods.IsPost(req.Method))
        {
            var digitada = string.Empty;
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync();
                digitada = form["chave"].ToString();
            }
            if (!await ChaveGestorStore.ValidarAsync(digitada))
            {
                _logger.LogWarning("Entrada de gestor recusada (chave de {N} caracteres).", digitada.Length);
                return Porta(401, "Chave invalida.");
            }
            var sessao = SegurancaToken.EmitirGestor(chaveHmac);
            _logger.LogInformation("Sessao de gestor iniciada, expira {Expira}.", sessao.ExpiraEm);
            req.HttpContext.Response.Cookies.Append(NomeCookie, sessao.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/verrelatorio",
                Expires = sessao.ExpiraEm
            });
            // Incremento 8B: 303 See Other, nao 302. O 303 obriga o navegador a
            // trocar POST por GET; o 302 deixa isso a cargo do costume dele.
            req.HttpContext.Response.Headers["Location"] = "/api/verrelatorio";
            return new StatusCodeResult(303);
        }

        // GET sem cookie valido: nao mostra dado nenhum, mostra a porta.
        if (!SegurancaToken.ValidarGestor(chaveHmac, req.Cookies[NomeCookie]))
        {
            return Porta(401, null);
        }

        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        var inv = CultureInfo.InvariantCulture;
        var fuso = TimeSpan.FromHours(-3);
        var hoje = DateTimeOffset.UtcNow.ToOffset(fuso).Date;
        var avisos = new List<string>();

        // Parametros da query string. Ausentes ou invalidos caem no padrao "hoje".
        var inicio = LerData(req.Query["inicio"], hoje, "inicio", avisos);
        var fim = LerData(req.Query["fim"], hoje, "fim", avisos);

        // Borda invertida: troca em vez de devolver vazio, que confundiria o gestor.
        if (fim < inicio)
        {
            var troca = inicio; inicio = fim; fim = troca;
            avisos.Add("As datas estavam invertidas e foram trocadas.");
        }

        // Limite de partições por consulta. Bordas INCLUSIVAS nas duas pontas.
        var dias = (int)(fim - inicio).TotalDays + 1;
        if (dias > MaximoDeDias)
        {
            fim = inicio.AddDays(MaximoDeDias - 1);
            dias = MaximoDeDias;
            avisos.Add("Intervalo limitado a " + MaximoDeDias + " dias; a data final foi ajustada.");
        }

        // Uma cláusula por partição: consulta de partição, não varredura da tabela.
        // A PartitionKey e a data UTC do envio, mas inicio e fim sao datas de
        // Brasilia. Um dia local cobre dois dias UTC, entao le-se uma particao
        // EXTRA e o excedente e descartado pelo horario local mais abaixo.
        var particoes = Enumerable.Range(0, dias + 1)
            .Select(d => inicio.AddDays(d).ToString("yyyy-MM-dd", inv))
            .ToList();
        var filtro = string.Join(" or ", particoes.Select(p => "PartitionKey eq '" + p + "'"));

        var tabela = new TableClient(conexao, "Coordenadas");
        var registros = new List<CoordenadaEntidade>();
        await foreach (var item in tabela.QueryAsync<CoordenadaEntidade>(filtro))
        {
            registros.Add(item);
        }

        // Descarta o que veio da particao extra mas pertence a outro dia local.
        var lidos = registros.Count;
        registros = registros
            .Where(r => r.DataHoraUtc.ToOffset(fuso).Date >= inicio && r.DataHoraUtc.ToOffset(fuso).Date <= fim)
            .ToList();
        _logger.LogInformation("Consulta de {dias} particao(oes) leu {lidos} e manteve {n} no periodo local.", dias + 1, lidos, registros.Count);

        var sInicio = inicio.ToString("yyyy-MM-dd", inv);
        var sFim = fim.ToString("yyyy-MM-dd", inv);
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html><html lang='pt-BR'><head><meta charset='utf-8'>");
        html.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        html.Append("<title>Relatorio de Rastreamento</title>");
        html.Append("<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'>");
        html.Append("<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>");
        html.Append("<style>");
        html.Append("body{font-family:Arial,Helvetica,sans-serif;margin:16px;color:#222}");
        html.Append("h1{font-size:20px;margin:0 0 4px}h2{font-size:16px;margin:24px 0 8px}");
        html.Append("#mapa{height:55vh;min-height:300px;border:1px solid #bbb;border-radius:6px;margin:12px 0}");
        html.Append("table{border-collapse:collapse;width:100%;max-width:760px}");
        html.Append("th,td{border:1px solid #ccc;padding:6px 8px;font-size:14px;text-align:left}");
        html.Append("th{background:#f0f0f0}.rodape{margin-top:24px;font-size:12px;color:#666}");
        html.Append(".legenda span{display:inline-block;margin-right:14px;font-size:13px}");
        html.Append(".bolinha{width:11px;height:11px;border-radius:50%;display:inline-block;margin-right:5px;vertical-align:middle}");
        html.Append(".filtro{background:#eef1f4;padding:12px;border-radius:6px;margin-bottom:12px}");
        html.Append(".filtro label{font-size:13px;margin-right:4px}");
        html.Append(".filtro input{padding:6px;font-size:14px;border:1px solid #bbb;border-radius:4px;margin-right:10px}");
        html.Append(".filtro button{padding:7px 14px;font-size:14px;border:0;border-radius:4px;background:#1565c0;color:#fff;cursor:pointer;margin-right:6px}");
        html.Append(".filtro a{font-size:13px;color:#1565c0}");
        html.Append(".aviso{background:#fff4e5;color:#8a4b00;padding:8px 10px;border-radius:4px;font-size:13px;margin-bottom:10px}");
        html.Append(".vazio{background:#eef1f4;padding:14px;border-radius:6px}");
        html.Append("</style></head><body>");
        html.Append("<h1>Diretoria de Ensino Centro Oeste - SEDUC/SP</h1>");
        html.Append("<p>Relatorio de rastreamento de colaboradores.</p>");

        // Formulario GET: o proprio filtro vira URL compartilhavel.
        html.Append("<div class='filtro'><form method='get'>");
        html.Append("<label for='inicio'>De</label><input type='date' id='inicio' name='inicio' value='").Append(sInicio).Append("'>");
        html.Append("<label for='fim'>Ate</label><input type='date' id='fim' name='fim' value='").Append(sFim).Append("'>");
        html.Append("<button type='submit'>Filtrar</button>");
        html.Append("<a href='?'>hoje</a></form></div>");

        foreach (var a in avisos)
        {
            html.Append("<div class='aviso'>").Append(a).Append("</div>");
        }

        if (registros.Count == 0)
        {
            html.Append("<div class='vazio'>Nenhuma coordenada registrada de ")
                .Append(inicio.ToString("dd/MM/yyyy", inv)).Append(" a ")
                .Append(fim.ToString("dd/MM/yyyy", inv)).Append(".</div>");
            html.Append("<p class='rodape'>Particoes consultadas: ").Append(dias)
                .Append(" | Gerado em ").Append(DateTimeOffset.UtcNow.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
                .Append("</p></body></html>");
            return new ContentResult { Content = html.ToString(), ContentType = MediaTypeNames.Text.Html, StatusCode = 200 };
        }

        html.Append("<div id='mapa'></div><div class='legenda' id='legenda'></div>");

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
            html.Append("<h2>Colaborador: ").Append(grupo.Key)
                .Append(" — ").Append(grupo.Count()).Append(" ponto(s)</h2>");
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

        html.Append("<p class='rodape'>Periodo: ").Append(inicio.ToString("dd/MM/yyyy", inv))
            .Append(" a ").Append(fim.ToString("dd/MM/yyyy", inv))
            .Append(" | Particoes consultadas: ").Append(dias)
            .Append(" | Total de registros: ").Append(registros.Count)
            .Append(" | Gerado em ").Append(DateTimeOffset.UtcNow.ToOffset(fuso).ToString("dd/MM/yyyy HH:mm:ss"))
            .Append(" | Mapa: Leaflet + OpenStreetMap</p></body></html>");

        return new ContentResult
        {
            Content = html.ToString(),
            ContentType = MediaTypeNames.Text.Html,
            StatusCode = 200
        };
    }

    // Incremento 8A: a tela de entrada. Sai com 401 de proposito: o aceite do
    // Roteiro (P1) testa "relatorio sem chave -> 401" e continua valendo.
    // O campo e type=password para nao aparecer na tela nem em gravacao de video.
    private static IActionResult Porta(int status, string? erro)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang='pt-BR'><head><meta charset='utf-8'>");
        html.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        html.Append("<title>GpsEquipe - Acesso do gestor</title><style>");
        html.Append("body{font-family:Arial,Helvetica,sans-serif;margin:0;background:#eef1f4;color:#222;");
        html.Append("display:flex;min-height:100vh;align-items:center;justify-content:center}");
        html.Append(".cartao{background:#fff;padding:28px;border-radius:8px;max-width:380px;width:90%;");
        html.Append("box-shadow:0 1px 4px rgba(0,0,0,.18)}");
        html.Append("h1{font-size:18px;margin:0 0 4px}p{font-size:13px;color:#555;margin:0 0 18px}");
        html.Append("label{font-size:13px;display:block;margin-bottom:6px}");
        html.Append("input{width:100%;padding:10px;font-size:15px;border:1px solid #bbb;border-radius:4px;box-sizing:border-box}");
        html.Append("button{width:100%;margin-top:14px;padding:11px;font-size:15px;border:0;border-radius:4px;");
        html.Append("background:#1565c0;color:#fff;cursor:pointer}");
        html.Append(".erro{background:#fdecea;color:#8a1c12;padding:9px 11px;border-radius:4px;font-size:13px;margin-bottom:14px}");
        html.Append(".nota{font-size:12px;color:#777;margin:16px 0 0}");
        html.Append("</style></head><body><div class='cartao'>");
        html.Append("<h1>Diretoria de Ensino Centro Oeste - SEDUC/SP</h1>");
        html.Append("<p>Relatorio de rastreamento. Acesso restrito.</p>");
        if (!string.IsNullOrEmpty(erro))
        {
            html.Append("<div class='erro'>").Append(erro).Append("</div>");
        }
        html.Append("<form method='post' action='/api/verrelatorio'>");
        html.Append("<label for='chave'>Chave de acesso</label>");
        html.Append("<input type='password' id='chave' name='chave' autocomplete='off' autofocus>");
        html.Append("<button type='submit'>Entrar</button></form>");
        html.Append("<p class='nota'>A chave e fornecida pelo administrador. A sessao vale 8 horas.</p>");
        html.Append("</div></body></html>");
        return new ContentResult
        {
            Content = html.ToString(),
            ContentType = MediaTypeNames.Text.Html,
            StatusCode = status
        };
    }

    // Aceita yyyy-MM-dd (formato do input type=date). Vazio usa o padrao em silencio;
    // valor presente mas invalido usa o padrao E avisa na pagina.
    private static DateTime LerData(string? valor, DateTime padrao, string nome, List<string> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return padrao;
        }
        if (DateTime.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
        {
            return d.Date;
        }
        avisos.Add("Data de " + nome + " invalida; usando " + padrao.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ".");
        return padrao;
    }
}