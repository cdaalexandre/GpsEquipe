using System;
using System.Security.Cryptography;

namespace GpsEquipe;

// Incremento 5: geracao e verificacao de PIN do colaborador.
// O PIN nunca e gravado. Grava-se um salt aleatorio por pessoa e o hash
// PBKDF2-SHA256 derivado do PIN com esse salt.
public static class SegurancaPin
{
    private const int Iteracoes = 100_000;
    private const int TamanhoSaltBytes = 16;
    private const int TamanhoHashBytes = 32;

    public static string GerarSalt()
    {
        var salt = RandomNumberGenerator.GetBytes(TamanhoSaltBytes);
        return Convert.ToBase64String(salt);
    }

    public static string CalcularHash(string pin, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iteracoes,
            HashAlgorithmName.SHA256,
            TamanhoHashBytes);
        return Convert.ToBase64String(hash);
    }

    // Comparacao em tempo fixo: nao vaza informacao pelo tempo de resposta.
    public static bool Conferir(string pin, string saltBase64, string hashEsperadoBase64)
    {
        if (string.IsNullOrWhiteSpace(pin) ||
            string.IsNullOrWhiteSpace(saltBase64) ||
            string.IsNullOrWhiteSpace(hashEsperadoBase64))
        {
            return false;
        }

        byte[] esperado;
        try { esperado = Convert.FromBase64String(hashEsperadoBase64); }
        catch (FormatException) { return false; }

        var calculado = Convert.FromBase64String(CalcularHash(pin, saltBase64));
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}