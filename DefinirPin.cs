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

// Incremento 5: funcao ADMINISTRATIVA. Define ou redefine o PIN de um
// colaborador ja cadastrado em FuncionariosPermitidos.
// AuthorizationLevel.Function: exige chave, como o VerRelatorio do Incremento 1.
public class DefinirPin
{
    private readonly ILogger<DefinirPin> _logger;

    public DefinirPin(ILogger<DefinirPin> logger)
    {
        _logger = logger;
    }

    public class PinRecebido
    {
        public string? Celular { get; set; }
        public string? Pin { get; set; }
    }

    [Function("DefinirPin")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            _logger.LogError("App setting TabelaConnectionString ausente.");
            return new StatusCodeResult(500);
        }

        PinRecebido? dados;
        try
        {
            dados = await JsonSerializer.DeserializeAsync<PinRecebido>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("JSON invalido.");
        }

        if (dados is null || string.IsNullOrWhiteSpace(dados.Celular) || string.IsNullOrWhiteSpace(dados.Pin))
        {
            return new BadRequestObjectResult("Campos Celular e Pin sao obrigatorios.");
        }

        // Mesma normalizacao do ReceberCoordenadas: somente digitos.
        var celular = new string(dados.Celular.Where(char.IsDigit).ToArray());
        var pin = dados.Pin.Trim();

        // Regra de formato: exatamente 6 digitos. Recusa PIN trivial.
        if (pin.Length != 6 || !pin.All(char.IsDigit))
        {
            return new BadRequestObjectResult("O PIN deve ter exatamente 6 digitos.");
        }
        if (pin.Distinct().Count() == 1)
        {
            return new BadRequestObjectResult("PIN com todos os digitos iguais nao e aceito.");
        }

        var permitidos = new TableClient(conexao, "FuncionariosPermitidos");
        var consulta = await permitidos.GetEntityIfExistsAsync<FuncionarioPermitidoEntidade>("FUNCIONARIO", celular);
        if (!consulta.HasValue)
        {
            _logger.LogWarning("Tentativa de definir PIN de celular nao cadastrado: {celular}", celular);
            return new NotFoundObjectResult("Celular nao cadastrado. Cadastre antes de definir o PIN.");
        }

        var entidade = consulta.Value!;
        var salt = SegurancaPin.GerarSalt();
        entidade.PinSalt = salt;
        entidade.PinHash = SegurancaPin.CalcularHash(pin, salt);
        entidade.PinDefinidoEm = DateTimeOffset.UtcNow;

        // UpdateMode.Merge preserva qualquer propriedade que nao esteja aqui.
        await permitidos.UpdateEntityAsync(entidade, entidade.ETag, TableUpdateMode.Merge);

        _logger.LogInformation("PIN definido para {celular}.", celular);
        return new OkObjectResult("PIN definido com sucesso.");
    }
}