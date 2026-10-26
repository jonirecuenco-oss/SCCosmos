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
// S-13 · LECTURA DEL EXCEL (TABLERO DE TERRITORIOS)
//
// Se lee el .xlsx sin dependencias externas (ZIP + XML). Estructura
// habitual: una fila de cabecera con los números de territorio y,
// debajo, rondas de «Publicador» y «Entregado / Devuelto».
// ============================================================

public static partial class ConvertidorS13
{
    private static readonly XNamespace EspacioHoja =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";


    private static List<TerritorioS13> ConvertirExcel(string ruta)
    {
        var celdas = LeerHoja(ruta);

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


    private static Dictionary<Posicion, string> LeerHoja(string ruta)
    {
        var celdas = new Dictionary<Posicion, string>();

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

        var hoja = archivo.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (hoja == null)
        {
            return celdas;
        }

        using var flujoHoja = hoja.Open();

        var documentoHoja = XDocument.Load(flujoHoja);

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
