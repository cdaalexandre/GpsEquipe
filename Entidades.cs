using Azure;
using Azure.Data.Tables;
using System;

namespace GpsEquipe;

// Tabela Coordenadas. PartitionKey = data do envio; RowKey = id unico do registro.
// O celular fica como propriedade comum, e NAO como chave. Foi o que permitiu a
// anonimizacao por update simples no Incremento 4, removida na reducao de nov/2026.
public class CoordenadaEntidade : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Celular { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset DataHoraUtc { get; set; }
}

// Tabela FuncionariosPermitidos, esquema herdado da v1 (Secao 11):
// PartitionKey = "FUNCIONARIO", RowKey = celular sem o sinal de mais.
// Reducao (nov/2026): os campos Totp* sairam da classe. O Table Storage nao tem
// esquema fixo: TotpSegredo continua nas linhas antigas ate ser eliminado.
public class FuncionarioPermitidoEntidade : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}