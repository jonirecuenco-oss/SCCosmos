#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// ARCHIVOS QUE SUBEN LOS ANCIANOS DESDE EL FORMULARIO WEB
//
// Cada tipo va a su subcarpeta dentro de la carpeta de la visita
// (la elige quien sirve el formulario; en Windows, el Archivo del
// circuito). Las S-21 van por grupos en «S-21\G1», «S-21\G2»…,
// que es como las lee ConvertidorS21. Solo nombres limpios, solo
// documentos, imágenes y comprimidos, y con límite de tamaño.
// ============================================================

public static class ArchivosProgramaWeb
{
    public const long MaxBytesArchivo = 50L * 1024 * 1024;

    public const long MaxBytesVisita = 1024L * 1024 * 1024;

    public const int MaxGruposS21 = 20;

    // Lo que aún se está subiendo (no se lista ni se cuenta como subido).
    private const string Temporal = ".subiendo";

    /// <summary>Las extensiones que se admiten (la página las comprueba antes de subir).</summary>
    public static readonly string[] Extensiones =
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".tif", ".tiff",
        ".doc", ".docx", ".xls", ".xlsx", ".ods", ".odt", ".rtf", ".csv", ".txt",
        ".zip", ".rar", ".7z"
    };

    private static readonly HashSet<string> Reservados = new(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // Tipo (en la dirección), subcarpeta y etiqueta para el resumen de SCCosmos.
    private static readonly (string Tipo, string Carpeta, string Etiqueta)[] Tipos =
    {
        ("s21", "S-21", "S-21"),
        ("s61", "S-61", "S-61"),
        ("grupos", "Grupos de predicación", "Grupos de predicación"),
        ("s88", "S-88", "S-88"),
        ("cuentas", "Cuentas de la congregación", "Cuentas"),
        ("mantenimiento", "Cuentas de mantenimiento", "Cuentas de mantenimiento"),
        ("contactos", "Datos de contacto", "Contactos"),
        ("s13", "S-13", "S-13"),
        ("s28", "S-28", "S-28"),
        ("territorios", "Territorios", "Territorios"),
        ("exhibidores", "Predicación pública", "Predicación pública"),
        ("otros", "Otros", "Otros")
    };


    /// <summary>Todos los tipos, con los grupos de la S-21 («s21-g1»…).</summary>
    public static IEnumerable<string> TodosLosTipos() =>
        Tipos.Select(t => t.Tipo)
            .Concat(Enumerable.Range(1, MaxGruposS21).Select(g => "s21-g" + g));


    /// <summary>La subcarpeta de un tipo («S-21\G3» para «s21-g3»); null si no existe.</summary>
    public static string? Carpeta(string? tipo)
    {
        if (tipo is null)
        {
            return null;
        }

        if (tipo.StartsWith("s21-g", StringComparison.Ordinal)
            && int.TryParse(tipo.AsSpan(5), out int grupo)
            && grupo is >= 1 and <= MaxGruposS21
            && tipo == "s21-g" + grupo)
        {
            return Path.Combine("S-21", "G" + grupo);
        }

        foreach (var t in Tipos)
        {
            if (t.Tipo == tipo)
            {
                return t.Carpeta;
            }
        }

        return null;
    }


    /// <summary>Etiqueta corta para el resumen («S-21», «Cuentas»…).</summary>
    public static string Etiqueta(string tipo)
    {
        if (tipo.StartsWith("s21", StringComparison.Ordinal))
        {
            return "S-21";
        }

        foreach (var t in Tipos)
        {
            if (t.Tipo == tipo)
            {
                return t.Etiqueta;
            }
        }

        return tipo;
    }


    /// <summary>
    /// El nombre de un archivo sin rutas ni caracteres que Windows no admite,
    /// con extensión de documento, imagen o comprimido; null si no vale.
    /// </summary>
    public static string? NombreSeguro(string? nombre)
    {
        string n = (nombre ?? "").Replace('\\', '/');
        n = n.Substring(n.LastIndexOf('/') + 1);

        var limpio = new StringBuilder(n.Length);

        foreach (char c in n)
        {
            limpio.Append(char.IsControl(c) || "<>:\"|?*".IndexOf(c) >= 0 ? '_' : c);
        }

        n = limpio.ToString().Trim().TrimEnd('.', ' ');

        string extension = Path.GetExtension(n).ToLowerInvariant();

        if (!Extensiones.Contains(extension))
        {
            return null;
        }

        string nombreBase = n.Substring(0, n.Length - extension.Length).Trim().TrimStart('.');

        if (nombreBase.Length == 0 || Reservados.Contains(nombreBase.ToUpperInvariant()))
        {
            nombreBase = "archivo";
        }

        if (nombreBase.Length > 100)
        {
            nombreBase = nombreBase.Substring(0, 100).TrimEnd();
        }

        return nombreBase + extension;
    }


    /// <summary>Lo ya subido a la carpeta de una visita: tipo, nombre y tamaño.</summary>
    public static List<(string Tipo, string Nombre, long Tamano)> Subidos(string carpeta)
    {
        var lista = new List<(string, string, long)>();

        foreach (string tipo in TodosLosTipos())
        {
            string ruta = Path.Combine(carpeta, Carpeta(tipo)!);

            if (!Directory.Exists(ruta))
            {
                continue;
            }

            foreach (FileInfo archivo in new DirectoryInfo(ruta)
                         .EnumerateFiles()
                         .Where(a => !a.Name.EndsWith(Temporal, StringComparison.Ordinal))
                         .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                lista.Add((tipo, archivo.Name, archivo.Length));
            }
        }

        return lista;
    }


    /// <summary>Lo subido, para la página: [{ "tipo", "nombre", "tamano" }].</summary>
    public static string ListaJson(string carpeta)
    {
        var lista = new JsonArray();

        foreach (var (tipo, nombre, tamano) in Subidos(carpeta))
        {
            lista.Add(new JsonObject { ["tipo"] = tipo, ["nombre"] = nombre, ["tamano"] = tamano });
        }

        return lista.ToJsonString();
    }


    /// <summary>Lo que ocupa ya la carpeta de la visita.</summary>
    public static long Ocupado(string carpeta) =>
        Directory.Exists(carpeta)
            ? new DirectoryInfo(carpeta).EnumerateFiles("*", SearchOption.AllDirectories).Sum(a => a.Length)
            : 0;


    /// <summary>«nombre.pdf», o «nombre (2).pdf» si ya hay uno con ese nombre.</summary>
    public static string NombreLibre(string carpeta, string nombre)
    {
        string extension = Path.GetExtension(nombre);
        string nombreBase = Path.GetFileNameWithoutExtension(nombre);
        string candidato = nombre;

        for (int i = 2; File.Exists(Path.Combine(carpeta, candidato)); i++)
        {
            candidato = $"{nombreBase} ({i}){extension}";
        }

        return candidato;
    }


    /// <summary>
    /// Copia lo subido a otra carpeta (para descargarlo desde el Calendario),
    /// con sus subcarpetas (S-21\G1…) y sin lo que aún se está subiendo. Lo que
    /// ya está allí igual no se repite; si hay otro con el mismo nombre, el
    /// nuevo va como «nombre (2).pdf».
    /// </summary>
    public static (int Copiados, int Iguales) Copiar(string origen, string destino)
    {
        int copiados = 0, iguales = 0;

        foreach (var (tipo, nombre, tamano) in Subidos(origen))
        {
            string subcarpeta = Carpeta(tipo)!;
            string carpetaDestino = Path.Combine(destino, subcarpeta);
            string hacia = Path.Combine(carpetaDestino, nombre);

            if (File.Exists(hacia) && new FileInfo(hacia).Length == tamano)
            {
                iguales++;
                continue;
            }

            Directory.CreateDirectory(carpetaDestino);
            File.Copy(Path.Combine(origen, subcarpeta, nombre), Path.Combine(carpetaDestino, NombreLibre(carpetaDestino, nombre)));
            copiados++;
        }

        return (copiados, iguales);
    }


    // ---- Descarga desde otro PC ----
    // Los archivos se quedan en el PC que sirve el formulario. Los demás PC
    // los piden con GET …/archivos/{tipo}/{nombre}?h={hora}&f={firma}: la
    // firma es un HMAC con la clave de sincronización (la que ya comparten tus
    // dispositivos), así que el enlace de los ancianos solo no basta. Vale
    // 5 minutos y para ese archivo.

    private static readonly TimeSpan VigenciaFirma = TimeSpan.FromMinutes(5);


    /// <summary>La firma para descargar un archivo (hex), o «» sin clave.</summary>
    public static string FirmaDescarga(string? clave, string codigo, string tipo, string nombre, long hora)
    {
        if (string.IsNullOrEmpty(clave))
        {
            return "";
        }

        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(clave));

        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"descarga\n{codigo}\n{tipo}\n{nombre}\n{hora}")));
    }


    /// <summary>Si la firma de una descarga es buena y reciente (sin clave, nunca).</summary>
    public static bool DescargaValida(string? clave, string codigo, string tipo, string nombre, string horaTexto, string firma, DateTimeOffset ahora)
    {
        if (string.IsNullOrEmpty(clave)
            || !long.TryParse(horaTexto, out long hora)
            || Math.Abs(ahora.ToUnixTimeSeconds() - hora) > VigenciaFirma.TotalSeconds)
        {
            return false;
        }

        byte[] esperada = Encoding.ASCII.GetBytes(FirmaDescarga(clave, codigo, tipo, nombre, hora));
        byte[] recibida = Encoding.ASCII.GetBytes((firma ?? "").ToUpperInvariant());

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(esperada, recibida);
    }


    /// <summary>
    /// Descarga de otro PC («https://pc.red.ts.net») lo que han subido los
    /// ancianos, con sus subcarpetas, como Copiar. Si ese PC no contesta, lanza
    /// HttpRequestException; si no acepta la clave, UnauthorizedAccessException.
    /// </summary>
    public static async Task<(int Copiados, int Iguales)> DescargarAsync(
        HttpClient http,
        string direccion,
        string codigo,
        string clave,
        string destino,
        CancellationToken token)
    {
        string baseArchivos = direccion.TrimEnd('/') + "/programa/" + codigo + "/archivos";
        JsonArray lista = JsonNode.Parse(await http.GetStringAsync(baseArchivos, token)) as JsonArray ?? new JsonArray();
        int copiados = 0, iguales = 0;

        foreach (JsonNode? archivo in lista)
        {
            string tipo = archivo?["tipo"]?.GetValue<string>() ?? "";
            string? nombre = NombreSeguro(archivo?["nombre"]?.GetValue<string>());
            string? subcarpeta = Carpeta(tipo);
            long tamano = archivo?["tamano"]?.GetValue<long>() ?? -1;

            if (nombre is null || subcarpeta is null)
            {
                continue;
            }

            string carpetaDestino = Path.Combine(destino, subcarpeta);

            if (File.Exists(Path.Combine(carpetaDestino, nombre)) && new FileInfo(Path.Combine(carpetaDestino, nombre)).Length == tamano)
            {
                iguales++;
                continue;
            }

            long hora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"{baseArchivos}/{tipo}/{Uri.EscapeDataString(nombre)}?h={hora}&f={FirmaDescarga(clave, codigo, tipo, nombre, hora)}";

            using HttpResponseMessage respuesta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);

            if (respuesta.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new UnauthorizedAccessException("El otro PC no acepta la clave de sincronización de este.");
            }

            respuesta.EnsureSuccessStatusCode();

            // Completo, con su nombre; si se corta, no queda nada a medias.
            Directory.CreateDirectory(carpetaDestino);
            string temporal = RutaTemporal(carpetaDestino);

            try
            {
                await using (var salida = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await respuesta.Content.CopyToAsync(salida, token);
                }

                File.Move(temporal, Path.Combine(carpetaDestino, NombreLibre(carpetaDestino, nombre)));
                copiados++;
            }
            finally
            {
                if (File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
        }

        return (copiados, iguales);
    }


    /// <summary>Dónde se va escribiendo un archivo mientras llega.</summary>
    public static string RutaTemporal(string carpeta) =>
        Path.Combine(carpeta, Guid.NewGuid().ToString("N") + Temporal);


    /// <summary>«S-21: 85 (4 grupos) · S-88: 1 · Cuentas: 3»: lo subido, por tipos.</summary>
    public static string Resumen(IReadOnlyCollection<(string Tipo, string Nombre, long Tamano)> subidos)
    {
        var partes = new List<string>();

        var s21 = subidos.Where(s => s.Tipo.StartsWith("s21", StringComparison.Ordinal)).ToList();

        if (s21.Count > 0)
        {
            int grupos = s21.Select(s => s.Tipo).Where(t => t != "s21").Distinct().Count();

            partes.Add($"S-21: {s21.Count}" + (grupos > 0 ? $" ({grupos} {(grupos == 1 ? "grupo" : "grupos")})" : ""));
        }

        foreach (var t in Tipos.Where(t => t.Tipo != "s21"))
        {
            int cuantos = subidos.Count(s => s.Tipo == t.Tipo);

            if (cuantos > 0)
            {
                partes.Add($"{t.Etiqueta}: {cuantos}");
            }
        }

        return string.Join(" · ", partes);
    }
}
