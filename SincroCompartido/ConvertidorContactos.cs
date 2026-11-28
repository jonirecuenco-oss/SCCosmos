#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SCCosmos.Sincro;

/// <summary>
/// Los datos de contacto que mandan las congregaciones, cada una con su formato:
/// las tablas de un Excel (.xlsx), de un Word (.docx), de un PDF con texto o de un CSV;
/// qué es cada columna, por su título (o, sin títulos, por lo que contiene); los datos
/// de cada fila; y cuánto se parece cada nombre al de una persona, para vincularlos.
/// </summary>
public static partial class ConvertidorContactos
{
    public enum Campo
    {
        Ignorar,
        NombreCompleto,
        Nombre,
        Apellidos,
        Telefono,
        Email,
        Direccion,
        Localidad,
        CodigoPostal,
        ContactoEmergencia,
        TelefonoEmergencia,
        Parentesco
    }

    /// <summary>Para elegir qué es cada columna: el campo y cómo se llama.</summary>
    public static readonly IReadOnlyList<(Campo Campo, string Texto)> Campos = new[]
    {
        (Campo.Ignorar, "No usar"),
        (Campo.NombreCompleto, "Nombre y apellidos"),
        (Campo.Nombre, "Nombre"),
        (Campo.Apellidos, "Apellidos"),
        (Campo.Telefono, "Teléfono"),
        (Campo.Email, "Email"),
        (Campo.Direccion, "Dirección"),
        (Campo.Localidad, "Localidad"),
        (Campo.CodigoPostal, "Código postal"),
        (Campo.ContactoEmergencia, "Contacto de emergencia"),
        (Campo.Parentesco, "Parentesco (emergencia)"),
        (Campo.TelefonoEmergencia, "Teléfono de emergencia")
    };

    /// <summary>Los archivos que se pueden leer.</summary>
    public static readonly string[] Extensiones = { ".xlsx", ".docx", ".pdf", ".csv", ".txt" };

    /// <summary>Una tabla del archivo (una hoja del Excel, una tabla del Word…), celda a celda.</summary>
    public sealed class Tabla
    {
        public string Nombre { get; init; } = "";

        public List<string[]> Filas { get; init; } = new();

        public int Columnas => Filas.Count == 0 ? 0 : Filas.Max(f => f.Length);
    }

    /// <summary>Una tabla con su fila de títulos (-1 si no tiene) y qué es cada columna.</summary>
    public sealed class Analisis
    {
        public Tabla Tabla { get; init; } = new();

        public int FilaTitulos { get; init; } = -1;

        public string[] Titulos { get; init; } = Array.Empty<string>();

        public Campo[] Columnas { get; init; } = Array.Empty<Campo>();
    }

    /// <summary>Lo de una fila: el nombre tal como viene y sus datos (vacío si no los tiene).</summary>
    public sealed record Contacto(
        string Nombre,
        string Telefono,
        string Email,
        string Direccion,
        string ContactoEmergencia,
        string TelefonoEmergencia,
        string Tabla,
        int Fila);


    // ================================================================
    // LEER
    // ================================================================

    /// <summary>Las tablas del archivo: Excel (.xlsx), Word (.docx) o CSV.</summary>
    public static List<Tabla> Leer(string ruta) =>
        (Path.GetExtension(ruta).ToLowerInvariant() switch
        {
            ".xlsx" => LeerXlsx(ruta),
            ".docx" => LeerDocx(ruta),
            ".pdf" => LeerPdf(ruta),
            ".csv" or ".txt" => new List<Tabla> { LeerCsv(LeerTexto(ruta), Path.GetFileNameWithoutExtension(ruta)) },
            _ => throw new NotSupportedException("Ese tipo de archivo todavía no se puede leer. Guárdalo como Excel (.xlsx) o Word (.docx).")
        })
        .SelectMany(SepararBloques)
        .ToList();

    /// <summary>
    /// Varias listas una al lado de otra (los mismos títulos repetidos a lo ancho:
    /// «Nombre | Teléfono | Nombre | Teléfono») se separan en una tabla por lista.
    /// </summary>
    private static List<Tabla> SepararBloques(Tabla tabla)
    {
        int fila = FilaDeTitulos(tabla);
        int columnas = tabla.Columnas;

        if (fila < 0 || columnas < 4)
        {
            return new List<Tabla> { tabla };
        }

        string[] titulos = Enumerable.Range(0, columnas)
            .Select(c => c < tabla.Filas[fila].Length ? Normalizar(tabla.Filas[fila][c]) : "")
            .ToArray();

        for (int ancho = 2; ancho <= columnas / 2; ancho++)
        {
            if (Enumerable.Range(0, ancho).Count(c => CampoDeTitulo(titulos[c]) != Campo.Ignorar) < 2
                || !Enumerable.Range(0, columnas - ancho).All(c => titulos[c] == titulos[c + ancho]))
            {
                continue;
            }

            int bloques = (columnas + ancho - 1) / ancho;

            return Enumerable.Range(0, bloques)
                .Select(b => new Tabla
                {
                    Nombre = $"{tabla.Nombre} ({b + 1})",
                    Filas = tabla.Filas
                        .Select(f => Enumerable.Range(b * ancho, ancho).Select(c => c < f.Length ? f[c] : "").ToArray())
                        .ToList()
                })
                .ToList();
        }

        return new List<Tabla> { tabla };
    }

    private static readonly XNamespace EspacioHoja = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace EspacioRelacion = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace EspacioPaquete = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace EspacioWord = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static List<Tabla> LeerXlsx(string ruta)
    {
        using ZipArchive zip = ZipFile.OpenRead(ruta);

        XDocument? Xml(string nombre)
        {
            ZipArchiveEntry? entrada = zip.GetEntry(nombre);

            if (entrada is null)
            {
                return null;
            }

            using Stream flujo = entrada.Open();
            return XDocument.Load(flujo);
        }

        List<string> compartidos =
            Xml("xl/sharedStrings.xml")?.Root?.Elements(EspacioHoja + "si")
                .Select(si => string.Concat(si.Descendants(EspacioHoja + "t").Select(t => t.Value)))
                .ToList()
            ?? new List<string>();

        // Las hojas con su nombre y en su orden: workbook.xml y sus relaciones.
        Dictionary<string, string> destinos =
            (Xml("xl/_rels/workbook.xml.rels")?.Root?.Elements(EspacioPaquete + "Relationship") ?? Enumerable.Empty<XElement>())
                .GroupBy(r => (string?)r.Attribute("Id") ?? "")
                .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("Target") ?? "");

        var tablas = new List<Tabla>();

        foreach (XElement hoja in Xml("xl/workbook.xml")?.Root?.Element(EspacioHoja + "sheets")?.Elements(EspacioHoja + "sheet")
                                  ?? Enumerable.Empty<XElement>())
        {
            if (!destinos.TryGetValue((string?)hoja.Attribute(EspacioRelacion + "id") ?? "", out string? destino))
            {
                continue;
            }

            string entrada = destino.StartsWith("/", StringComparison.Ordinal) ? destino.TrimStart('/') : "xl/" + destino;

            if (Xml(entrada) is { } documento)
            {
                tablas.Add(new Tabla { Nombre = (string?)hoja.Attribute("name") ?? "", Filas = FilasXlsx(documento, compartidos) });
            }
        }

        return tablas;
    }

    private static List<string[]> FilasXlsx(XDocument documento, List<string> compartidos)
    {
        var filas = new SortedDictionary<int, SortedDictionary<int, string>>();

        foreach (XElement fila in documento.Root?.Element(EspacioHoja + "sheetData")?.Elements(EspacioHoja + "row") ?? Enumerable.Empty<XElement>())
        {
            foreach (XElement celda in fila.Elements(EspacioHoja + "c"))
            {
                Match referencia = Regex.Match((string?)celda.Attribute("r") ?? "", @"^([A-Za-z]+)(\d+)$");

                if (!referencia.Success)
                {
                    continue;
                }

                int columna = referencia.Groups[1].Value.ToUpperInvariant().Aggregate(0, (n, letra) => n * 26 + (letra - 'A' + 1)) - 1;
                int numero = int.Parse(referencia.Groups[2].Value, CultureInfo.InvariantCulture);

                string texto = ((string?)celda.Attribute("t") switch
                {
                    "s" => int.TryParse(celda.Element(EspacioHoja + "v")?.Value, out int indice) && indice >= 0 && indice < compartidos.Count
                        ? compartidos[indice]
                        : "",
                    "inlineStr" => string.Concat(celda.Descendants(EspacioHoja + "t").Select(t => t.Value)),
                    _ => Numero(celda.Element(EspacioHoja + "v")?.Value ?? "")
                }).Trim();

                if (texto.Length == 0)
                {
                    continue;
                }

                if (!filas.TryGetValue(numero, out SortedDictionary<int, string>? deLaFila))
                {
                    filas[numero] = deLaFila = new SortedDictionary<int, string>();
                }

                deLaFila[columna] = texto;
            }
        }

        int ancho = filas.Count == 0 ? 0 : filas.Values.Max(f => f.Keys.Max()) + 1;

        return filas.Values
            .Select(f => Enumerable.Range(0, ancho).Select(c => f.TryGetValue(c, out string? valor) ? valor : "").ToArray())
            .ToList();
    }

    // Un teléfono guardado como número puede venir como «6.00123456E+8».
    private static string Numero(string valor) =>
        valor.IndexOfAny(new[] { 'E', 'e', '.' }) >= 0
        && double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out double numero)
        && Math.Abs(numero) >= 100000
        && Math.Abs(numero - Math.Round(numero)) < 0.0001
            ? Math.Round(numero).ToString("0", CultureInfo.InvariantCulture)
            : valor;

    private static List<Tabla> LeerDocx(string ruta)
    {
        using ZipArchive zip = ZipFile.OpenRead(ruta);

        ZipArchiveEntry entrada = zip.GetEntry("word/document.xml")
                                  ?? throw new InvalidDataException("No es un documento de Word (.docx).");

        XDocument documento;

        using (Stream flujo = entrada.Open())
        {
            documento = XDocument.Load(flujo);
        }

        var tablas = new List<Tabla>();

        foreach (XElement tabla in documento.Descendants(EspacioWord + "tbl").Where(t => !t.Ancestors(EspacioWord + "tbl").Any()))
        {
            tablas.Add(new Tabla
            {
                Nombre = "Tabla " + (tablas.Count + 1),
                Filas = tabla.Elements(EspacioWord + "tr")
                    .Select(tr => tr.Elements(EspacioWord + "tc")
                        .SelectMany(tc => new[] { TextoCelda(tc) }.Concat(Enumerable.Repeat("", Ancho(tc) - 1)))
                        .ToArray())
                    .ToList()
            });
        }

        if (tablas.Count > 0)
        {
            return Fichas(tablas) is { } unidas ? new List<Tabla> { unidas } : tablas;
        }

        // Sin tablas, cada línea es una fila y sus tabulaciones separan las columnas;
        // las líneas en blanco separan las fichas, si lo son.
        List<string[]> lineas = documento.Descendants(EspacioWord + "p")
            .SelectMany(p => TextoParrafo(p).Split('\n'))
            .Select(l => Regex.Split(l.Trim(), @"\t+").Select(c => c.Trim()).ToArray())
            .ToList();

        var bloques = new List<Tabla>();
        var bloque = new List<string[]>();

        foreach (string[] linea in lineas.Append(Array.Empty<string>()))
        {
            if (linea.All(c => c.Length == 0))
            {
                if (bloque.Count > 0)
                {
                    bloques.Add(new Tabla { Nombre = "Texto", Filas = bloque });
                    bloque = new List<string[]>();
                }
            }
            else
            {
                bloque.Add(linea);
            }
        }

        return new List<Tabla> { Fichas(bloques) ?? new Tabla { Nombre = "Texto", Filas = lineas } };
    }

    // El texto de un párrafo, con las tabulaciones («\t») y los saltos de línea («\n»).
    private static string TextoParrafo(XElement parrafo) =>
        string.Concat(parrafo.Descendants(EspacioWord + "r").SelectMany(r => r.Elements()).Select(e =>
            e.Name == EspacioWord + "t" ? e.Value
            : e.Name == EspacioWord + "tab" ? "\t"
            : e.Name == EspacioWord + "br" || e.Name == EspacioWord + "cr" ? "\n"
            : ""));

    private static string TextoCelda(XElement celda) =>
        Regex.Replace(string.Join(" ", celda.Elements(EspacioWord + "p").Select(TextoParrafo)), @"\s+", " ").Trim();

    // Una celda unida a lo ancho ocupa varias columnas.
    private static int Ancho(XElement celda) =>
        int.TryParse((string?)celda.Element(EspacioWord + "tcPr")?.Element(EspacioWord + "gridSpan")?.Attribute(EspacioWord + "val"), out int ancho) && ancho > 1
            ? ancho
            : 1;

    /// <summary>
    /// Un Word en fichas (una tabla o un bloque de líneas por persona, con «Teléfono:
    /// …», «Email: …») se convierte en una sola tabla con una fila por persona. Null si
    /// no son fichas.
    /// </summary>
    private static Tabla? Fichas(List<Tabla> bloques)
    {
        // Las filas con un dato con rótulo («Teléfono: 600…»; no los títulos solos).
        static int ConDatos(Tabla bloque) =>
            bloque.Filas.Count(f => Rotulos(f).Any(r => r.Valor.Length > 0 && CampoDeTitulo(r.Titulo) != Campo.Ignorar));

        // Al menos tres fichas con dos datos, y casi todos los bloques con alguno.
        List<Tabla> fichas = bloques.Where(b => ConDatos(b) >= 1).ToList();

        if (bloques.Count(b => b.Columnas <= 4 && ConDatos(b) >= 2) < 3 || fichas.Count < bloques.Count * 0.7)
        {
            return null;
        }

        const string TituloNombre = "Nombre y apellidos";
        var titulos = new List<string>();
        var filas = new List<Dictionary<string, string>>();

        foreach (Tabla ficha in fichas)
        {
            var valores = new Dictionary<string, string>();
            string suelta = "";
            string? abierto = null;
            int sueltasDelAbierto = 0;

            void Poner(string rotulo, string valor)
            {
                string titulo = titulos.FirstOrDefault(t => Normalizar(t) == Normalizar(rotulo)) ?? rotulo;

                if (!titulos.Contains(titulo))
                {
                    titulos.Add(titulo);
                }

                valores[titulo] = valores.TryGetValue(titulo, out string? antes) && antes.Length > 0
                    ? (valor.Length > 0 ? antes + " / " + valor : antes)
                    : valor;
            }

            foreach (string[] fila in ficha.Filas)
            {
                List<(string Titulo, string Valor)> pares = Rotulos(fila);
                string texto = string.Join(" ", fila.Select(c => c.Trim()).Where(c => c.Length > 0));

                if (pares.Count > 0)
                {
                    foreach (var (rotulo, valor) in pares)
                    {
                        Poner(rotulo, valor);
                    }

                    // Un rótulo sin valor en su línea («Emergencia:»): las líneas sueltas que siguen son suyas.
                    abierto = pares[^1].Valor.Length == 0 ? pares[^1].Titulo : null;
                    sueltasDelAbierto = 0;
                }
                else if (texto.Length == 0)
                {
                    continue;
                }
                else if (abierto is not null && sueltasDelAbierto < 5)
                {
                    Poner(abierto, texto);
                    sueltasDelAbierto++;
                }
                else if (Regex.Match(texto, @"[^\s/;,:]+@[^\s/;,:]+\.[A-Za-z]{2,}") is { Success: true } correo)
                {
                    // Un email suelto en la ficha es el suyo.
                    Poner("Email", correo.Value);
                }
                else if (valores.Count == 0 && suelta.Length == 0)
                {
                    // La primera línea suelta, antes de ningún rótulo: el nombre (si no hay uno con rótulo).
                    suelta = texto;
                }
            }

            if (suelta.Length > 0
                && !valores.Any(v => v.Value.Length > 0 && (CampoDeTitulo(v.Key) is Campo.NombreCompleto or Campo.Nombre or Campo.Apellidos)))
            {
                Poner(TituloNombre, suelta);
            }

            filas.Add(valores);
        }

        var unida = new List<string[]> { titulos.ToArray() };
        unida.AddRange(filas.Select(f => titulos.Select(t => f.TryGetValue(t, out string? v) ? v : "").ToArray()));

        return new Tabla { Nombre = "Fichas", Filas = unida };
    }

    /// <summary>
    /// Los rótulos de una línea de ficha con sus valores: «Teléfono | 600…» en celdas
    /// (también «Nombre | Juan | Apellidos | García») o «Teléfono: 600…» en una sola.
    /// Vacío si la línea no empieza por un rótulo.
    /// </summary>
    private static List<(string Titulo, string Valor)> Rotulos(string[] fila)
    {
        string[] llenas = fila.Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        var pares = new List<(string Titulo, string Valor)>();

        // El valor de un rótulo que no lo trae: la celda siguiente, si no es otro rótulo.
        string Siguiente(ref int i) =>
            i + 1 < llenas.Length && !llenas[i + 1].EndsWith(':') && SoloRotulo(llenas[i + 1]) is null ? llenas[++i] : "";

        for (int i = 0; i < llenas.Length; i++)
        {
            string celda = llenas[i];
            int dosPuntos = celda.IndexOf(':');
            string antes = dosPuntos > 0 ? celda[..dosPuntos].Trim() : "";

            if (antes.Length > 0 && !antes.Any(char.IsDigit) && antes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4)
            {
                // «Teléfono: 600…», o «Grupo:» con su valor en la celda siguiente.
                string valor = celda[(dosPuntos + 1)..].Trim();
                pares.Add((antes, valor.Length > 0 ? valor : Siguiente(ref i)));
            }
            else if (SoloRotulo(celda) is { } rotulo)
            {
                pares.Add((rotulo, Siguiente(ref i)));
            }
            else if (RotuloDelante(celda) is { } delante)
            {
                pares.Add(delante);
            }
            else if (pares.Count > 0 && pares[^1].Valor.Length == 0)
            {
                pares[^1] = (pares[^1].Titulo, celda);
            }
            else if (pares.Count == 0)
            {
                break;
            }
        }

        return pares;
    }

    // Las palabras de los rótulos («Teléfono móvil», «Correo electrónico», «Persona de contacto»…).
    private static readonly HashSet<string> PalabrasDeRotulo = new()
    {
        "nombre", "nombres", "apellido", "apellidos", "completo", "y", "de", "del", "la", "el", "en", "caso",
        "telefono", "telefonos", "tel", "telf", "tlf", "tlfno", "tfno", "movil", "moviles", "fijo", "celular", "whatsapp",
        "email", "emails", "mail", "correo", "correos", "electronico", "direccion", "direcciones", "domicilio", "domicilios",
        "localidad", "localidades", "poblacion", "municipio", "cp", "codigo", "postal", "contacto", "contactos", "persona",
        "emergencia", "emergencias", "urgencia", "urgencias", "parentesco", "relacion", "pariente", "familiar",
        "otro", "otra", "1", "2", "n", "no", "num", "numero", "personal", "particular", "casa"
    };

    private static readonly HashSet<string> Conectores = new() { "y", "de", "del", "la", "el", "en" };

    // Si todo el texto es un rótulo («Teléfono móvil», «E-mail:»), el rótulo; si no, null.
    private static string? SoloRotulo(string texto)
    {
        string limpio = texto.Trim().TrimEnd(':').Trim();
        string[] palabras = Normalizar(limpio).Replace("e mail", "email").Replace("c p", "cp")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return palabras.Length is > 0 and <= 6
               && palabras.All(PalabrasDeRotulo.Contains)
               && !Conectores.Contains(palabras[^1])
               && CampoDeTitulo(limpio) != Campo.Ignorar
            ? limpio
            : null;
    }

    // «Dirección Madroño 20», «Teléfono móvil 600…» (sin dos puntos): el rótulo de delante y lo demás.
    private static (string Titulo, string Valor)? RotuloDelante(string texto)
    {
        string[] palabras = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int k = Math.Min(4, palabras.Length - 1); k >= 1; k--)
        {
            if (SoloRotulo(string.Join(" ", palabras.Take(k))) is { } rotulo)
            {
                return (rotulo, string.Join(" ", palabras.Skip(k)));
            }
        }

        return null;
    }

    private static string LeerTexto(string ruta)
    {
        byte[] bytes = File.ReadAllBytes(ruta);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            // Un CSV de Excel en español suele venir en Latin-1.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static Tabla LeerCsv(string texto, string nombre)
    {
        string[] lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string muestra = string.Join("\n", lineas.Take(10));
        char separador = new[] { ';', ',', '\t' }.OrderByDescending(c => muestra.Count(x => x == c)).First();

        return new Tabla
        {
            Nombre = nombre,
            Filas = lineas.Where(l => l.Trim().Length > 0).Select(l => CamposCsv(l, separador)).ToList()
        };
    }

    private static string[] CamposCsv(string linea, char separador)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        bool entreComillas = false;

        for (int i = 0; i < linea.Length; i++)
        {
            char c = linea[i];

            if (c == '"')
            {
                if (entreComillas && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else
                {
                    entreComillas = !entreComillas;
                }
            }
            else if (c == separador && !entreComillas)
            {
                campos.Add(actual.ToString().Trim());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }

        campos.Add(actual.ToString().Trim());
        return campos.ToArray();
    }


    // ================================================================
    // QUÉ ES CADA COLUMNA
    // ================================================================

    /// <summary>Sin tildes ni mayúsculas ni signos: «Teléfono móvil:» → «telefono movil».</summary>
    public static string Normalizar(string? texto)
    {
        string sinTildes = new string((texto ?? "").Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return Regex.Replace(Regex.Replace(sinTildes.ToLowerInvariant(), @"[^a-z0-9@]+", " "), @"\s+", " ").Trim();
    }

    /// <summary>Qué campo es una columna por su título (Ignorar si no se reconoce).</summary>
    public static Campo CampoDeTitulo(string? titulo)
    {
        string t = Normalizar(titulo);

        // Un título es corto y no lleva arrobas ni números (salvo «Teléfono 2»).
        if (t.Length == 0 || t.Length > 60 || t.Contains('@') || t.Count(char.IsDigit) > 2)
        {
            return Campo.Ignorar;
        }

        bool telefono = Regex.IsMatch(t, @"\b(tel|telf|tlf|tlfno|tfno|telefono|telefonos|movil|moviles|celular|fijo|whatsapp)\b");

        if (Regex.IsMatch(t, @"\b(emergencia|emergencias|urgencia|urgencias|pariente|parientes|familiar|familiares)\b") || t.Contains("persona de contacto") || t.Contains("en caso de"))
        {
            return telefono ? Campo.TelefonoEmergencia : Campo.ContactoEmergencia;
        }

        if (Regex.IsMatch(t, @"\b(parentesco|parentescos|relacion)\b"))
        {
            return Campo.Parentesco;
        }

        if (telefono)
        {
            return Campo.Telefono;
        }

        if (Regex.IsMatch(t, @"\b(e ?mail|emails|email|correo|correos|mail)\b"))
        {
            return Campo.Email;
        }

        if (Regex.IsMatch(t, @"\b(direccion|direcciones|domicilio|domicilios)\b"))
        {
            return Campo.Direccion;
        }

        if (Regex.IsMatch(t, @"\b(localidad|localidades|poblacion|poblaciones|municipio|municipios)\b"))
        {
            return Campo.Localidad;
        }

        if (t is "cp" or "c p" || t.Contains("codigo postal"))
        {
            return Campo.CodigoPostal;
        }

        bool apellidos = Regex.IsMatch(t, @"\bapellidos?\b");
        bool nombre = Regex.IsMatch(t, @"\bnombres?\b");

        if ((apellidos && nombre) || Regex.IsMatch(t, @"\b(nombre completo|publicador|publicadores|publicadora|hermano|hermanos|hermana)\b"))
        {
            return Campo.NombreCompleto;
        }

        return apellidos ? Campo.Apellidos : nombre ? Campo.Nombre : Campo.Ignorar;
    }

    /// <summary>
    /// Los títulos (de las 10 primeras filas, la que más reconoce, al menos dos) y qué
    /// es cada columna. Sin títulos, por lo que contiene. Lo recordado de esa
    /// congregación (por el título) manda.
    /// </summary>
    public static Analisis Analizar(Tabla tabla, IReadOnlyDictionary<string, Campo>? recordado = null)
    {
        int columnas = tabla.Columnas;
        int filaTitulos = FilaDeTitulos(tabla);

        string[] titulos = Enumerable.Range(0, columnas)
            .Select(c => filaTitulos >= 0 && c < tabla.Filas[filaTitulos].Length ? tabla.Filas[filaTitulos][c].Trim() : "")
            .ToArray();

        // Títulos en dos filas («Teléfono» y «Móvil» bajo «Datos de contacto»): manda la de abajo.
        if (filaTitulos >= 0 && filaTitulos + 1 < tabla.Filas.Count)
        {
            string[] debajo = tabla.Filas[filaTitulos + 1];

            if (debajo.Any(c => CampoDeTitulo(c) != Campo.Ignorar)
                && debajo.All(c => c.Trim().Length == 0 || CampoDeTitulo(c) != Campo.Ignorar))
            {
                for (int c = 0; c < Math.Min(columnas, debajo.Length); c++)
                {
                    if (debajo[c].Trim().Length > 0)
                    {
                        titulos[c] = debajo[c].Trim();
                    }
                }

                filaTitulos++;
            }
        }

        Campo[] campos = filaTitulos >= 0 ? CamposPorTitulos(titulos, tabla, filaTitulos) : CamposPorContenido(tabla, columnas);

        if (recordado is not null)
        {
            for (int c = 0; c < columnas; c++)
            {
                if (titulos[c].Length > 0 && recordado.TryGetValue(Normalizar(titulos[c]), out Campo campo))
                {
                    campos[c] = campo;
                }
            }
        }

        // Sin columna de nombre (a veces no lleva título): la que más lo parece.
        if (!campos.Any(c => c is Campo.NombreCompleto or Campo.Nombre or Campo.Apellidos))
        {
            int columnaNombre = -1;
            double mejorParte = 0.6;

            for (int c = 0; c < columnas; c++)
            {
                List<string> valores = Valores(tabla, c, filaTitulos);
                double parte = valores.Count < 2 ? 0 : valores.Count(PareceNombre) / (double)valores.Count;

                if (campos[c] == Campo.Ignorar && parte > mejorParte)
                {
                    mejorParte = parte;
                    columnaNombre = c;
                }
            }

            if (columnaNombre >= 0)
            {
                campos[columnaNombre] = Campo.NombreCompleto;
            }
        }

        return new Analisis { Tabla = tabla, FilaTitulos = filaTitulos, Titulos = titulos, Columnas = campos };
    }

    // La fila de títulos: de las 10 primeras, la que más reconoce (al menos dos); -1 si ninguna.
    private static int FilaDeTitulos(Tabla tabla)
    {
        int fila = -1;
        int mejor = 1;

        for (int f = 0; f < Math.Min(10, tabla.Filas.Count); f++)
        {
            int reconocidas = tabla.Filas[f].Count(c => CampoDeTitulo(c) != Campo.Ignorar);

            if (reconocidas > mejor)
            {
                mejor = reconocidas;
                fila = f;
            }
        }

        return fila;
    }

    // Lo que hay en una columna bajo los títulos (hasta 40 valores).
    private static List<string> Valores(Tabla tabla, int columna, int filaTitulos) =>
        tabla.Filas
            .Skip(filaTitulos + 1)
            .Select(f => columna < f.Length ? f[columna].Trim() : "")
            .Where(v => v.Length > 0)
            .Take(40)
            .ToList();

    private static bool PareceNombre(string valor) =>
        valor.Count(char.IsLetter) >= 4 && !valor.Any(char.IsDigit) && !valor.Contains('@') && valor.Trim().Contains(' ');

    // Justo detrás de la columna del contacto de emergencia, el primer teléfono es el
    // suyo y un email, también el suyo (no se guarda); salvo que esa columna ya traiga
    // los teléfonos: entonces lo que sigue es de la persona.
    private static Campo[] CamposPorTitulos(string[] titulos, Tabla tabla, int filaTitulos)
    {
        Campo[] campos = titulos.Select(CampoDeTitulo).ToArray();

        // «Contacto» a secas: con teléfonos, es el suyo; con nombres, el de emergencia.
        for (int c = 0; c < campos.Length; c++)
        {
            if (Normalizar(titulos[c]) is "contacto" or "contactos")
            {
                List<string> valores = Valores(tabla, c, filaTitulos);

                if (valores.Count > 0)
                {
                    campos[c] = valores.Count(v => v.Count(char.IsDigit) >= 9) >= valores.Count * 0.6 ? Campo.Telefono
                        : valores.Count(v => v.Count(char.IsLetter) >= 3 && !v.Contains('@')) >= valores.Count * 0.6 ? Campo.ContactoEmergencia
                        : Campo.Ignorar;
                }
            }
        }

        for (int c = 0; c < campos.Length; c++)
        {
            if (campos[c] != Campo.ContactoEmergencia)
            {
                continue;
            }

            List<string> valores = Valores(tabla, c, filaTitulos);

            if (valores.Count == 0 || valores.Count(v => v.Count(char.IsDigit) >= 9) >= valores.Count * 0.5)
            {
                continue;
            }

            bool hayTelefono = false;

            for (int d = c + 1; d < campos.Length; d++)
            {
                if (campos[d] == Campo.Telefono && !hayTelefono)
                {
                    campos[d] = Campo.TelefonoEmergencia;
                }
                else if (campos[d] == Campo.Email)
                {
                    campos[d] = Campo.Ignorar;
                    break;
                }
                else if (campos[d] is not (Campo.Parentesco or Campo.TelefonoEmergencia))
                {
                    break;
                }

                hayTelefono |= campos[d] == Campo.TelefonoEmergencia;
            }
        }

        return campos;
    }

    private static Campo[] CamposPorContenido(Tabla tabla, int columnas)
    {
        var campos = new Campo[columnas];
        bool hayNombre = false;

        for (int c = 0; c < columnas; c++)
        {
            List<string> valores = Valores(tabla, c, -1);

            if (valores.Count == 0)
            {
                continue;
            }

            double Parte(Func<string, bool> condicion) => valores.Count(condicion) / (double)valores.Count;

            if (Parte(v => v.Contains('@')) >= 0.6)
            {
                campos[c] = Campo.Email;
            }
            else if (Parte(v => v.Count(char.IsDigit) >= 9 && v.Count(char.IsLetter) <= 3) >= 0.6)
            {
                campos[c] = Campo.Telefono;
            }
            else if (Parte(v => v.Count(char.IsDigit) >= 9 && v.Count(char.IsLetter) >= 4) >= 0.6)
            {
                // Texto y teléfono: si el texto se repite («Móvil», «Fijo») es un rótulo y el
                // teléfono es suyo; si cada uno es distinto, es el nombre de su contacto.
                List<string> textos = valores.Select(v => Normalizar(Regex.Replace(v, @"\d", " "))).Where(t => t.Length > 0).ToList();
                campos[c] = textos.Distinct().Count() >= textos.Count * 0.5 ? Campo.ContactoEmergencia : Campo.Telefono;
            }
            else if (Parte(v => Regex.IsMatch(Normalizar(v), @"\b(c|calle|avda|avenida|plaza|pza|ctra|carretera|camino|urb|paseo|ronda)\b") && v.Any(char.IsDigit)) >= 0.5)
            {
                campos[c] = Campo.Direccion;
            }
            else if (Parte(v => Regex.IsMatch(v, @"^\d{4,5}\s*[-,]?\s*\p{L}")) >= 0.6)
            {
                // «06380 Jerez de los Caballeros»: el código postal y la localidad.
                campos[c] = Campo.Localidad;
            }
            else if (!hayNombre && Parte(PareceNombre) >= 0.6)
            {
                campos[c] = Campo.NombreCompleto;
                hayNombre = true;
            }
        }

        return campos;
    }


    // ================================================================
    // LOS DATOS DE CADA FILA
    // ================================================================

    /// <summary>Los contactos de la tabla: una fila por persona (sin las vacías, los títulos repetidos ni las de solo un rótulo).</summary>
    public static List<Contacto> Contactos(Analisis analisis)
    {
        var lista = new List<Contacto>();

        for (int f = analisis.FilaTitulos + 1; f < analisis.Tabla.Filas.Count; f++)
        {
            string[] fila = analisis.Tabla.Filas[f];

            // Los títulos repetidos (las listas de cada grupo, una tras otra) no son personas.
            if (fila.Count(c => CampoDeTitulo(c) != Campo.Ignorar) >= 2)
            {
                continue;
            }

            string Juntar(Campo campo, string separador) =>
                string.Join(separador, Enumerable.Range(0, Math.Min(fila.Length, analisis.Columnas.Length))
                    .Where(c => analisis.Columnas[c] == campo)
                    .Select(c => fila[c].Trim())
                    .Where(v => v.Length > 0)
                    .Distinct());

            string nombre = Juntar(Campo.NombreCompleto, " ");

            if (nombre.Length == 0)
            {
                nombre = (Juntar(Campo.Nombre, " ") + " " + Juntar(Campo.Apellidos, " ")).Trim();
            }

            string codigoPostal = Juntar(Campo.CodigoPostal, " ");

            // Guardado como número, pierde el cero: «6800» es el 06800.
            if (Regex.IsMatch(codigoPostal, @"^\d{4}$"))
            {
                codigoPostal = "0" + codigoPostal;
            }

            string direccion = string.Join(", ", new[]
                {
                    Juntar(Campo.Direccion, " "),
                    (codigoPostal + " " + Juntar(Campo.Localidad, " ")).Trim()
                }
                .Where(s => s.Length > 0));

            // Solo lo que lo parece: un email con su arroba y un teléfono con sus cifras (si una
            // columna se tomó por otra, no se cuela un número de grupo).
            string telefono = Juntar(Campo.Telefono, " / ");
            telefono = telefono.Count(char.IsDigit) >= 6 ? telefono : "";
            string email = string.Join(" / ", Regex.Matches(Juntar(Campo.Email, " "), @"[^\s/;,:<>()]+@[^\s/;,:<>()]+\.[A-Za-z]{2,}")
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            string emergencia = Juntar(Campo.ContactoEmergencia, " · ");
            string telefonoEmergencia = Juntar(Campo.TelefonoEmergencia, " / ");

            // «María (madre) 600 123 456» en una sola columna: el teléfono, aparte.
            if (telefonoEmergencia.Length == 0)
            {
                (emergencia, telefonoEmergencia) = SepararTelefono(emergencia);
            }

            telefonoEmergencia = telefonoEmergencia.Count(char.IsDigit) >= 6 ? telefonoEmergencia : "";

            // En una ficha, el contacto de emergencia puede traer en líneas aparte su email o su
            // dirección: se quedan el nombre y el parentesco.
            if (emergencia.Contains(" / "))
            {
                emergencia = string.Join(" ", emergencia.Split(" / ")
                    .Select(p => p.Trim(' ', '/'))
                    .Where(p => p.Length > 0 && !p.Contains('@') && !p.Any(char.IsDigit)));
            }

            string parentesco = Juntar(Campo.Parentesco, " ");

            if (parentesco.Length > 0)
            {
                emergencia = emergencia.Length > 0 ? $"{emergencia} ({parentesco})" : parentesco;
            }

            bool tieneDatos = telefono.Length + email.Length + direccion.Length + emergencia.Length + telefonoEmergencia.Length > 0;

            if (!tieneDatos || nombre.Count(char.IsLetter) < 2 || CampoDeTitulo(nombre) != Campo.Ignorar)
            {
                continue;
            }

            lista.Add(new Contacto(nombre, telefono, email, direccion, emergencia, telefonoEmergencia, analisis.Tabla.Nombre, f + 1));
        }

        return lista;
    }

    private static (string Contacto, string Telefono) SepararTelefono(string texto)
    {
        List<string> numeros = Regex.Matches(texto, @"\+?\d[\d .-]{7,}\d")
            .Select(m => m.Value.Trim())
            .Where(n => n.Count(char.IsDigit) >= 9)
            .ToList();

        if (numeros.Count == 0)
        {
            return (texto.Trim(), "");
        }

        string resto = numeros.Aggregate(texto, (t, n) => t.Replace(n, " "));
        resto = Regex.Replace(Regex.Replace(resto, @"\(\s*\)", " "), @"\s+", " ").Trim(' ', '-', ',', ';', ':', '/', '·');

        return (resto, string.Join(" / ", numeros));
    }

    /// <summary>
    /// La misma persona en varias hojas (la lista completa y la de su grupo) queda una
    /// sola vez: con los datos de la primera fila en que sale y, lo que a esa le falte,
    /// de las demás.
    /// </summary>
    public static List<Contacto> SinRepetir(IEnumerable<Contacto> contactos)
    {
        var lista = new List<Contacto>();
        var porNombre = new Dictionary<string, int>();

        static string Uno(string a, string b) => a.Length > 0 ? a : b;

        foreach (Contacto contacto in contactos)
        {
            string clave = string.Join(" ", Palabras(contacto.Nombre).OrderBy(p => p, StringComparer.Ordinal));

            if (clave.Length == 0 || !porNombre.TryGetValue(clave, out int igual))
            {
                if (clave.Length > 0)
                {
                    porNombre[clave] = lista.Count;
                }

                lista.Add(contacto);
                continue;
            }

            Contacto antes = lista[igual];

            lista[igual] = antes with
            {
                Telefono = Uno(antes.Telefono, contacto.Telefono),
                Email = Uno(antes.Email, contacto.Email),
                Direccion = Uno(antes.Direccion, contacto.Direccion),
                ContactoEmergencia = Uno(antes.ContactoEmergencia, contacto.ContactoEmergencia),
                TelefonoEmergencia = Uno(antes.TelefonoEmergencia, contacto.TelefonoEmergencia)
            };
        }

        return lista;
    }


    // ================================================================
    // VINCULAR CON LAS PERSONAS
    // ================================================================

    // Lo que no es del nombre: enlaces, tratamientos y, aparte, lo que va entre paréntesis («(anciano)»).
    private static readonly HashSet<string> Enlaces = new()
    {
        "de", "del", "la", "las", "los", "y", "e", "da", "do", "dos", "das",
        "hno", "hna", "hnos", "hnas", "hermano", "hermana", "sr", "sra", "srta", "familia", "fam"
    };

    private static List<string> Palabras(string nombre) =>
        Normalizar(Regex.Replace(nombre ?? "", @"\([^)]*\)", " ")).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Enlaces.Contains(p) && !p.Any(char.IsDigit))
            .ToList();

    /// <summary>
    /// De 0 a 100: cuánto se parecen dos nombres, sin tildes ni mayúsculas y en
    /// cualquier orden («GARCÍA PÉREZ, Juan» y «Juan García Pérez», 100). Si a uno le
    /// falta un apellido, baja un poco («Juan García», 90).
    /// </summary>
    public static int Parecido(string a, string b)
    {
        List<string> pa = Palabras(a);
        List<string> pb = Palabras(b);

        if (pa.Count == 0 || pb.Count == 0)
        {
            return 0;
        }

        (List<string> corta, List<string> larga) = pa.Count <= pb.Count ? (pa, pb) : (pb, pa);
        var libres = larga.ToList();
        double suma = 0;

        foreach (string palabra in corta)
        {
            int mejor = -1;
            double valor = 0;

            for (int i = 0; i < libres.Count; i++)
            {
                double v = Similitud(palabra, libres[i]);

                if (v > valor)
                {
                    valor = v;
                    mejor = i;
                }
            }

            if (mejor >= 0)
            {
                suma += valor;
                libres.RemoveAt(mejor);
            }
        }

        int parecido = (int)Math.Round(100 * (0.7 * suma / corta.Count + 0.3 * suma / larga.Count));

        // Un nombre de una sola palabra («Juan») no basta para saber quién es.
        return corta.Count == 1 ? Math.Min(parecido, 60) : parecido;
    }

    private static double Similitud(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }

        // Una inicial («J.») vale por el nombre que empieza por ella.
        if ((a.Length == 1 && b.StartsWith(a, StringComparison.Ordinal)) || (b.Length == 1 && a.StartsWith(b, StringComparison.Ordinal)))
        {
            return 0.9;
        }

        double v = 1 - Levenshtein(a, b) / (double)Math.Max(a.Length, b.Length);

        // Palabras distintas no suman, y una letra cambiada en un nombre corto puede
        // ser otro nombre («María» y «Marta»): cuenta bastante menos.
        return v < 0.75 ? 0 : v * v;
    }

    private static int Levenshtein(string a, string b)
    {
        var anterior = new int[b.Length + 1];
        var actual = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            anterior[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            actual[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, anterior[j] + 1), anterior[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (anterior, actual) = (actual, anterior);
        }

        return anterior[b.Length];
    }

    /// <summary>Desde aquí se vincula una fila con una persona; desde <see cref="MinimoSeguro"/>, se marca sola.</summary>
    public const int MinimoVinculo = 70;

    public const int MinimoSeguro = 90;

    /// <summary>
    /// El parecido (0 a 100) de cada contacto con cada persona: el mayor con sus
    /// nombres (el completo, el común…).
    /// </summary>
    public static int[,] Parecidos(
        IReadOnlyList<string> nombresContactos,
        IReadOnlyList<IReadOnlyList<string>> nombresPersonas)
    {
        var parecidos = new int[nombresContactos.Count, nombresPersonas.Count];

        for (int c = 0; c < nombresContactos.Count; c++)
        {
            for (int p = 0; p < nombresPersonas.Count; p++)
            {
                parecidos[c, p] = MejorParecido(nombresContactos[c], nombresPersonas[p]);
            }
        }

        return parecidos;
    }

    /// <summary>
    /// Para cada contacto, la persona que más se le parece (−1 si ninguna llega a
    /// <see cref="MinimoVinculo"/>), sin repetir persona: si dos filas apuntan a la
    /// misma, se la queda la que más se parece. «Dudoso» si otra persona se le
    /// parece casi lo mismo (5 puntos o menos de diferencia).
    /// </summary>
    public static (int Persona, int Porcentaje, bool Dudoso)[] Vincular(int[,] parecidos)
    {
        int contactos = parecidos.GetLength(0);
        int personas = parecidos.GetLength(1);
        var pares = new List<(int Contacto, int Persona, int Porcentaje)>();

        for (int c = 0; c < contactos; c++)
        {
            for (int p = 0; p < personas; p++)
            {
                if (parecidos[c, p] >= MinimoVinculo)
                {
                    pares.Add((c, p, parecidos[c, p]));
                }
            }
        }

        var resultado = Enumerable.Repeat((-1, 0, false), contactos).ToArray();
        var usadas = new HashSet<int>();

        foreach (var par in pares.OrderByDescending(x => x.Porcentaje).ThenBy(x => x.Contacto).ThenBy(x => x.Persona))
        {
            if (resultado[par.Contacto].Item1 >= 0 || usadas.Contains(par.Persona))
            {
                continue;
            }

            bool dudoso = Enumerable.Range(0, personas)
                .Any(p => p != par.Persona && parecidos[par.Contacto, p] >= par.Porcentaje - 5);

            resultado[par.Contacto] = (par.Persona, par.Porcentaje, dudoso);
            usadas.Add(par.Persona);
        }

        return resultado;
    }

    /// <summary>El mayor parecido entre un nombre y los de una persona.</summary>
    public static int MejorParecido(string nombre, IReadOnlyList<string> nombresPersona) =>
        nombresPersona.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Parecido(nombre, n)).DefaultIfEmpty(0).Max();


    // ================================================================
    // LO RECORDADO POR CONGREGACIÓN
    // ================================================================

    /// <summary>La clave de Configuración con lo elegido para cada título en esa congregación.</summary>
    public static string ClaveColumnas(string congregacion) => "Contactos_Columnas_" + congregacion;

    /// <summary>Lo elegido para cada título (sin los que no tienen título), en JSON.</summary>
    public static string ColumnasJson(IEnumerable<Analisis> analisis, string? anterior = null) =>
        ColumnasJson(analisis.SelectMany(a => a.Titulos.Select((titulo, c) => (titulo, a.Columnas[c]))), anterior);

    /// <summary>Lo de antes con lo elegido para esos títulos, en JSON.</summary>
    public static string ColumnasJson(IEnumerable<(string Titulo, Campo Campo)> elegidas, string? anterior = null)
    {
        var json = Objeto(anterior) ?? new JsonObject();

        foreach (var (titulo, campo) in elegidas)
        {
            if (titulo.Trim().Length > 0)
            {
                json[Normalizar(titulo)] = campo.ToString();
            }
        }

        return json.ToJsonString();
    }

    public static Dictionary<string, Campo> ColumnasDe(string? json)
    {
        var resultado = new Dictionary<string, Campo>();

        if (Objeto(json) is { } objeto)
        {
            foreach (var (titulo, valor) in objeto)
            {
                if (valor is JsonValue v && v.TryGetValue(out string? texto) && Enum.TryParse(texto, out Campo campo))
                {
                    resultado[titulo] = campo;
                }
            }
        }

        return resultado;
    }

    private static JsonObject? Objeto(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
