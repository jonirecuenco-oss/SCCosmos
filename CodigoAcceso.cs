using System;
using System.Security.Cryptography;

namespace SCCosmos.Sincro;

// ============================================================
// VERIFICACIÓN DE CÓDIGOS DE ACCESO
//
// Los códigos personales no se guardan en claro: el código solo
// contiene su verificación PBKDF2-SHA256 (sal aleatoria +
// 100.000 iteraciones). Así no aparecen en el código publicado
// y no se pueden sacar de ahí con una simple mirada.
// ============================================================

public static class CodigoAcceso
{
    /// <summary>Iteraciones de PBKDF2-SHA256.</summary>
    public const int Iteraciones =
        100000;

    /// <summary>
    /// Comprueba si un código coincide con la verificación guardada.
    /// No distingue mayúsculas (el tema oscuro de Windows escribe en
    /// mayúsculas dentro del campo del código).
    /// </summary>
    public static bool Verificar(
        string codigo,
        string salBase64,
        string claveBase64)
    {
        try
        {
            byte[] sal =
                Convert.FromBase64String(salBase64);

            byte[] esperada =
                Convert.FromBase64String(claveBase64);

            byte[] obtenida =
                Rfc2898DeriveBytes.Pbkdf2(
                    (codigo ?? "")
                        .Trim()
                        .ToLowerInvariant(),
                    sal,
                    Iteraciones,
                    HashAlgorithmName.SHA256,
                    esperada.Length);

            return CryptographicOperations
                .FixedTimeEquals(
                    obtenida,
                    esperada);
        }
        catch
        {
            return false;
        }
    }
}
