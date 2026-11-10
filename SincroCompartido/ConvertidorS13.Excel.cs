using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SCCosmos.Sincro;

// ============================================================
// S-13 · LECTURA DEL EXCEL
//
// Se lee el .xlsx sin dependencias externas (ZIP + XML). Dos formas:
//   - El formulario oficial (S-13): un territorio cada dos filas
//     (nombres arriba, fechas debajo) y hasta 7 asignaciones en parejas
//     de columnas «se asignó / se completó», con una hoja por año de
//     servicio. Se juntan todas las hojas.
//   - El tablero: una fila de cabecera con los números de territorio y,
//     debajo, rondas de «Publicador» y «Entregado / Devuelto».
// ============================================================

public static partial class ConvertidorS13
{
    private static readonly XNamespace EspacioHoja =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";


    private static List<TerritorioS13> ConvertirExcel(string ruta)
    {
        List<Dictionary<Posicion, string>> hojas = LeerHojas(ruta);

        // Primero, el formulario oficial en todas las hojas.
        List<TerritorioS13> registro = ConvertirRegistroExcel(hojas);

        if (registro.Count > 0)
        {
            return registro;
        }

        // Si no, el tablero de la primera hoja.
        var celdas = hojas.FirstOrDefault() ?? new Dictionary<Posicion, string>();

        if (celdas.Count == 0)
        {
            return new List<TerritorioS13>();
        }

        int filas = celdas.Keys.Max(k => k.Fila);
        int columnas = celdas.Keys.Max(k => k.Columna);

        int filaCabecera = 0;

        for (int f = 1; f <= Math.Min(filas, 15) && filaCabecera == 0; f++)
        {
            for (int c = 1; c <= columnas; c++)
            {
                string texto = Obtener(celdas, f, c);

                if (texto.Contains("territorio", StringComparison.OrdinalIgnoreCase))
                {
                    filaCabecera = f;
                    break;
                }
            }
        }

        if (filaCabecera == 0)
        {
            return new List<TerritorioS13>();
        }

        // Columna de etiquetas («Publicador», «Entregado/Devuelto»): no
        // siempre es la primera (hay hojas que empiezan en la B).
        int columnaEtiqueta = 1;

        foreach (var celda in celdas)
        {
            if (celda.Value.Contains("publicador", StringComparison.OrdinalIgnoreCase))
            {
                columnaEtiqueta = celda.Key.Columna;
                break;
            }
        }

        var numeros = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int c = 1; c <= columnas; c++)
        {
            string texto = Obtener(celdas, filaCabecera, c).Trim();

            if (Regex.IsMatch(texto, @"^\d{1,4}[A-Za-z]?$")
                && !numeros.ContainsKey(texto))
            {
                numeros[texto] = c;
            }
        }

        if (numeros.Count == 0)
        {
            return new List<TerritorioS13>();
        }

        var resultado = new List<TerritorioS13>();

        foreach (var par in numeros)
        {
            string publicador = "";
            string entregado = "";
            string devuelto = "";
            string ultima = "";

            for (int f = filaCabecera + 1; f <= filas; f++)
            {
                string columna1 = Obtener(celdas, f, par.Value);
                string columna2 = Obtener(celdas, f, par.Value + 1);
                string etiqueta = Obtener(celdas, f, columnaEtiqueta)
                    .ToLowerInvariant();

                bool esPublicador = etiqueta.Contains("publicador");
                bool esEntrega = etiqueta.Contains("entrega")
                    || etiqueta.Contains("devuel");

                if (esPublicador)
                {
                    if (columna1.Trim().Length > 0)
                    {
                        publicador = columna1.Trim();
                        entregado = "";
                        devuelto = "";
                    }
                }
                else if (esEntrega)
                {
                    string fecha1 = FechaExcel(columna1);
                    string fecha2 = FechaExcel(columna2);

                    if (EsFecha(fecha1))
                    {
                        entregado = NormalizarFecha(fecha1);
                        devuelto = "";
                    }

                    if (EsFecha(fecha2))
                    {
                        devuelto = NormalizarFecha(fecha2);

                        if (ClaveFecha(devuelto) > ClaveFecha(ultima))
                        {
                            ultima = devuelto;
                        }
                    }
                }
            }

            resultado.Add(new TerritorioS13
            {
                Nombre = par.Key,
                UltimaFechaCompletado = ultima,
                AsignadoA = publicador,
                FechaAsignado = entregado,
                FechaCompletado = devuelto,
            });
        }

        return resultado
            .OrderBy(t =>
            {
                var m = Regex.Match(t.Nombre, @"\d+");
                return m.Success ? int.Parse(m.Value) : 0;
            })
            .ToList();
    }


    // ========================================================
    // FORMULARIO OFICIAL (S-13)
    // ========================================================

    private sealed class AsignacionS13
    {
        public string Publicador = "";
        public string Asignado = "";
        public string Completado = "";

        // «Última fecha en que se completó» de la hoja de la que sale.
        public string Ultima = "";

        // Año de servicio de la hoja y posición: ordena lo que no tiene fecha.
        public int Orden;
    }


    /// <summary>
    /// El formulario oficial en Excel: la cabecera «Núm. de terr.», la
    /// columna «Última fecha en que se completó» y las parejas «Asignado a»
    /// con «Fecha en que se asignó / se completó» debajo. Las columnas que
    /// alguien añada a la derecha (meses, pedir territorio…) no cuentan. Sale
    /// cada asignación de todas las hojas (una fila por asignación, sin
    /// repetir las que estén en dos hojas), para que la pestaña Territorios
    /// cuente todas las vueltas de cada año de servicio.
    /// </summary>
    private static List<TerritorioS13> ConvertirRegistroExcel(List<Dictionary<Posicion, string>> hojas)
    {
        var asignaciones = new Dictionary<string, List<AsignacionS13>>(StringComparer.OrdinalIgnoreCase);
        var ultimas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var orden = new List<string>();

        foreach (var celdas in hojas)
        {
            if (celdas.Count == 0)
            {
                continue;
            }

            Posicion cabecera = celdas
                .Where(c => c.Key.Fila <= 20 && Plano(c.Value).Contains("terr") && Plano(c.Value).Contains("num"))
                .Select(c => c.Key)
                .OrderBy(c => c.Fila)
                .FirstOrDefault();

            if (cabecera is null)
            {
                continue;
            }

            int filaCabecera = cabecera.Fila;
            int columnaNumero = cabecera.Columna;
            int filas = celdas.Keys.Max(k => k.Fila);
            int columnas = celdas.Keys.Max(k => k.Columna);

            // «Año de servicio: 2026»: el número va a la derecha de la etiqueta.
            int anio = 0;

            foreach (var etiqueta in celdas.Where(c => c.Key.Fila < filaCabecera && Plano(c.Value).Contains("servicio")))
            {
                if (int.TryParse(Obtener(celdas, etiqueta.Key.Fila, etiqueta.Key.Columna + 1), out int valor))
                {
                    anio = valor;
                }
            }

            int columnaUltima = Enumerable.Range(1, columnas)
                .FirstOrDefault(c => Plano(Obtener(celdas, filaCabecera, c)).Contains("ultima"));

            List<int> parejas = Enumerable.Range(1, columnas)
                .Where(c => Plano(Obtener(celdas, filaCabecera, c)).Contains("asignado")
                            && Plano(Obtener(celdas, filaCabecera + 1, c)).Contains("asign")
                            && Plano(Obtener(celdas, filaCabecera + 1, c + 1)).Contains("complet"))
                .ToList();

            if (parejas.Count == 0)
            {
                continue;
            }

            for (int f = filaCabecera + 2; f <= filas; f++)
            {
                string territorio = Obtener(celdas, f, columnaNumero).Trim();

                // Filas vacías o la cabecera repetida en otra página.
                if (territorio.Length == 0 || Plano(territorio).Contains("terr"))
                {
                    continue;
                }

                if (!asignaciones.ContainsKey(territorio))
                {
                    asignaciones[territorio] = new List<AsignacionS13>();
                    ultimas[territorio] = new List<string>();
                    orden.Add(territorio);
                }

                string ultimaHoja = "";

                if (columnaUltima > 0)
                {
                    string ultima = FechaExcel(Obtener(celdas, f, columnaUltima));

                    if (EsFecha(ultima))
                    {
                        ultimaHoja = NormalizarFecha(ultima);
                        ultimas[territorio].Add(ultimaHoja);
                    }
                }

                // Los nombres en esta fila y sus fechas en la de debajo.
                for (int i = 0; i < parejas.Count; i++)
                {
                    int c = parejas[i];

                    string publicador = Obtener(celdas, f, c).Trim();
                    string asignado = FechaExcel(Obtener(celdas, f + 1, c));
                    string completado = FechaExcel(Obtener(celdas, f + 1, c + 1));

                    if (!EsNombre(publicador) && !EsFecha(asignado) && !EsFecha(completado))
                    {
                        continue;
                    }

                    asignaciones[territorio].Add(new AsignacionS13
                    {
                        Publicador = EsNombre(publicador) ? publicador : "",
                        Asignado = EsFecha(asignado) ? NormalizarFecha(asignado) : "",
                        Completado = EsFecha(completado) ? NormalizarFecha(completado) : "",
                        Ultima = ultimaHoja,
                        Orden = anio * 100 + i,
                    });
                }

                // La fila de las fechas ya está leída (si no empieza otro territorio en ella).
                if (Obtener(celdas, f + 1, columnaNumero).Trim().Length == 0)
                {
                    f++;
                }
            }
        }

        // Todas las asignaciones (la pestaña Territorios cuenta cada vuelta del
        // año de servicio), una fila por asignación.
        var resultado = new List<TerritorioS13>();

        foreach (string territorio in orden)
        {
            // Sin repetir lo que esté en dos hojas, y por fecha.
            List<AsignacionS13> lista = asignaciones[territorio]
                .GroupBy(a => (Plano(a.Publicador), a.Asignado, a.Completado))
                .Select(g => g.First())
                .OrderBy(a => ClaveFecha(a.Asignado) > 0 ? ClaveFecha(a.Asignado) : ClaveFecha(a.Completado))
                .ThenBy(a => a.Orden)
                .ToList();

            // Solo la última puede seguir abierta. Una sin completar a la que
            // sigue la misma persona es la misma asignación pasada a la hoja
            // del año siguiente; si sigue otra, se devolvió sin completar.
            var validas = new List<AsignacionS13>();

            for (int i = 0; i < lista.Count; i++)
            {
                AsignacionS13 actual = lista[i];

                if (actual.Completado.Length == 0 && i + 1 < lista.Count)
                {
                    AsignacionS13 siguiente = lista[i + 1];

                    if (actual.Publicador.Length > 0
                        && Plano(actual.Publicador) == Plano(siguiente.Publicador)
                        && actual.Asignado.Length > 0)
                    {
                        siguiente.Asignado = actual.Asignado;
                    }

                    continue;
                }

                validas.Add(actual);
            }

            if (validas.Count == 0)
            {
                // Sin asignaciones: el territorio cuenta igual, con su última fecha.
                resultado.Add(new TerritorioS13
                {
                    Nombre = territorio,
                    UltimaFechaCompletado = ultimas[territorio].OrderBy(ClaveFecha).LastOrDefault() ?? "",
                });

                continue;
            }

            foreach (AsignacionS13 asignacion in validas)
            {
                resultado.Add(new TerritorioS13
                {
                    Nombre = territorio,
                    UltimaFechaCompletado = asignacion.Ultima,
                    AsignadoA = asignacion.Publicador,
                    FechaAsignado = asignacion.Asignado,
                    FechaCompletado = asignacion.Completado,
                });
            }
        }

        return resultado;
    }


    private sealed class Posicion : IEquatable<Posicion>
    {
        public int Fila;
        public int Columna;

        public bool Equals(Posicion otro) =>
            otro is not null && otro.Fila == Fila && otro.Columna == Columna;

        public override bool Equals(object obj) =>
            obj is Posicion otro && Equals(otro);

        public override int GetHashCode() =>
            HashCode.Combine(Fila, Columna);
    }


    private static string Obtener(
        Dictionary<Posicion, string> celdas,
        int fila,
        int columna)
    {
        return celdas.TryGetValue(
            new Posicion { Fila = fila, Columna = columna },
            out string valor)
            ? valor
            : "";
    }


    /// <summary>Las celdas con texto de cada hoja, en el orden del libro (sheet1, sheet2…).</summary>
    private static List<Dictionary<Posicion, string>> LeerHojas(string ruta)
    {
        var hojas = new List<Dictionary<Posicion, string>>();

        using var archivo = ZipFile.OpenRead(ruta);

        var compartidos = new List<string>();

        var hojaCompartidos = archivo.GetEntry("xl/sharedStrings.xml");

        if (hojaCompartidos != null)
        {
            using var flujo = hojaCompartidos.Open();

            var documento = XDocument.Load(flujo);

            foreach (var si in documento.Root.Elements(EspacioHoja + "si"))
            {
                compartidos.Add(string.Concat(
                    si.Descendants(EspacioHoja + "t").Select(t => t.Value)));
            }
        }

        var entradas = archivo.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e =>
            {
                var m = Regex.Match(e.FullName, @"(\d+)\.xml$", RegexOptions.IgnoreCase);
                return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
            })
            .ToList();

        foreach (var entrada in entradas)
        {
            using var flujoHoja = entrada.Open();

            hojas.Add(LeerCeldas(XDocument.Load(flujoHoja), compartidos));
        }

        return hojas;
    }


    private static Dictionary<Posicion, string> LeerCeldas(
        XDocument documentoHoja,
        List<string> compartidos)
    {
        var celdas = new Dictionary<Posicion, string>();

        var datos = documentoHoja.Root.Element(EspacioHoja + "sheetData");

        if (datos == null)
        {
            return celdas;
        }

        foreach (var fila in datos.Elements(EspacioHoja + "row"))
        {
            foreach (var celda in fila.Elements(EspacioHoja + "c"))
            {
                string referencia = celda.Attribute("r")?.Value ?? "";

                var m = Regex.Match(referencia, @"^([A-Za-z]+)(\d+)$");

                if (!m.Success)
                {
                    continue;
                }

                int columna = 0;

                foreach (char letra in m.Groups[1].Value.ToUpperInvariant())
                {
                    columna = columna * 26 + (letra - 64);
                }

                int numeroFila = int.Parse(m.Groups[2].Value);

                string texto = "";

                var valor = celda.Element(EspacioHoja + "v");

                if (valor != null)
                {
                    texto = valor.Value;

                    if ((string)celda.Attribute("t") == "s"
                        && int.TryParse(texto, out int indice)
                        && indice >= 0
                        && indice < compartidos.Count)
                    {
                        texto = compartidos[indice];
                    }
                }
                else
                {
                    var enLinea = celda.Element(EspacioHoja + "is");

                    if (enLinea != null)
                    {
                        texto = string.Concat(
                            enLinea.Descendants(EspacioHoja + "t")
                                .Select(t => t.Value));
                    }
                }

                texto = (texto ?? "").Trim();

                if (texto.Length > 0)
                {
                    celdas[new Posicion
                    {
                        Fila = numeroFila,
                        Columna = columna,
                    }] = texto;
                }
            }
        }

        return celdas;
    }


    /// <summary>Convierte el número de serie de Excel en dd/MM/yyyy.</summary>
    private static string FechaExcel(string valor)
    {
        string texto = (valor ?? "").Trim();

        if (!Regex.IsMatch(texto, @"^\d{5}(\.\d+)?$"))
        {
            return texto;
        }

        try
        {
            var fecha = new DateTime(1899, 12, 30)
                .AddDays((int)double.Parse(texto, CultureInfo.InvariantCulture));

            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
        catch
        {
            return texto;
        }
    }
}
