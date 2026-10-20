using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// ACTUALIZACIONES DE LA APP
//
// Comprueba en GitHub si hay una versión nueva publicada (el
// proyecto de releases es público, no hace falta cuenta) y la
// descarga. La instalación es específica de cada plataforma.
//
// Cada versión se publica como un release del repositorio con:
//   - SCCosmos-android.apk   (app Android, arm64)
//   - SCCosmos-windows.zip   (app Windows, compilada)
// ============================================================

public sealed class VersionPublicada
{
    /// <summary>Etiqueta del release (p. ej. "v1.0.1").</summary>
    public string Etiqueta { get; set; } = "";

    /// <summary>Número de versión sin la "v" (p. ej. "1.0.1").</summary>
    public string Numero =>
        Etiqueta.TrimStart('v', 'V');

    /// <summary>Notas de la versión (cambios).</summary>
    public string Notas { get; set; } = "";


    /// <summary>
    /// Deja solo los cambios: quita el pie de «Archivos» que añade el
    /// script de publicación al release.
    /// </summary>
    public static string LimpiarNotas(
        string notas)
    {
        string limpio =
            (notas ?? "").Trim();

        int corte =
            limpio.IndexOf(
                "**Archivos**",
                StringComparison.OrdinalIgnoreCase);

        if (corte >= 0)
        {
            limpio =
                limpio.Substring(
                    0,
                    corte).TrimEnd(
                        ' ', '\r', '\n', '-', '—');
        }

        return limpio;
    }

    /// <summary>Enlace de descarga del APK de Android.</summary>
    public string UrlApk { get; set; } = "";

    /// <summary>Enlace de descarga del ZIP de Windows.</summary>
    public string UrlWindows { get; set; } = "";

    public long TamanoApk { get; set; }

    public long TamanoWindows { get; set; }
}


public sealed class ResultadoComprobacion
{
    /// <summary>La consulta a GitHub funcionó.</summary>
    public bool Correcto { get; set; }

    /// <summary>Motivo del fallo cuando Correcto es falso.</summary>
    public string Error { get; set; } = "";

    /// <summary>Última versión publicada (null si no hay ninguna).</summary>
    public VersionPublicada Version { get; set; }
}


public static class ActualizadorSccosmos
{
    /// <summary>Versión instalada de la aplicación.</summary>
    public const string VersionApp =
        "1.6.4";

    private const string Propietario =
        "jonirecuenco-oss";

    private const string Repositorio =
        "SCCosmos";

    public const string NombreApk =
        "SCCosmos-android.apk";

    public const string NombreWindows =
        "SCCosmos-windows.zip";


    /// <summary>Comprueba la última versión publicada en GitHub.</summary>
    public static async Task<ResultadoComprobacion> ComprobarAsync(
        CancellationToken token = default)
    {
        var resultado =
            new ResultadoComprobacion();

        try
        {
            using var cliente =
                CrearCliente();

            string url =
                "https://api.github.com/repos/"
                + Propietario
                + "/"
                + Repositorio
                + "/releases/latest";

            using HttpResponseMessage respuesta =
                await cliente.GetAsync(
                    url,
                    token);

            if (respuesta.StatusCode
                == System.Net.HttpStatusCode.NotFound)
            {
                // Todavía no hay ninguna versión publicada.
                resultado.Correcto = true;
                return resultado;
            }

            respuesta.EnsureSuccessStatusCode();

            string json =
                await respuesta.Content
                    .ReadAsStringAsync(token);

            using JsonDocument documento =
                JsonDocument.Parse(json);

            var raiz =
                documento.RootElement;

            var version =
                new VersionPublicada
                {
                    Etiqueta =
                        raiz.TryGetProperty(
                            "tag_name",
                            out JsonElement etiqueta)
                            ? etiqueta.GetString() ?? ""
                            : "",

                    Notas =
                        VersionPublicada.LimpiarNotas(
                            raiz.TryGetProperty(
                                "body",
                                out JsonElement notas)
                                ? notas.GetString() ?? ""
                                : "")
                };

            if (raiz.TryGetProperty(
                    "assets",
                    out JsonElement activos))
            {
                foreach (JsonElement activo in
                         activos.EnumerateArray())
                {
                    string nombre =
                        activo.TryGetProperty(
                            "name",
                            out JsonElement n)
                            ? n.GetString() ?? ""
                            : "";

                    string enlace =
                        activo.TryGetProperty(
                            "browser_download_url",
                            out JsonElement u)
                            ? u.GetString() ?? ""
                            : "";

                    long tamano =
                        activo.TryGetProperty(
                            "size",
                            out JsonElement t)
                            && t.TryGetInt64(out long v)
                            ? v
                            : 0;

                    if (string.Equals(
                            nombre,
                            NombreApk,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        version.UrlApk = enlace;
                        version.TamanoApk = tamano;
                    }
                    else if (string.Equals(
                            nombre,
                            NombreWindows,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        version.UrlWindows = enlace;
                        version.TamanoWindows = tamano;
                    }
                }
            }

            resultado.Correcto = true;
            resultado.Version = version;
        }
        catch (Exception ex)
        {
            resultado.Correcto = false;
            resultado.Error = ex.Message;
        }

        return resultado;
    }


    /// <summary>¿La versión publicada es más nueva que la instalada?</summary>
    public static bool EsMasNueva(
        string publicada,
        string instalada)
    {
        string[] partesA =
            (publicada ?? "").Split('.');

        string[] partesB =
            (instalada ?? "").Split('.');

        int total =
            Math.Max(
                partesA.Length,
                partesB.Length);

        for (int i = 0; i < total; i++)
        {
            int a =
                i < partesA.Length
                && int.TryParse(
                    partesA[i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int valorA)
                    ? valorA
                    : 0;

            int b =
                i < partesB.Length
                && int.TryParse(
                    partesB[i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int valorB)
                    ? valorB
                    : 0;

            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }


    /// <summary>
    /// Descarga un archivo con informe de progreso (0 a 1).
    /// </summary>
    public static async Task DescargarAsync(
        string url,
        string destino,
        Action<double> progreso = null,
        CancellationToken token = default)
    {
        using var cliente =
            CrearCliente();

        using HttpResponseMessage respuesta =
            await cliente.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                token);

        respuesta.EnsureSuccessStatusCode();

        long total =
            respuesta.Content.Headers.ContentLength
            ?? 0;

        await using Stream origen =
            await respuesta.Content
                .ReadAsStreamAsync(token);

        await using FileStream archivo =
            File.Create(destino);

        byte[] bufer =
            new byte[81920];

        long leidos = 0;

        while (true)
        {
            int cantidad =
                await origen.ReadAsync(
                    bufer,
                    token);

            if (cantidad <= 0)
            {
                break;
            }

            await archivo.WriteAsync(
                bufer.AsMemory(0, cantidad),
                token);

            leidos += cantidad;

            if (total > 0)
            {
                progreso?.Invoke(
                    leidos / (double)total);
            }
        }
    }


    private static HttpClient CrearCliente()
    {
        var cliente =
            new HttpClient();

        cliente.Timeout =
            TimeSpan.FromSeconds(60);

        cliente.DefaultRequestHeaders
            .UserAgent.ParseAdd(
                "SCCosmos/" + VersionApp);

        return cliente;
    }
}
