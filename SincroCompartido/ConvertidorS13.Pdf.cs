using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

using Path = System.IO.Path;

namespace SCCosmos.Sincro;

// ============================================================
// S-13 · LECTURA DEL PDF
// ============================================================

public static partial class ConvertidorS13
{
    private sealed class PalabraPdf
    {
        public string Texto = "";
        public float X0;
        public float Y0;
        public float X1;
        public float Y1;

        public float CentroX => (X0 + X1) / 2;
        public float CentroY => (Y0 + Y1) / 2;
    }


    private sealed class CaracterPdf
    {
        public string Texto = "";
        public float X0;
        public float X1;
        public float Y0;
        public float Y1;

        public float CentroY => (Y0 + Y1) / 2;
    }


    private sealed class EscuchaTexto : IEventListener
    {
        private readonly List<CaracterPdf> _caracteres = new();

        public IReadOnlyList<PalabraPdf> Palabras => Agrupar(_caracteres);

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info)
            {
                return;
            }

            IList<TextRenderInfo> caracteres;

            try
            {
                caracteres = info.GetCharacterRenderInfos();
            }
            catch
            {
                return;
            }

            foreach (var caracter in caracteres)
            {
                try
                {
                    string texto = caracter.GetText();

                    if (string.IsNullOrEmpty(texto))
                    {
                        continue;
                    }

                    var linea = caracter.GetBaseline();
                    var inicio = linea.GetStartPoint();
                    var fin = linea.GetEndPoint();

                    float x0 = Math.Min(inicio.Get(0), fin.Get(0));
                    float x1 = Math.Max(inicio.Get(0), fin.Get(0));

                    float alto = info.GetFontSize();
                    float y0;
                    float y1;

                    try
                    {
                        var ascendente = caracter.GetAscentLine().GetStartPoint();
                        var descendente = caracter.GetDescentLine().GetStartPoint();

                        y0 = Math.Min(ascendente.Get(1), descendente.Get(1));
                        y1 = Math.Max(ascendente.Get(1), descendente.Get(1));
                    }
                    catch
                    {
                        y0 = inicio.Get(1) - alto;
                        y1 = inicio.Get(1) + alto * 0.25f;
                    }

                    _caracteres.Add(new CaracterPdf
                    {
                        Texto = texto,
                        X0 = x0,
                        X1 = x1 > x0 + 0.01f ? x1 : x0 + alto * 0.5f,
                        Y0 = y0,
                        Y1 = y1,
                    });
                }
                catch
                {
                }
            }
        }

        public ICollection<EventType> GetSupportedEvents() => null;


        /// <summary>Une los caracteres en palabras (misma línea y sin hueco).</summary>
        private static List<PalabraPdf> Agrupar(List<CaracterPdf> caracteres)
        {
            var palabras = new List<PalabraPdf>();

            if (caracteres.Count == 0)
            {
                return palabras;
            }

            var ordenados = caracteres
                .OrderBy(c => c.CentroY)
                .ThenBy(c => c.X0)
                .ToList();

            var linea = new List<CaracterPdf>();

            void CerrarLinea()
            {
                if (linea.Count == 0)
                {
                    return;
                }

                linea.Sort((a, b) => a.X0.CompareTo(b.X0));

                var texto = new StringBuilder();
                float inicioX = linea[0].X0;
                float finX = linea[0].X1;
                float y0 = linea[0].Y0;
                float y1 = linea[0].Y1;

                void CerrarPalabra()
                {
                    string valor = texto.ToString().Trim();

                    if (valor.Length > 0)
                    {
                        palabras.Add(new PalabraPdf
                        {
                            Texto = valor,
                            X0 = inicioX,
                            X1 = finX,
                            Y0 = y0,
                            Y1 = y1,
                        });
                    }

                    texto.Clear();
                }

                foreach (var caracter in linea)
                {
                    bool salto = texto.Length > 0
                        && caracter.X0 - finX > 1.6f;

                    if (salto)
                    {
                        CerrarPalabra();
                        inicioX = caracter.X0;
                        y0 = caracter.Y0;
                        y1 = caracter.Y1;
                    }

                    texto.Append(caracter.Texto);
                    finX = Math.Max(finX, caracter.X1);
                }

                CerrarPalabra();
                linea.Clear();
            }

            foreach (var caracter in ordenados)
            {
                if (linea.Count == 0
                    || Math.Abs(caracter.CentroY - linea[0].CentroY) <= 3.0f)
                {
                    linea.Add(caracter);
                }
                else
                {
                    CerrarLinea();
                    linea.Add(caracter);
                }
            }

            CerrarLinea();

            return palabras;
        }
    }


    /// <summary>Diagnóstico: primeras palabras detectadas de un PDF.</summary>
    public static string DescribirPalabras(string ruta, int maximo = 40)
    {
        var sb = new StringBuilder();

        using var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>()));

        using var documento = new PdfDocument(lector);

        var pagina = documento.GetPage(1);
        var escucha = new EscuchaTexto();

        new PdfCanvasProcessor(escucha).ProcessPageContent(pagina);

        float alto = pagina.GetPageSize().GetHeight();

        var palabras = escucha.Palabras
            .Select(p => new PalabraPdf
            {
                Texto = p.Texto,
                X0 = p.X0,
                X1 = p.X1,
                Y0 = alto - p.Y1,
                Y1 = alto - p.Y0,
            })
            .OrderBy(p => p.CentroY)
            .ThenBy(p => p.X0)
            .Take(maximo);

        foreach (var p in palabras)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "x={0:0}-{1:0} y={2:0} '{3}'",
                p.X0, p.X1, p.CentroY, p.Texto));
        }

        return sb.ToString();
    }


    private static List<TerritorioS13> ConvertirPdf(string ruta)
    {
        var territorios = new Dictionary<string, TerritorioS13>(
            StringComparer.OrdinalIgnoreCase);

        var orden = new List<string>();

        using var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>()));

        using var documento = new PdfDocument(lector);

        List<float> centros = null;

        for (int i = 1; i <= documento.GetNumberOfPages(); i++)
        {
            var pagina = documento.GetPage(i);

            var escucha = new EscuchaTexto();

            try
            {
                new PdfCanvasProcessor(escucha)
                    .ProcessPageContent(pagina);
            }
            catch
            {
                continue;
            }

            float alto = pagina.GetPageSize().GetHeight();

            // iText usa origen abajo-izquierda: se normaliza arriba-izquierda.
            var palabras = escucha.Palabras
                .Select(p => new PalabraPdf
                {
                    Texto = p.Texto,
                    X0 = p.X0,
                    X1 = p.X1,
                    Y0 = alto - p.Y1,
                    Y1 = alto - p.Y0,
                })
                .ToList();

            if (palabras.Count == 0)
            {
                continue;
            }

            // El formulario oficial tiene una sola columna «Núm. de
            // terr.»; si se repite, es un registro por zonas.
            int cabeceras = palabras.Count(p =>
                Plano(p.Texto).Trim('.').Equals(
                    "num",
                    StringComparison.OrdinalIgnoreCase));

            List<TerritorioS13> dePagina;

            if (cabeceras >= 2)
            {
                dePagina = LeerPaginaColumnas(palabras);
            }
            else
            {
                dePagina = LeerPagina(
                    palabras,
                    pagina.GetPageSize().GetWidth(),
                    ref centros);

                if (dePagina.Count == 0)
                {
                    dePagina = LeerPaginaSimple(palabras)
                        ?? new List<TerritorioS13>();
                }
            }

            foreach (var territorio in dePagina)
            {
                Fusionar(territorios, orden, territorio);
            }
        }

        return ComponerNombres(territorios, orden);
    }


    private static readonly Regex NumeroTerritorio =
        new(@"^[A-Za-z]{0,5}\d{1,4}[A-Za-z]?$");


    private static List<TerritorioS13> LeerPagina(
        List<PalabraPdf> palabras,
        float ancho,
        ref List<float> centrosPrevios)
    {
        var resultado = new List<TerritorioS13>();

        var cabecera = palabras
            .Where(p => Plano(p.Texto).StartsWith("num"))
            .ToList();

        float yCabecera = cabecera.Count > 0
            ? cabecera.Min(p => p.Y0)
            : 0f;

        float xNum = cabecera.Count > 0
            ? cabecera.Min(p => p.X0)
            : ancho * 0.10f;

        float limiteIzquierda = xNum + 42f;

        string area = AreaDePagina(palabras, yCabecera);

        var numeros = palabras
            .Where(p => p.Y0 > yCabecera
                        && p.X0 < limiteIzquierda
                        && NumeroTerritorio.IsMatch(p.Texto.Trim()))
            .OrderBy(p => p.CentroY)
            .ToList();

        if (numeros.Count == 0)
        {
            return resultado;
        }

        var centros = CentrosDeBloques(palabras);

        if (centros.Count == 0)
        {
            centros = centrosPrevios;
        }

        if (centros != null && centros.Count > 0)
        {
            centrosPrevios = centros;
        }

        var rangos = RangosDeBloques(centros, ancho - 20f);

        if (rangos.Count == 0)
        {
            return resultado;
        }

        float x1Ultima = rangos[0].X0;

        var ys = numeros.Select(n => n.CentroY).ToList();

        float espaciado = 20f;

        if (ys.Count > 1)
        {
            var diferencias = new List<float>();

            for (int i = 0; i + 1 < ys.Count; i++)
            {
                diferencias.Add(ys[i + 1] - ys[i]);
            }

            diferencias.Sort();
            espaciado = diferencias[diferencias.Count / 2];
        }

        float umbral = Math.Max(6f, espaciado * 0.72f);
        float yMin = ys[0] - espaciado * 0.5f;

        var porFila = new Dictionary<int, List<PalabraPdf>>();

        foreach (var palabra in palabras)
        {
            float y = palabra.CentroY;

            if (y < yMin)
            {
                continue;
            }

            int mejor = 0;
            float distancia = float.MaxValue;

            for (int i = 0; i < ys.Count; i++)
            {
                float d = Math.Abs(ys[i] - y);

                if (d < distancia)
                {
                    distancia = d;
                    mejor = i;
                }
            }

            if (distancia > umbral)
            {
                continue;
            }

            if (!porFila.TryGetValue(mejor, out var lista))
            {
                lista = new List<PalabraPdf>();
                porFila[mejor] = lista;
            }

            lista.Add(palabra);
        }

        for (int i = 0; i < numeros.Count; i++)
        {
            if (!porFila.TryGetValue(i, out var banda) || banda.Count == 0)
            {
                continue;
            }

            string ultima = "";

            foreach (var palabra in banda.OrderBy(p => p.X0))
            {
                if (palabra.X0 < x1Ultima && EsFecha(palabra.Texto))
                {
                    ultima = NormalizarFecha(palabra.Texto);
                    break;
                }
            }

            TerritorioS13 elegido = null;

            foreach (var rango in rangos)
            {
                var delBloque = banda
                    .Where(p => p.CentroX >= rango.X0 && p.CentroX <= rango.X1)
                    .OrderBy(p => p.X0)
                    .ToList();

                if (delBloque.Count == 0)
                {
                    continue;
                }

                var (fechas, restantes) = SepararFechas(delBloque);

                var candidatos = restantes
                    .Where(p => EsNombre(p.Texto))
                    .Select(p => p.Texto)
                    .ToList();

                string nombre = string.Join(" ", candidatos);

                if (candidatos.Count == 1 && nombre.Length < 4)
                {
                    nombre = "";
                }

                if (nombre.Trim().Length == 0 && fechas.Count == 0)
                {
                    continue;
                }

                elegido = new TerritorioS13
                {
                    Nombre = numeros[i].Texto.Trim(),
                    AsignadoA = string.Join(" ", nombre.Split(
                        ' ', StringSplitOptions.RemoveEmptyEntries)),
                    FechaAsignado = fechas.Count > 0
                        ? NormalizarFecha(fechas[0])
                        : "",
                    FechaCompletado = fechas.Count > 1
                        ? NormalizarFecha(fechas[1])
                        : "",
                };
            }

            if (elegido == null)
            {
                continue;
            }

            elegido.UltimaFechaCompletado = ultima;
            elegido.Area = area;

            // Localidad escrita debajo del número.
            elegido.Localidad = string.Join(" ",
                banda
                    .Where(p => p.X0 < limiteIzquierda
                                && !ReferenceEquals(p, numeros[i])
                                && EsNombre(p.Texto))
                    .Select(p => p.Texto));

            resultado.Add(elegido);
        }

        return resultado;
    }


    private sealed class Rango
    {
        public float X0;
        public float X1;
    }


    private static List<Rango> RangosDeBloques(List<float> centros, float fin)
    {
        var rangos = new List<Rango>();

        if (centros == null || centros.Count == 0)
        {
            return rangos;
        }

        var ordenados = centros.OrderBy(c => c).ToList();

        float ancho = ordenados.Count > 1
            ? (ordenados[^1] - ordenados[0]) / (ordenados.Count - 1)
            : 100f;

        float mitad = ancho / 2f;

        for (int i = 0; i < ordenados.Count; i++)
        {
            float c = ordenados[i];

            float x0 = i == 0
                ? c - mitad
                : (ordenados[i - 1] + c) / 2f;

            float x1 = i == ordenados.Count - 1
                ? Math.Max(c + mitad, fin)
                : (c + ordenados[i + 1]) / 2f;

            rangos.Add(new Rango { X0 = x0, X1 = x1 });
        }

        return rangos;
    }


    private static List<float> CentrosDeBloques(List<PalabraPdf> palabras)
    {
        var candidatos = palabras
            .Where(p => Plano(p.Texto).StartsWith("asignado"))
            .ToList();

        if (candidatos.Count == 0)
        {
            return new List<float>();
        }

        // La cabecera es la línea con más «Asignado a».
        var lineas = new List<(float Y, List<PalabraPdf> Palabras)>();

        foreach (var palabra in candidatos.OrderBy(p => p.CentroY))
        {
            if (lineas.Count > 0
                && Math.Abs(lineas[^1].Y - palabra.CentroY) <= 3.5f)
            {
                lineas[^1].Palabras.Add(palabra);
            }
            else
            {
                lineas.Add((palabra.CentroY, new List<PalabraPdf> { palabra }));
            }
        }

        var elegida = lineas
            .Where(l => l.Palabras.Count >= 2)
            .OrderByDescending(l => l.Palabras.Count)
            .FirstOrDefault();

        if (elegida.Palabras == null)
        {
            return new List<float> { candidatos[0].CentroX };
        }

        return elegida.Palabras.Select(p => p.CentroX).ToList();
    }


    private static string AreaDePagina(List<PalabraPdf> palabras, float yCabecera)
    {
        var titulo = palabras
            .Where(p => Plano(p.Texto).StartsWith("registro"))
            .ToList();

        float yTitulo = titulo.Count > 0
            ? titulo.Max(p => p.Y1)
            : 0f;

        var candidatas = palabras
            .Where(p => p.CentroY > yTitulo && p.CentroY < yCabecera - 2f)
            .OrderBy(p => p.CentroY)
            .ThenBy(p => p.X0)
            .ToList();

        if (candidatas.Count == 0)
        {
            return "";
        }

        var lineas = new List<(float Y, List<PalabraPdf> Palabras)>();

        foreach (var palabra in candidatas)
        {
            if (lineas.Count > 0
                && Math.Abs(lineas[^1].Y - palabra.CentroY) <= 4f)
            {
                lineas[^1].Palabras.Add(palabra);
            }
            else
            {
                lineas.Add((palabra.CentroY, new List<PalabraPdf> { palabra }));
            }
        }

        foreach (var linea in lineas)
        {
            var textos = linea.Palabras.Select(p => p.Texto.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (textos.Count == 0 || textos.Count > 6)
            {
                continue;
            }

            string texto = string.Join(" ", textos);
            string plano = Plano(texto);

            if (plano.Contains("ano") || plano.Contains("servicio")
                || plano.Contains("registro"))
            {
                continue;
            }

            if (Regex.IsMatch(texto, @"\d"))
            {
                continue;
            }

            return string.Join(" ", texto.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return "";
    }


    private static (List<string> Fechas, List<PalabraPdf> Restantes)
        SepararFechas(List<PalabraPdf> bloque)
    {
        var fechas = new List<string>();
        var restantes = new List<PalabraPdf>();

        int i = 0;

        while (i < bloque.Count)
        {
            bool encontrada = false;

            for (int n = 4; n >= 1; n--)
            {
                if (i + n > bloque.Count)
                {
                    continue;
                }

                string texto = string.Join(" ",
                    bloque.Skip(i).Take(n).Select(p => p.Texto));

                if (EsFecha(texto))
                {
                    fechas.Add(texto);
                    i += n;
                    encontrada = true;
                    break;
                }
            }

            if (!encontrada)
            {
                restantes.Add(bloque[i]);
                i++;
            }
        }

        return (fechas, restantes);
    }


    /// <summary>
    /// Registros con varias zonas en la misma página: cada columna lleva
    /// su «Núm. de terr. &lt;zona&gt; &lt;número&gt;» y, debajo, las
    /// asignaciones (nombre y una o dos fechas).
    /// </summary>
    private static List<TerritorioS13> LeerPaginaColumnas(List<PalabraPdf> palabras)
    {
        var resultado = new List<TerritorioS13>();

        var cabeceras = palabras
            .Where(p => Plano(p.Texto).Trim('.').Equals(
                "num",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.X0)
            .ToList();

        if (cabeceras.Count == 0)
        {
            return resultado;
        }

        float yCabecera = cabeceras.Min(p => p.Y0);
        float ancho = palabras.Max(p => p.X1) + 20f;

        var limites = new List<(float X0, float X1)>();

        for (int i = 0; i < cabeceras.Count; i++)
        {
            float x0 = cabeceras[i].X0 - 4f;
            float x1 = i + 1 < cabeceras.Count
                ? cabeceras[i + 1].X0 - 4f
                : ancho;

            limites.Add((x0, x1));
        }

        foreach (var limite in limites)
        {
            var enColumna = palabras
                .Where(p => p.CentroX >= limite.X0 && p.CentroX < limite.X1)
                .OrderBy(p => p.CentroY)
                .ThenBy(p => p.X0)
                .ToList();

            if (enColumna.Count == 0)
            {
                continue;
            }

            var lineas = AgruparLineas(enColumna, 4f);

            int inicio = 0;

            for (int i = 0; i < lineas.Count; i++)
            {
                if (lineas[i].Palabras.Any(p => Plano(p.Texto).Trim('.').Equals(
                        "num",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    inicio = i;
                    break;
                }
            }

            // La cabecera acaba en la línea que cierra el número.
            int indiceDatos = 0;
            string acumulado = "";

            for (int i = inicio; i < lineas.Count; i++)
            {
                string textoLinea = string.Join(" ",
                    lineas[i].Palabras.OrderBy(p => p.X0).Select(p => p.Texto));

                acumulado += " " + textoLinea;

                if (Plano(acumulado).Contains("terr")
                    && Regex.IsMatch(textoLinea.Trim(), @"\d{1,4}[A-Za-z]?\s*$"))
                {
                    indiceDatos = i + 1;
                    break;
                }
            }

            if (indiceDatos == 0)
            {
                continue;
            }

            string textoCabecera = string.Join(" ",
                lineas.Skip(inicio)
                    .Take(indiceDatos - inicio)
                    .SelectMany(l => l.Palabras.OrderBy(p => p.X0).Select(p => p.Texto)));

            textoCabecera = Regex.Replace(
                textoCabecera, @"(?i)\bde\s+terr\.?\b", " ");
            textoCabecera = Regex.Replace(
                textoCabecera, @"(?i)\b(n[uú]m|terr)\.?", " ");
            textoCabecera = string.Join(" ",
                textoCabecera.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries));

            var m = Regex.Match(textoCabecera, @"(\d{1,4}[A-Za-z]?)\s*$");

            if (!m.Success)
            {
                continue;
            }

            string numero = m.Groups[1].Value;
            string zona = textoCabecera
                .Substring(0, m.Index)
                .Trim(' ', '.', '-', '·');

            var resto = lineas
                .Skip(indiceDatos)
                .SelectMany(l => l.Palabras)
                .ToList();

            if (resto.Count == 0)
            {
                continue;
            }

            // Emparejado nombre → fechas.
            var asignaciones = new List<(string Nombre, List<string> Fechas)>();
            var nombreActual = new List<string>();

            foreach (var linea in AgruparLineas(resto, 4f))
            {
                var textos = linea.Palabras
                    .Where(p => Plano(p.Texto).Trim('(', ')', '.').Length > 0
                                && !Plano(p.Texto).Trim('(', ')', '.').Equals(
                                    "c",
                                    StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Texto)
                    .ToList();

                var (fechas, _) = SepararFechas(
                    linea.Palabras.OrderBy(p => p.X0).ToList());

                if (fechas.Count > 0)
                {
                    string nombre = string.Join(" ",
                        textos.Where(EsNombre));

                    if (nombre.Trim().Length == 0)
                    {
                        nombre = string.Join(" ", nombreActual);
                    }

                    asignaciones.Add((
                        string.Join(" ", nombre.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries)),
                        fechas));

                    nombreActual.Clear();
                }
                else
                {
                    nombreActual.AddRange(textos.Where(EsNombre));
                }
            }

            if (asignaciones.Count == 0)
            {
                continue;
            }

            string nombreFinal = "";
            string fechaAsignado = "";
            string fechaCompletado = "";
            string ultima = "";

            foreach (var (nombre, fechas) in asignaciones)
            {
                if (nombre.Length > 0)
                {
                    nombreFinal = nombre;
                }

                if (fechas.Count >= 2)
                {
                    fechaAsignado = NormalizarFecha(fechas[0]);
                    fechaCompletado = NormalizarFecha(fechas[1]);

                    if (ClaveFecha(fechaCompletado) > ClaveFecha(ultima))
                    {
                        ultima = fechaCompletado;
                    }
                }
                else
                {
                    fechaAsignado = NormalizarFecha(fechas[0]);
                    fechaCompletado = "";
                }
            }

            resultado.Add(new TerritorioS13
            {
                Nombre = numero,
                Area = zona,
                UltimaFechaCompletado = ultima,
                AsignadoA = nombreFinal,
                FechaAsignado = fechaAsignado,
                FechaCompletado = fechaCompletado,
            });
        }

        return resultado;
    }


    private static List<(float Y, List<PalabraPdf> Palabras)> AgruparLineas(
        List<PalabraPdf> palabras,
        float tolerancia)
    {
        var lineas = new List<(float Y, List<PalabraPdf> Palabras)>();

        foreach (var palabra in palabras
            .OrderBy(p => p.CentroY)
            .ThenBy(p => p.X0))
        {
            if (lineas.Count > 0
                && Math.Abs(lineas[^1].Y - palabra.CentroY) <= tolerancia)
            {
                lineas[^1].Palabras.Add(palabra);
            }
            else
            {
                lineas.Add((palabra.CentroY, new List<PalabraPdf> { palabra }));
            }
        }

        return lineas;
    }


    /// <summary>
    /// Respaldo para registros con un territorio por página: la zona y
    /// el número en el encabezado y una línea por asignación.
    /// </summary>
    private static List<TerritorioS13> LeerPaginaSimple(List<PalabraPdf> palabras)
    {
        var lineas = new List<(float Y, List<PalabraPdf> Palabras)>();

        foreach (var palabra in palabras.OrderBy(p => p.CentroY).ThenBy(p => p.X0))
        {
            if (lineas.Count > 0
                && Math.Abs(lineas[^1].Y - palabra.CentroY) <= 4f)
            {
                lineas[^1].Palabras.Add(palabra);
            }
            else
            {
                lineas.Add((palabra.CentroY, new List<PalabraPdf> { palabra }));
            }
        }

        string area = "";
        string numero = "";

        for (int i = 0; i < lineas.Count; i++)
        {
            string texto = string.Join(" ", lineas[i].Palabras.Select(p => p.Texto));
            string plano = Plano(texto);

            if (!plano.Contains("num") || !plano.Contains("terr"))
            {
                continue;
            }

            string resto = Regex.Replace(
                texto,
                @"(?i)n[uú]m\.?\s*de\s*terr\.?",
                "").Trim();

            if (resto.Length == 0 && i + 1 < lineas.Count)
            {
                resto = string.Join(" ",
                    lineas[i + 1].Palabras.Select(p => p.Texto));
            }

            var m = Regex.Match(resto, @"(\d{1,4})\s*$");

            if (m.Success)
            {
                numero = m.Groups[1].Value;
                area = resto.Substring(0, m.Index).Trim(' ', '.', '-', '·');
            }

            break;
        }

        if (numero.Length == 0 || area.Length == 0)
        {
            return null;
        }

        var asignaciones = new List<(string Nombre, List<string> Fechas)>();

        foreach (var linea in lineas)
        {
            var ordenada = linea.Palabras.OrderBy(p => p.X0).ToList();
            var (fechas, restantes) = SepararFechas(ordenada);

            if (fechas.Count == 0)
            {
                continue;
            }

            string nombre = string.Join(" ",
                restantes.Where(p => EsNombre(p.Texto)).Select(p => p.Texto));

            asignaciones.Add((nombre, fechas));
        }

        if (asignaciones.Count == 0)
        {
            return null;
        }

        string nombreFinal = "";
        string fechaAsignado = "";
        string fechaCompletado = "";
        string ultima = "";

        foreach (var (nombre, fechas) in asignaciones)
        {
            if (nombre.Length > 0)
            {
                nombreFinal = nombre;
            }

            if (fechas.Count >= 2)
            {
                fechaAsignado = NormalizarFecha(fechas[0]);
                fechaCompletado = NormalizarFecha(fechas[1]);

                if (ClaveFecha(fechaCompletado) > ClaveFecha(ultima))
                {
                    ultima = fechaCompletado;
                }
            }
            else
            {
                fechaAsignado = NormalizarFecha(fechas[0]);
                fechaCompletado = "";
            }
        }

        return new List<TerritorioS13>
        {
            new()
            {
                Nombre = numero,
                Area = area,
                UltimaFechaCompletado = ultima,
                AsignadoA = nombreFinal,
                FechaAsignado = fechaAsignado,
                FechaCompletado = fechaCompletado,
            },
        };
    }
}
