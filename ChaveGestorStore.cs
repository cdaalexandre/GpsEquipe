using Azure;
using Azure.Data.Tables;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GpsEquipe;

// Incremento 8C: a chave de acesso do gestor sai do app setting ChaveGestor e
// passa a viver na tabela Configuracao, guardada como HASH.
//
// POR QUE SAIU DO APP SETTING: gravar app setting e plano de GERENCIAMENTO e
// REINICIA a Function App. A requisicao que grava morre no restart, entao a
// troca nunca poderia ser feita por tela. Escrever em tabela e plano de DADOS:
// mesma credencial que o codigo ja usa, e sem restart.
//
// POR QUE PODE SER HASH, ao contrario do segredo TOTP: aqui o servidor apenas
// COMPARA o que o gestor digitou. No TOTP ele precisa RECALCULAR o codigo a
// cada 30s, e para isso precisa do valor original (ver Entidades.cs).
//
// POR QUE SHA-256 SEM SALT BASTA: a chave e sorteada, 32 bytes. Nao existe
// dicionario a percorrer. Salt e derivacao lenta servem contra senha humana.
public static class ChaveGestorStore
{
    private const string NomeTabela = "Configuracao";
    private const string Particao   = "CONFIG";
    private const string Linha      = "ChaveGestor";

    private static TableClient Cliente()
    {
        var conexao = Environment.GetEnvironmentVariable("TabelaConnectionString");
        if (string.IsNullOrWhiteSpace(conexao))
        {
            throw new InvalidOperationException("App setting TabelaConnectionString ausente.");
        }
        return new TableClient(conexao, NomeTabela);
    }

    private static string Hash(string valor)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(valor)));
    }

    // NullableResponse: checar HasValue ANTES de tocar em Value. Foi este tipo
    // que gerou o CS0266 no log da v1. O catch cobre a tabela ainda nao existir,
    // antes da primeira troca.
    private static async Task<TableEntity?> LerAsync()
    {
        try
        {
            var r = await Cliente().GetEntityIfExistsAsync<TableEntity>(Particao, Linha);
            return r.HasValue ? r.Value : null;
        }
        catch (RequestFailedException)
        {
            return null;
        }
    }

    private static async Task<string?> HashEsperadoAsync()
    {
        var e = await LerAsync();
        if (e != null)
        {
            var h = e.GetString("Hash");
            if (!string.IsNullOrWhiteSpace(h)) { return h; }
        }

        // Ponte de migracao: enquanto a tabela nao tiver hash, vale o app setting
        // antigo em claro. A primeira troca grava o hash e mata este caminho.
        var antiga = Environment.GetEnvironmentVariable("ChaveGestor");
        return string.IsNullOrWhiteSpace(antiga) ? null : Hash(antiga);
    }

    public static async Task<bool> ValidarAsync(string? digitada)
    {
        if (string.IsNullOrEmpty(digitada)) { return false; }
        var esperado = await HashEsperadoAsync();
        if (esperado == null) { return false; }
        // Os dois lados sao base64 de SHA-256: 44 caracteres sempre. O atalho de
        // tamanho de ConferirSegredo nunca dispara, entao a comparacao e toda em
        // tempo constante.
        return SegurancaToken.ConferirSegredo(Hash(digitada), esperado);
    }

    // 43 caracteres base64url, o mesmo formato que o Admin.ps1 produzia.
    // O valor em claro existe SO neste retorno: a tabela recebe apenas o hash.
    public static async Task<string> RotacionarAsync()
    {
        var cliente = Cliente();
        await cliente.CreateIfNotExistsAsync();

        var chave = SegurancaToken.ParaBase64Url(RandomNumberGenerator.GetBytes(32));

        await cliente.UpsertEntityAsync(new TableEntity(Particao, Linha)
        {
            { "Hash", Hash(chave) },
            { "TrocadaEm", DateTimeOffset.UtcNow }
        }, TableUpdateMode.Replace);

        return chave;
    }

    // Para o painel: estado, sem expor nada.
    public static async Task<(bool EmTabela, DateTimeOffset? TrocadaEm)> EstadoAsync()
    {
        var e = await LerAsync();
        if (e == null || string.IsNullOrWhiteSpace(e.GetString("Hash")))
        {
            return (false, null);
        }
        return (true, e.GetDateTimeOffset("TrocadaEm"));
    }
}