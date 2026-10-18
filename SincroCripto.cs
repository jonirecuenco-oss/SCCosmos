using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SCCosmos.Sincro;

// ============================================================
// CANAL CIFRADO (AES-GCM)
//
// Todo lo que viaja entre dispositivos va cifrado con una clave
// derivada de la "clave de sincronización" que se escribe una
// vez en cada dispositivo. Nadie que no conozca la clave puede
// leer ni escribir en el canal.
//
// Formato de trama:
//   [4 bytes: largo del resto][12 bytes: nonce][cifrado + tag]
// ============================================================

public sealed class CanalCifrado
    : IDisposable
{
    private const int TamanoNonce =
        12;

    private const int TamanoTag =
        16;

    private readonly Stream _flujo;

    private readonly byte[] _clave;


    public CanalCifrado(
        Stream flujo,
        string clave)
    {
        _flujo =
            flujo;

        _clave =
            DerivarClave(
                clave);
    }


    public static byte[] DerivarClave(
        string clave)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            clave ?? "",
            Encoding.UTF8.GetBytes(
                "SCCosmos-Sincro-v1"),
            150_000,
            HashAlgorithmName.SHA256,
            32);
    }


    // ========================================================
    // MENSAJES SUELTOS (canal por carpeta)
    //
    // Cada línea de los archivos de cambios es un mensaje cifrado
    // en Base64: [12 bytes nonce][cifrado][16 bytes tag].
    // ========================================================

    public static string CifrarMensaje(
        string texto,
        byte[] clave)
    {
        byte[] plano =
            Encoding.UTF8.GetBytes(
                texto ?? "");

        byte[] nonce =
            RandomNumberGenerator
                .GetBytes(
                    12);

        byte[] cifrado =
            new byte[plano.Length];

        byte[] tag =
            new byte[16];

        using (var aes =
               new AesGcm(
                   clave,
                   16))
        {
            aes.Encrypt(
                nonce,
                plano,
                cifrado,
                tag);
        }

        byte[] trama =
            new byte[12 + cifrado.Length + 16];

        Buffer.BlockCopy(
            nonce,
            0,
            trama,
            0,
            12);

        Buffer.BlockCopy(
            cifrado,
            0,
            trama,
            12,
            cifrado.Length);

        Buffer.BlockCopy(
            tag,
            0,
            trama,
            12 + cifrado.Length,
            16);

        return Convert.ToBase64String(
            trama);
    }


    /// <summary>
    /// Devuelve null si la línea no se puede descifrar (clave
    /// distinta, línea a medio escribir...). Nunca lanza.
    /// </summary>
    public static string? DescifrarMensaje(
        string linea,
        byte[] clave)
    {
        try
        {
            byte[] trama =
                Convert.FromBase64String(
                    (linea ?? "").Trim());

            if (trama.Length < 12 + 16)
            {
                return null;
            }

            int largoCifrado =
                trama.Length - 12 - 16;

            byte[] plano =
                new byte[largoCifrado];

            using (var aes =
                   new AesGcm(
                       clave,
                       16))
            {
                aes.Decrypt(
                    trama.AsSpan(
                        0,
                        12),
                    trama.AsSpan(
                        12,
                        largoCifrado),
                    trama.AsSpan(
                        12 + largoCifrado,
                        16),
                    plano);
            }

            return Encoding.UTF8
                .GetString(
                    plano);
        }
        catch
        {
            return null;
        }
    }


    // ========================================================
    // ENVIAR
    // ========================================================

    public async Task EnviarAsync(
        string texto,
        CancellationToken token)
    {
        byte[] plano =
            Encoding.UTF8.GetBytes(
                texto ?? "");

        byte[] nonce =
            RandomNumberGenerator
                .GetBytes(
                    TamanoNonce);

        byte[] cifrado =
            new byte[plano.Length];

        byte[] tag =
            new byte[TamanoTag];

        using (var aes =
               new AesGcm(
                   _clave,
                   TamanoTag))
        {
            aes.Encrypt(
                nonce,
                plano,
                cifrado,
                tag);
        }

        int largoResto =
            TamanoNonce +
            cifrado.Length +
            TamanoTag;

        byte[] cabecera =
            new byte[4];

        BinaryPrimitives.WriteInt32BigEndian(
            cabecera,
            largoResto);

        // Se escribe todo junto para evitar tramas partidas.
        byte[] trama =
            new byte[4 + largoResto];

        Buffer.BlockCopy(
            cabecera,
            0,
            trama,
            0,
            4);

        Buffer.BlockCopy(
            nonce,
            0,
            trama,
            4,
            TamanoNonce);

        Buffer.BlockCopy(
            cifrado,
            0,
            trama,
            4 + TamanoNonce,
            cifrado.Length);

        Buffer.BlockCopy(
            tag,
            0,
            trama,
            4 + TamanoNonce + cifrado.Length,
            TamanoTag);

        await _flujo
            .WriteAsync(
                trama,
                token);

        await _flujo
            .FlushAsync(
                token);
    }


    // ========================================================
    // RECIBIR
    //
    // Devuelve null si el otro extremo cerró la conexión.
    // Lanza CryptographicException si la clave no coincide
    // o el mensaje fue manipulado.
    // ========================================================

    public async Task<string?> RecibirAsync(
        CancellationToken token)
    {
        byte[] cabecera =
            new byte[4];

        bool completo =
            await LeerExactoAsync(
                cabecera,
                token);

        if (!completo)
        {
            return null;
        }

        int largoResto =
            BinaryPrimitives
                .ReadInt32BigEndian(
                    cabecera);

        if (largoResto < TamanoNonce + TamanoTag
            ||
            largoResto > 32 * 1024 * 1024)
        {
            throw new CryptographicException(
                "Trama de sincronización con tamaño no válido.");
        }

        byte[] trama =
            new byte[largoResto];

        completo =
            await LeerExactoAsync(
                trama,
                token);

        if (!completo)
        {
            return null;
        }

        ReadOnlySpan<byte> nonce =
            trama.AsSpan(
                0,
                TamanoNonce);

        int largoCifrado =
            largoResto -
            TamanoNonce -
            TamanoTag;

        ReadOnlySpan<byte> cifrado =
            trama.AsSpan(
                TamanoNonce,
                largoCifrado);

        ReadOnlySpan<byte> tag =
            trama.AsSpan(
                TamanoNonce + largoCifrado,
                TamanoTag);

        byte[] plano =
            new byte[largoCifrado];

        using (var aes =
               new AesGcm(
                   _clave,
                   TamanoTag))
        {
            aes.Decrypt(
                nonce,
                cifrado,
                tag,
                plano);
        }

        return Encoding.UTF8
            .GetString(
                plano);
    }


    private async Task<bool> LeerExactoAsync(
        byte[] destino,
        CancellationToken token)
    {
        int leidos =
            0;

        while (leidos < destino.Length)
        {
            int n =
                await _flujo.ReadAsync(
                    destino.AsMemory(
                        leidos),
                    token);

            if (n <= 0)
            {
                return false;
            }

            leidos +=
                n;
        }

        return true;
    }


    public void Dispose()
    {
        try
        {
            _flujo.Dispose();
        }
        catch
        {
        }
    }
}
