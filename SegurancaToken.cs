using System;
using System.Security.Cryptography;
using System.Text;

namespace GpsEquipe;

// Incremento 7b: token de sessao assinado, sem estado no servidor.
// Formato: base64url(payload) + "." + base64url(HMACSHA256(payload))
// payload: v1|celular|expiraUnix|carimboSegredoUnix
// O carimbo do segredo TOTP entra no payload de proposito: recadastrar o TOTP do
// colaborador muda o carimbo e invalida de uma vez todos os tokens antigos dele.
// E a unica forma de revogacao que este desenho permite, e sai de graca porque
// quem valida o token ja le a linha do colaborador na tabela.
public static class SegurancaToken
{
    public const int HorasValidade = 8;
    private const string Versao = "v1";

    // Incremento 8A: o token do GESTOR usa prefixo e numero de campos DIFERENTES.
    // "g1|expiraUnix" tem 2 campos, entao nao passa em Validar, que exige 4 campos
    // e prefixo "v1"; e o token do colaborador nao passa em ValidarGestor, que
    // exige 2 campos e "g1". A separacao de papel e ESTRUTURAL e esta dentro da
    // assinatura: nao existe caminho em que um confira o token do outro.
    private const string VersaoGestor = "g1";

    public static (string Token, DateTimeOffset ExpiraEm) Emitir(byte[] chave, string celular, long carimboSegredo)
    {
        var expira = DateTimeOffset.UtcNow.AddHours(HorasValidade);
        var payload = Versao + "|" + celular + "|" + expira.ToUnixTimeSeconds() + "|" + carimboSegredo;
        var bytes = Encoding.UTF8.GetBytes(payload);
        return (ParaBase64Url(bytes) + "." + ParaBase64Url(Assinar(chave, bytes)), expira);
    }

    // Verifica assinatura e validade. Nao consulta nada: e por isso que o token
    // nao pode ser revogado antes de expirar.
    public static bool Validar(byte[] chave, string? token, out string celular, out long carimboSegredo)
    {
        celular = string.Empty;
        carimboSegredo = 0;
        if (string.IsNullOrWhiteSpace(token)) { return false; }

        var partes = token.Split('.');
        if (partes.Length != 2) { return false; }

        byte[] corpo;
        byte[] assinatura;
        try
        {
            corpo = DeBase64Url(partes[0]);
            assinatura = DeBase64Url(partes[1]);
        }
        catch (FormatException) { return false; }

        var esperada = Assinar(chave, corpo);
        // Tempo constante: nao vaza quantos bytes casaram antes de falhar.
        if (!CryptographicOperations.FixedTimeEquals(esperada, assinatura)) { return false; }

        var campos = Encoding.UTF8.GetString(corpo).Split('|');
        if (campos.Length != 4 || campos[0] != Versao) { return false; }
        if (!long.TryParse(campos[2], out var expira)) { return false; }
        if (!long.TryParse(campos[3], out var carimbo)) { return false; }
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expira) { return false; }

        celular = campos[1];
        carimboSegredo = carimbo;
        return true;
    }

    // Incremento 8A: sessao do gestor. Sem celular no payload porque o gestor nao
    // e um numero cadastrado: ele e quem apresentou a chave de acesso.
    public static (string Token, DateTimeOffset ExpiraEm) EmitirGestor(byte[] chave)
    {
        var expira = DateTimeOffset.UtcNow.AddHours(HorasValidade);
        var payload = VersaoGestor + "|" + expira.ToUnixTimeSeconds();
        var bytes = Encoding.UTF8.GetBytes(payload);
        return (ParaBase64Url(bytes) + "." + ParaBase64Url(Assinar(chave, bytes)), expira);
    }

    // Incremento 8A: aceita SOMENTE o token de gestor. Token de colaborador cai
    // no teste de 2 campos e do prefixo, mesmo estando assinado e no prazo.
    public static bool ValidarGestor(byte[] chave, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return false; }

        var partes = token.Split('.');
        if (partes.Length != 2) { return false; }

        byte[] corpo;
        byte[] assinatura;
        try
        {
            corpo = DeBase64Url(partes[0]);
            assinatura = DeBase64Url(partes[1]);
        }
        catch (FormatException) { return false; }

        if (!CryptographicOperations.FixedTimeEquals(Assinar(chave, corpo), assinatura)) { return false; }

        var campos = Encoding.UTF8.GetString(corpo).Split('|');
        if (campos.Length != 2 || campos[0] != VersaoGestor) { return false; }
        if (!long.TryParse(campos[1], out var expira)) { return false; }
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expira;
    }

    // Incremento 8A: comparacao do segredo digitado pelo gestor. Tamanho diferente
    // sai antes e vaza apenas o tamanho, nunca o conteudo.
    public static bool ConferirSegredo(string? digitado, string? esperado)
    {
        if (string.IsNullOrEmpty(digitado) || string.IsNullOrEmpty(esperado)) { return false; }
        var a = Encoding.UTF8.GetBytes(digitado);
        var b = Encoding.UTF8.GetBytes(esperado);
        if (a.Length != b.Length) { return false; }
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static byte[] LerChave(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) { return Array.Empty<byte>(); }
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }

    private static byte[] Assinar(byte[] chave, byte[] dados)
    {
        using var hmac = new HMACSHA256(chave);
        return hmac.ComputeHash(dados);
    }

    // Base64 comum tem +, / e = , que atrapalham em URL e em JSON de log.
    public static string ParaBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] DeBase64Url(string texto)
    {
        var s = texto.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}