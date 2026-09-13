using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace GpsEquipe;

public class AnonimizarCoordenadas
{
    private readonly ILogger<AnonimizarCoordenadas> _logger;

    // Retencao: 90 dias. Depois disso o vinculo com a pessoa e removido.
    private const int DiasDeRetencao = 90;

    // Valor final, sem hash: hash de celular e quebravel por forca bruta em
    // minutos, o que seria pseudonimizacao disfarcada de anonimizacao.
    private const string ValorAnonimo = "ANONIMIZADO";

    public AnonimizarCoordenadas(ILogger<AnonimizarCoordenadas> logger)
    {
        _logger = logger;
    }

    // 3h30 UTC todos os dias (00h30 em Brasilia).
    [Function("AnonimizarCoordenadas")]
    public async Task Run([TimerTrigger("0 30 3 * * *")] TimerInfo timer)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente. Anonimizacao abortada.");
            return;
        }

        var limite = DateTime.UtcNow.Date.AddDays(-DiasDeRetencao)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Comparacao lexicografica: funciona porque yyyy-MM-dd e ordenavel como texto.
        // Filtro em PartitionKey, nao em propriedade comum: consulta de particao.
        var filtro = "PartitionKey lt '" + limite + "'";

        var tabela = new TableClient(conexao, "Coordenadas");

        var alvos = new List<CoordenadaEntidade>();
        await foreach (var e in tabela.QueryAsync<CoordenadaEntidade>(filtro))
        {
            alvos.Add(e);
        }

        var anonimizados = 0;
        var jaAnonimos = 0;
        var falhas = 0;

        foreach (var e in alvos)
        {
            // Idempotencia: rodar duas vezes no mesmo dia nao causa dano.
            if (e.Celular == ValorAnonimo)
            {
                jaAnonimos++;
                continue;
            }

            try
            {
                // Entidade MINIMA: so as chaves e o campo a mudar. Merge toca apenas
                // o que vai no payload, entao Latitude, Longitude e DataHoraUtc nem
                // sao reenviados. Evita 400 OutOfRangeInput quando algum campo do
                // registro esta ausente ou fora de faixa.
                var patch = new TableEntity(e.PartitionKey, e.RowKey)
                {
                    { "Celular", ValorAnonimo }
                };
                await tabela.UpdateEntityAsync(patch, e.ETag, TableUpdateMode.Merge);
                anonimizados++;
            }
            catch (Exception ex)
            {
                falhas++;
                _logger.LogError(ex, "Falha ao anonimizar {p}/{r}.", e.PartitionKey, e.RowKey);
            }
        }

        _logger.LogInformation(
            "Anonimizacao LGPD concluida. Limite {limite} ({dias} dias). Examinados {ex}, anonimizados {an}, ja anonimos {ja}, falhas {fa}.",
            limite, DiasDeRetencao, alvos.Count, anonimizados, jaAnonimos, falhas);
    }
}