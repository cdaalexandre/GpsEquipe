using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace GpsEquipe;

// Incremento 7: TOTP conforme RFC 6238, compativel com o Microsoft Authenticator.
// O algoritmo foi conferido contra os vetores da norma ANTES de virar codigo:
// com o segredo "12345678901234567890", o contador 1 gera 287082 e o contador
// 37037036 gera 081804. Depois foi conferido contra o app real no celular.
public static class SegurancaTotp
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const int BytesSegredo = 20;      // 160 bits, o minimo da RFC 4226
    public const int Periodo = 30;
    public const int Digitos = 6;
    public const int Tolerancia = 1;         // aceita a janela anterior e a seguinte

    public static string GerarSegredoBase32()
    {
        var bytes = new byte[BytesSegredo];
        RandomNumberGenerator.Fill(bytes);
        return ParaBase32(bytes);
    }

    public static string ParaBase32(byte[] bytes)
    {
        var bits = new StringBuilder();
        foreach (var b in bytes) { bits.Append(Convert.ToString(b, 2).PadLeft(8, '0')); }
        var saida = new StringBuilder();
        for (var i = 0; i + 5 <= bits.Length; i += 5)
        {
            saida.Append(Alfabeto[Convert.ToInt32(bits.ToString(i, 5), 2)]);
        }
        return saida.ToString();
    }

    public static byte[] DeBase32(string texto)
    {
        var bits = new StringBuilder();
        foreach (var c in texto.ToUpperInvariant())
        {
            var i = Alfabeto.IndexOf(c);
            if (i >= 0) { bits.Append(Convert.ToString(i, 2).PadLeft(5, '0')); }
        }
        var bytes = new List<byte>();
        for (var p = 0; p + 8 <= bits.Length; p += 8)
        {
            bytes.Add(Convert.ToByte(bits.ToString(p, 8), 2));
        }
        return bytes.ToArray();
    }

    public static long JanelaAtual()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Periodo;
    }

    public static string Calcular(byte[] segredo, long contador)
    {
        var b = BitConverter.GetBytes(contador);
        if (BitConverter.IsLittleEndian) { Array.Reverse(b); }
        using var hmac = new HMACSHA1(segredo);
        var hash = hmac.ComputeHash(b);
        var off = hash[hash.Length - 1] & 0x0f;
        var bin = ((hash[off] & 0x7f) << 24)
                | ((hash[off + 1] & 0xff) << 16)
                | ((hash[off + 2] & 0xff) << 8)
                | (hash[off + 3] & 0xff);
        var mod = (int)Math.Pow(10, Digitos);
        return (bin % mod).ToString(new string('0', Digitos));
    }

    // Retorna a janela que aceitou o codigo, ou -1 se nenhuma aceitou.
    // ultimaJanelaUsada: janelas menores ou iguais sao recusadas (anti-replay).
    public static long Validar(string segredoBase32, string codigo, long ultimaJanelaUsada)
    {
        if (string.IsNullOrWhiteSpace(segredoBase32)) { return -1; }
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length != Digitos) { return -1; }
        foreach (var c in codigo) { if (!char.IsDigit(c)) { return -1; } }

        var segredo = DeBase32(segredoBase32);
        if (segredo.Length < BytesSegredo) { return -1; }

        var atual = JanelaAtual();
        var informado = Encoding.ASCII.GetBytes(codigo);
        for (var d = -Tolerancia; d <= Tolerancia; d++)
        {
            var janela = atual + d;
            if (janela <= ultimaJanelaUsada) { continue; }
            var esperado = Encoding.ASCII.GetBytes(Calcular(segredo, janela));
            // Comparacao de tempo constante: nao vaza por quanto tempo demorou.
            if (CryptographicOperations.FixedTimeEquals(esperado, informado)) { return janela; }
        }
        return -1;
    }

    public static string MontarUri(string emissor, string conta, string segredoBase32)
    {
        return "otpauth://totp/" + Uri.EscapeDataString(emissor) + ":" + Uri.EscapeDataString(conta)
             + "?secret=" + segredoBase32
             + "&issuer=" + Uri.EscapeDataString(emissor)
             + "&algorithm=SHA1&digits=" + Digitos + "&period=" + Periodo;
    }
}