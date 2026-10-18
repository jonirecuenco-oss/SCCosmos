using System;
using System.Numerics;

using Android.Graphics;

namespace SCCosmos.Mobile.Platforms.Android;


// =============================================================
// COMPARADOR VISUAL DE FOTOGRAFÍAS JW HUB / SQLITE
//
// No compara Base64 ni bytes JPEG directamente.
//
// Windows puede volver a codificar una fotografía y Android
// puede descargar los bytes originales de JW Hub. La imagen
// visual puede ser la misma aunque el Base64 sea diferente.
//
// Para evitar falsos conflictos utilizamos dHash de 64 bits.
// =============================================================

public static class JwHubPhotoComparer
{
    // Un JPEG recodificado o redimensionado de la misma foto
    // normalmente queda muy por debajo de este valor.
    //
    // El umbral es deliberadamente conservador para la primera
    // prueba real. Si hiciera falta, podremos ajustarlo con
    // fotografías reales sin tocar el resto del sincronizador.
    private const int UmbralDistancia =
        10;


    // =========================================================
    // COMPARAR
    // =========================================================

    public static bool SonVisualmenteIguales(
        string? fotoBaseDatos,
        string? fotoJwHub)
    {
        string db =
            fotoBaseDatos?.Trim()
            ?? "";


        string hub =
            fotoJwHub?.Trim()
            ?? "";


        // JW Hub sin fotografía:
        // no proponemos borrar una foto local.
        if (string.IsNullOrWhiteSpace(
                hub))
        {
            return true;
        }


        // JW Hub aparentemente tiene foto y SQLite no.
        //
        // Solo generamos conflicto si lo recibido desde JW Hub
        // es realmente una imagen válida.
        //
        // Esta comprobación es una segunda barrera de seguridad:
        // evita falsos conflictos si por cualquier cambio del DOM
        // llegara aquí una URL/página/valor que no sea una foto.
        if (string.IsNullOrWhiteSpace(
                db))
        {
            return
                !IntentarCrearDHash(
                    hub,
                    out _);
        }


        // Si por casualidad ya son idénticas,
        // evitamos incluso decodificar.
        if (string.Equals(
                db,
                hub,
                StringComparison.Ordinal))
        {
            return true;
        }


        if (!IntentarCrearDHash(
                db,
                out ulong hashDb)
            ||
            !IntentarCrearDHash(
                hub,
                out ulong hashHub))
        {
            // Si alguna imagen no pudiera decodificarse,
            // no generamos un conflicto dudoso.
            //
            // La ausencia total en SQLite ya se trató arriba.
            return true;
        }


        int distancia =
            BitOperations.PopCount(
                hashDb ^ hashHub);


        return
            distancia <=
            UmbralDistancia;
    }


    // =========================================================
    // CREAR DHASH
    //
    // 9 x 8 píxeles -> 8 comparaciones horizontales por fila
    // -> 64 bits.
    // =========================================================

    private static bool IntentarCrearDHash(
        string base64,
        out ulong hash)
    {
        hash =
            0;


        try
        {
            string limpio =
                ExtraerBase64(
                    base64);


            if (string.IsNullOrWhiteSpace(
                    limpio))
            {
                return false;
            }


            byte[] bytes =
                Convert.FromBase64String(
                    limpio);


            if (bytes.Length == 0)
            {
                return false;
            }


            Bitmap? original =
                BitmapFactory.DecodeByteArray(
                    bytes,
                    0,
                    bytes.Length);


            if (original is null)
            {
                return false;
            }


            Bitmap? reducida =
                null;


            try
            {
                reducida =
                    Bitmap.CreateScaledBitmap(
                        original,
                        9,
                        8,
                        true);


                if (reducida is null)
                {
                    return false;
                }


                int bit =
                    0;


                for (int y = 0;
                     y < 8;
                     y++)
                {
                    for (int x = 0;
                         x < 8;
                         x++)
                    {
                        int colorA =
                            reducida.GetPixel(
                                x,
                                y);


                        int colorB =
                            reducida.GetPixel(
                                x + 1,
                                y);


                        int grisA =
                            ObtenerGris(
                                colorA);


                        int grisB =
                            ObtenerGris(
                                colorB);


                        if (grisA >
                            grisB)
                        {
                            hash |=
                                1UL << bit;
                        }


                        bit++;
                    }
                }


                return true;
            }
            finally
            {
                if (reducida is not null
                    &&
                    !ReferenceEquals(
                        reducida,
                        original))
                {
                    reducida.Dispose();
                }


                original.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }


    // =========================================================
    // ESCALA DE GRISES
    // =========================================================

    private static int ObtenerGris(
        int color)
    {
        int r =
            global::Android.Graphics.Color.GetRedComponent(
                color);


        int g =
            global::Android.Graphics.Color.GetGreenComponent(
                color);


        int b =
            global::Android.Graphics.Color.GetBlueComponent(
                color);


        return
            (
                r * 299
                +
                g * 587
                +
                b * 114
            )
            /
            1000;
    }


    // =========================================================
    // BASE64 LIMPIO
    // =========================================================

    private static string ExtraerBase64(
        string valor)
    {
        string limpio =
            valor.Trim();


        if (limpio.StartsWith(
                "data:image/",
                StringComparison.OrdinalIgnoreCase))
        {
            int coma =
                limpio.IndexOf(',');


            if (coma >= 0
                &&
                coma < limpio.Length - 1)
            {
                limpio =
                    limpio.Substring(
                        coma + 1);
            }
        }


        return limpio;
    }
}
