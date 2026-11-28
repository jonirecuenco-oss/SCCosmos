#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace SCCosmos.Sincro;

/// <summary>
/// Las listas de contacto en PDF con texto (las de Excel o Word guardadas como PDF): la
/// tabla se rehace con la posición de cada palabra. Las columnas son las franjas entre
/// huecos que ninguna palabra tapa, y la fila de títulos dice qué es cada una. Los
/// escaneados (una imagen, sin texto) todavía no se leen.
/// </summary>
public static partial class ConvertidorContactos
{
    // Una letra o una palabra, ya en horizontal: X de izquierda a derecha; Y, su línea
    // base (cuanto mayor, más arriba).
    private sealed class Trozo
    {
        public string Texto = "";
        public float X0;
        public float X1;
        public float Y;
        public float Alto;
    }

    private sealed class EscuchaPdf : IEventListener
    {
        public readonly List<Trozo> Letras = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info)
            {
                return;
            }

            IList<TextRenderInfo> letras;

            try
            {
                letras = info.GetCharacterRenderInfos();
            }
            catch (Exception)
            {
                return;
            }

            foreach (TextRenderInfo letra in letras)
            {
                try
                {
                    string texto = letra.GetText();

                    if (string.IsNullOrEmpty(texto))
                    {
                        continue;
                    }

                    Vector a = letra.GetBaseline().GetStartPoint();
                    Vector b = letra.GetBaseline().GetEndPoint();
                    Vector arriba = letra.GetAscentLine().GetStartPoint();
                    Vector abajo = letra.GetDescentLine().GetStartPoint();
                    float dx = b.Get(0) - a.Get(0);
                    float dy = b.Get(1) - a.Get(1);
                    float alto = (float)Math.Sqrt(Math.Pow(arriba.Get(0) - abajo.Get(0), 2) + Math.Pow(arriba.Get(1) - abajo.Get(1), 2));

                    // Escrito de lado (una página apaisada girada): se pone en horizontal.
                    (float x0, float x1, float y) =
                        Math.Abs(dx) >= Math.Abs(dy) ? (a.Get(0), b.Get(0), a.Get(1))
                        : dy > 0 ? (a.Get(1), b.Get(1), -a.Get(0))
                        : (-a.Get(1), -b.Get(1), a.Get(0));

                    Letras.Add(new Trozo
                    {
                        Texto = texto,
                        X0 = Math.Min(x0, x1),
                        X1 = Math.Max(x0, x1),
                        Y = y,
                        Alto = alto > 0.5f ? alto : 8f
                    });
                }
                catch (Exception)
                {
                    // Una letra que no se puede situar no cuenta.
                }
            }
        }

        public ICollection<EventType> GetSupportedEvents() => null!;
    }

    private static List<Tabla> LeerPdf(string ruta)
    {
        var paginas = new List<List<List<Trozo>>>();
        int letras = 0;

        using (var lector = new PdfReader(ruta))
        {
            lector.SetUnethicalReading(true);
            using var documento = new PdfDocument(lector);

            for (int p = 1; p <= documento.GetNumberOfPages(); p++)
            {
                var escucha = new EscuchaPdf();
                new PdfCanvasProcessor(escucha).ProcessPageContent(documento.GetPage(p));
                letras += escucha.Letras.Count(l => l.Texto.Trim().Length > 0);
                paginas.Add(LineasPdf(escucha.Letras).Where(l => !EsPie(l)).ToList());
            }
        }

        if (letras < 30)
        {
            throw new NotSupportedException("Es un PDF escaneado (una imagen, sin texto): todavía no se puede leer. Si la lista está también en Excel o Word, elige esa.");
        }

        var tablas = new List<Tabla>();
        List<string[]>? filas = null;
        List<float> cortes = new();
        string firma = "";
        int columnaNombre = -1;
        int desde = 0;
        bool hayTitulos = false;

        void Cerrar(int hasta)
        {
            if (filas is { Count: > 0 })
            {
                tablas.Add(new Tabla { Nombre = desde >= hasta ? $"Página {desde + 1}" : $"Páginas {desde + 1}-{hasta + 1}", Filas = filas });
            }

            filas = null;
        }

        for (int p = 0; p < paginas.Count; p++)
        {
            List<List<Trozo>> lineas = paginas[p];

            if (lineas.Count == 0)
            {
                continue;
            }

            float alto = Mediana(lineas.SelectMany(l => l).Select(w => w.Alto));

            // La fila de títulos: de las 25 primeras líneas, la que más reconoce (al menos dos).
            int cabecera = -1;
            int mejor = 1;

            for (int i = 0; i < Math.Min(25, lineas.Count); i++)
            {
                int reconocidos = Segmentos(lineas[i], alto * 0.6f).Count(s => CampoDeTitulo(s.Texto) != Campo.Ignorar);

                if (reconocidos > mejor)
                {
                    mejor = reconocidos;
                    cabecera = i;
                }
            }

            List<List<Trozo>> datos;

            if (cabecera >= 0)
            {
                hayTitulos = true;
                datos = lineas.Skip(cabecera + 1).ToList();
                cortes = Cortes(datos, lineas[cabecera], alto);
                string[] celdas = Celdas(lineas[cabecera], cortes);
                string nueva = string.Join("|", celdas.Select(Normalizar));

                // Los mismos títulos que la página anterior: la tabla sigue.
                if (filas is null || nueva != firma)
                {
                    Cerrar(p - 1);
                    filas = new List<string[]> { celdas };
                    firma = nueva;
                    desde = p;
                    columnaNombre = Array.FindIndex(celdas, c => CampoDeTitulo(c) is Campo.NombreCompleto or Campo.Nombre or Campo.Apellidos);
                }
            }
            else
            {
                // Sin títulos en esta página: sigue la tabla anterior, con sus columnas.
                datos = lineas;

                if (filas is null)
                {
                    cortes = Cortes(datos, new List<Trozo>(), alto);
                    filas = new List<string[]>();
                    firma = "";
                    desde = p;
                    columnaNombre = -1;
                }
            }

            AnadirFilas(filas, datos, cortes, columnaNombre, alto);
        }

        Cerrar(paginas.Count - 1);

        // ¿Son fichas (un bloque de líneas por persona)? Se queda lo que saque más personas con datos.
        // (Sin repetidos: un rótulo que sale en cada página no son personas.)
        if (FichasPdf(paginas) is { } fichas
            && SinRepetir(Contactos(Analizar(fichas))).Count > SinRepetir(tablas.SelectMany(t => Contactos(Analizar(t)))).Count)
        {
            return new List<Tabla> { fichas };
        }

        return tablas;
    }

    /// <summary>
    /// Las fichas de un PDF: las líneas en bloques de tres maneras (una persona por página,
    /// un bloque por cada nombre en el margen de los nombres, o por los huecos grandes);
    /// la que saque más personas con datos. Null si no son fichas.
    /// </summary>
    private static Tabla? FichasPdf(List<List<List<Trozo>>> paginas)
    {
        Tabla? mejor = null;
        int cuantas = 0;

        foreach (List<Tabla> bloques in new[] { BloquesPorPagina(paginas), BloquesPorNombre(paginas), BloquesPorHuecos(paginas) })
        {
            if (Fichas(bloques) is { } fichas && SinRepetir(Contactos(Analizar(fichas))).Count is int n && n > cuantas)
            {
                mejor = fichas;
                cuantas = n;
            }
        }

        return mejor;
    }

    // Una línea de ficha: sus trozos separados por un hueco grande («Correo» | «ana@…»).
    private static string[] CeldasFicha(List<Trozo> linea) =>
        Segmentos(linea, Mediana(linea.Select(w => w.Alto)) * 1.2f).Select(s => s.Texto).ToArray();

    private static List<Tabla> BloquesPorPagina(List<List<List<Trozo>>> paginas) =>
        paginas
            .Where(lineas => lineas.Count > 0)
            .Select(lineas => new Tabla { Nombre = "Fichas", Filas = lineas.Select(CeldasFicha).ToList() })
            .ToList();

    // Un bloque por cada línea que empieza por un nombre (sin rótulo) en el margen donde empiezan más.
    private static List<Tabla> BloquesPorNombre(List<List<List<Trozo>>> paginas)
    {
        static bool EsNombre(List<Trozo> linea)
        {
            string[] celdas = CeldasFicha(linea);
            return celdas.Length <= 2 && PareceNombre(celdas[0]) && !celdas[0].Contains(':') && Rotulos(celdas).Count == 0;
        }

        List<List<Trozo>> todas = paginas.SelectMany(l => l).ToList();
        List<List<Trozo>> nombres = todas.Where(EsNombre).ToList();
        var bloques = new List<Tabla>();

        if (nombres.Count < 3)
        {
            return bloques;
        }

        float margen = nombres.GroupBy(l => (int)Math.Round(l[0].X0 / 5)).OrderByDescending(g => g.Count()).First().Key * 5f;
        List<string[]>? bloque = null;

        foreach (List<Trozo> linea in todas)
        {
            if (Math.Abs(linea[0].X0 - margen) <= linea[0].Alto && EsNombre(linea))
            {
                bloque = new List<string[]>();
                bloques.Add(new Tabla { Nombre = "Fichas", Filas = bloque });
            }

            bloque?.Add(CeldasFicha(linea));
        }

        return bloques;
    }

    // Un bloque por cada tramo de líneas entre huecos grandes.
    private static List<Tabla> BloquesPorHuecos(List<List<List<Trozo>>> paginas)
    {
        var bloques = new List<Tabla>();

        foreach (List<List<Trozo>> lineas in paginas.Where(l => l.Count > 0))
        {
            float alto = Mediana(lineas.SelectMany(l => l).Select(w => w.Alto));
            float hueco = lineas.Count > 1 ? Mediana(lineas.Zip(lineas.Skip(1), (a, b) => a[0].Y - b[0].Y)) : alto;
            var bloque = new List<string[]>();

            for (int i = 0; i < lineas.Count; i++)
            {
                if (i > 0 && bloque.Count > 0 && lineas[i - 1][0].Y - lineas[i][0].Y > hueco * 1.6f)
                {
                    bloques.Add(new Tabla { Nombre = "Fichas", Filas = bloque });
                    bloque = new List<string[]>();
                }

                bloque.Add(CeldasFicha(lineas[i]));
            }

            if (bloque.Count > 0)
            {
                bloques.Add(new Tabla { Nombre = "Fichas", Filas = bloque });
            }
        }

        return bloques;
    }

    // Las letras de una página en líneas (de arriba abajo), cada una con sus palabras.
    private static List<List<Trozo>> LineasPdf(List<Trozo> letras)
    {
        var lineas = new List<List<Trozo>>();

        if (letras.Count == 0)
        {
            return lineas;
        }

        float alto = Mediana(letras.Select(l => l.Alto));
        var actual = new List<Trozo>();
        float y = 0;

        foreach (Trozo letra in letras.OrderByDescending(l => l.Y))
        {
            if (actual.Count > 0 && y - letra.Y > alto * 0.35f)
            {
                lineas.Add(UnirLetras(actual, alto));
                actual = new List<Trozo>();
            }

            if (actual.Count == 0)
            {
                y = letra.Y;
            }

            actual.Add(letra);
        }

        lineas.Add(UnirLetras(actual, alto));

        return lineas.Where(l => l.Count > 0).ToList();
    }

    // Las letras de una línea en palabras: se corta en los espacios y en los huecos.
    private static List<Trozo> UnirLetras(List<Trozo> letras, float alto)
    {
        var palabras = new List<Trozo>();
        Trozo? palabra = null;
        Trozo? previa = null;

        foreach (Trozo letra in letras.OrderBy(l => l.X0))
        {
            // La misma letra dos veces casi en el mismo sitio (negrita fingida): una.
            if (previa is not null && letra.Texto == previa.Texto && Math.Abs(letra.X0 - previa.X0) < alto * 0.1f)
            {
                continue;
            }

            previa = letra;
            bool espacio = letra.Texto.Trim().Length == 0;

            if (palabra is not null && (espacio || letra.X0 - palabra.X1 > alto * 0.15f))
            {
                palabras.Add(palabra);
                palabra = null;
            }

            if (espacio)
            {
                continue;
            }

            if (palabra is null)
            {
                palabra = new Trozo { Texto = letra.Texto, X0 = letra.X0, X1 = letra.X1, Y = letra.Y, Alto = letra.Alto };
            }
            else
            {
                palabra.Texto += letra.Texto;
                palabra.X1 = Math.Max(palabra.X1, letra.X1);
            }
        }

        if (palabra is not null)
        {
            palabras.Add(palabra);
        }

        return palabras;
    }

    // Las palabras de una línea en trozos: se separan donde hay más hueco que «hueco».
    private static List<Trozo> Segmentos(List<Trozo> linea, float hueco)
    {
        var trozos = new List<Trozo>();

        foreach (Trozo palabra in linea.OrderBy(w => w.X0))
        {
            if (trozos.Count > 0 && palabra.X0 - trozos[^1].X1 <= hueco)
            {
                trozos[^1].Texto += " " + palabra.Texto;
                trozos[^1].X1 = Math.Max(trozos[^1].X1, palabra.X1);
            }
            else
            {
                trozos.Add(new Trozo { Texto = palabra.Texto, X0 = palabra.X0, X1 = palabra.X1, Y = palabra.Y, Alto = palabra.Alto });
            }
        }

        return trozos;
    }

    /// <summary>
    /// Dónde se cortan las columnas: en los huecos entre los datos que no tapa ninguna
    /// palabra (de las líneas con varias partes). Dentro de cada hueco, donde no hay
    /// palabras de la fila de títulos y, si se puede, fuera de un título; si el hueco cae
    /// dentro de uno, es que eran dos títulos juntos («Grupo Email»). Y entre dos títulos
    /// sin corte entre ellos (los datos los cruzan), por donde menos se tapa.
    /// </summary>
    private static List<float> Cortes(List<List<Trozo>> lineas, List<Trozo> cabecera, float alto)
    {
        List<List<Trozo>> conPartes = lineas.Where(l => Segmentos(l, alto).Count >= 2).ToList();
        List<Trozo> palabras = conPartes.SelectMany(l => l).ToList();
        List<Trozo> segmentos = Segmentos(cabecera, alto * 0.6f);
        List<Trozo> titulos = segmentos.Where(s => CampoDeTitulo(s.Texto) != Campo.Ignorar).ToList();
        List<Trozo> todas = palabras.Concat(cabecera).ToList();
        var cortes = new List<float>();

        if (todas.Count == 0)
        {
            return cortes;
        }

        const float Paso = 0.5f;
        float inicio = todas.Min(p => p.X0);
        int casillas = Math.Min(20000, (int)Math.Ceiling((todas.Max(p => p.X1) - inicio) / Paso) + 1);
        var tapado = new int[casillas];

        int Casilla(float x) => Math.Clamp((int)((x - inicio) / Paso), 0, casillas - 1);

        foreach (Trozo palabra in palabras)
        {
            for (int i = Casilla(palabra.X0); i <= Casilla(palabra.X1); i++)
            {
                tapado[i]++;
            }
        }

        int minimo = Math.Max(1, (int)Math.Ceiling(alto * 0.3f / Paso));

        for (int i = 0; i < casillas;)
        {
            if (tapado[i] > 0)
            {
                i++;
                continue;
            }

            int j = i;

            while (j < casillas && tapado[j] == 0)
            {
                j++;
            }

            if (i > 0 && j < casillas && j - i >= minimo)
            {
                List<(float X0, float X1)> libres = Libres(inicio + i * Paso, inicio + j * Paso, cabecera);
                var fuera = libres.Where(l => !titulos.Any(t => t.X0 < (l.X0 + l.X1) / 2 && (l.X0 + l.X1) / 2 < t.X1)).ToList();

                List<(float X0, float X1)> elegibles = fuera.Count > 0 ? fuera : libres;

                if (elegibles.Count > 0)
                {
                    var hueco = elegibles.OrderByDescending(l => l.X1 - l.X0).First();
                    cortes.Add((hueco.X0 + hueco.X1) / 2);
                }
            }

            i = j;
        }

        // Dos títulos sin corte entre ellos: si los dos se reconocen, se corta aunque los datos
        // los crucen; si no («Grupo» más ancho que su número, tapando el hueco), solo donde
        // (casi) nada tapa.
        List<Trozo> ordenados = segmentos.OrderBy(t => t.X0).ToList();

        for (int k = 1; k < ordenados.Count; k++)
        {
            float a = (ordenados[k - 1].X0 + ordenados[k - 1].X1) / 2;
            float b = (ordenados[k].X0 + ordenados[k].X1) / 2;

            if (cortes.Any(c => a < c && c < b))
            {
                continue;
            }

            float desde = Math.Min(ordenados[k - 1].X1, ordenados[k].X0);
            float hasta = Math.Max(ordenados[k - 1].X1, ordenados[k].X0);
            float medio = (desde + hasta) / 2;
            int menos = Casilla(medio);

            for (int i = Casilla(desde); i <= Casilla(hasta); i++)
            {
                if (tapado[i] < tapado[menos] || (tapado[i] == tapado[menos] && Math.Abs(i - Casilla(medio)) < Math.Abs(menos - Casilla(medio))))
                {
                    menos = i;
                }
            }

            bool reconocidos = titulos.Contains(ordenados[k - 1]) && titulos.Contains(ordenados[k]);

            if (reconocidos || tapado[menos] <= Math.Max(1, conPartes.Count / 10))
            {
                cortes.Add(inicio + menos * Paso);
            }
        }

        return cortes.OrderBy(c => c).ToList();
    }

    // Los tramos de [a, b] que no tapa ninguna de esas palabras.
    private static List<(float X0, float X1)> Libres(float a, float b, List<Trozo> palabras)
    {
        var libres = new List<(float X0, float X1)>();
        float desde = a;

        foreach (Trozo palabra in palabras.Where(p => p.X1 > a && p.X0 < b).OrderBy(p => p.X0))
        {
            if (palabra.X0 > desde)
            {
                libres.Add((desde, palabra.X0));
            }

            desde = Math.Max(desde, palabra.X1);
        }

        if (b > desde)
        {
            libres.Add((desde, b));
        }

        return libres.Where(l => l.X1 - l.X0 >= 0.5f).ToList();
    }

    private static string[] Celdas(List<Trozo> linea, List<float> cortes)
    {
        var celdas = new string[cortes.Count + 1];

        foreach (Trozo palabra in linea.OrderBy(w => w.X0))
        {
            int columna = cortes.Count(c => c <= palabra.X0 + 0.1f);
            celdas[columna] = celdas[columna] is null ? palabra.Texto : celdas[columna] + " " + palabra.Texto;
        }

        return celdas.Select(c => c ?? "").ToArray();
    }

    /// <summary>
    /// Cada línea, una fila; la que va pegada a la anterior sin nombre (una dirección en
    /// dos líneas) se suma a ella. Si las filas están más separadas que las líneas de una
    /// celda, también la que va más pegada (un nombre largo en dos líneas).
    /// </summary>
    private static void AnadirFilas(List<string[]> filas, List<List<Trozo>> lineas, List<float> cortes, int columnaNombre, float alto)
    {
        float hueco = lineas.Count > 1 ? Mediana(lineas.Zip(lineas.Skip(1), (a, b) => a[0].Y - b[0].Y)) : alto * 2;
        int primera = filas.Count;
        float anterior = float.NaN;

        foreach (List<Trozo> linea in lineas)
        {
            string[] celdas = Celdas(linea, cortes);
            float distancia = float.IsNaN(anterior) ? float.MaxValue : anterior - linea[0].Y;
            anterior = linea[0].Y;

            bool sinNombre = columnaNombre >= 0 && columnaNombre < celdas.Length && celdas[columnaNombre].Length == 0;
            bool pegada = filas.Count > primera
                && distancia <= alto * 2f
                && (sinNombre || (hueco >= alto * 1.4f && distancia < hueco * 0.7f));

            if (!pegada)
            {
                filas.Add(celdas);
                continue;
            }

            string[] ultima = filas[^1];

            for (int c = 0; c < Math.Min(ultima.Length, celdas.Length); c++)
            {
                if (celdas[c].Length > 0)
                {
                    ultima[c] = (ultima[c] + " " + celdas[c]).Trim();
                }
            }
        }
    }

    // «3», «- 3 -», «Página 3», «Pág. 3 de 5», «3/5»: el número de página.
    private static bool EsPie(List<Trozo> linea) =>
        Regex.IsMatch(Normalizar(string.Join(" ", linea.Select(p => p.Texto))), @"^(pag|pagina)?\s*\d{1,3}(\s*(de)?\s*\d{1,3})?$");

    private static float Mediana(IEnumerable<float> valores)
    {
        List<float> lista = valores.OrderBy(v => v).ToList();
        return lista.Count == 0 ? 10f : lista[lista.Count / 2];
    }
}
