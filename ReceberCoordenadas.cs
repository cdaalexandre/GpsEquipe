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

    public ReceberCoordenadas(ILogger<ReceberCoordenadas> logger)
    {
        _logger = logger;
    }

    // Formato do JSON enviado pelo index.html, identico ao da v1.
    public class CoordenadaRecebida
    {
        public string? Celular { get; set; }
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

        // Normaliza para o formato da RowKey da v1: somente digitos, sem o "+".
        var celular = new string(dados.Celular.Where(char.IsDigit).ToArray());

        // GetEntityIfExistsAsync devolve NullableResponse: testa-se HasValue.
        // Foi o uso de GetEntityAsync aqui que gerou o CS0266 na v1.
        var permitidos = new TableClient(conexao, "FuncionariosPermitidos");
        var consulta = await permitidos.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>("FUNCIONARIO", celular);
        if (!consulta.HasValue)
        {
            _logger.LogWarning("Celular nao cadastrado: {celular}", celular);
            return new StatusCodeResult(403);
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