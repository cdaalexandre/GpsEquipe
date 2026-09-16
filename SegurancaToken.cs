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