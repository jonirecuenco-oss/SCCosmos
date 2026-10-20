using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SCCosmos
{
    public class RecomendacionHub
    {
        public string Tipo { get; set; }
        public string Congregacion { get; set; }
        public string UrlDetalle { get; set; }
        public string Fecha { get; set; }
        public string Hermano { get; set; }
        public string RecomendadoComo { get; set; } // NUEVA

        // Solo las recomendaciones de tipo «Entre visitas» pueden aprobarse
        // o cancelarse (las de «Visita regular» quedan pendientes en Hub).
        public bool EsProcesable =>
            string.Equals((Tipo ?? "").Trim(), "Entre visitas", StringComparison.OrdinalIgnoreCase);

        public List<ArchivoDocumento> ArchivosAdjuntos { get; set; }
    }

    public static class ExtractorNombramientosHelper
    {
        public static List<RecomendacionHub> ExtraerRecomendaciones(string htmlNombramientos)
        {
            var resultados = new List<RecomendacionHub>();

            // 1. Partimos el HTML por bloques de tipos de visita
            string[] partes = Regex.Split(htmlNombramientos, @"<h3[^>]*>.*?<ptrn-translate>(Entre visitas|Visita regular)</ptrn-translate>");

            for (int i = 1; i < partes.Length; i += 2)
            {
                string tipoVisita = partes[i];
                string contenidoBloque = partes[i + 1];

                // 2. Buscamos cada tarjeta dentro de este bloque
                var regexTarjeta = new Regex(@"<article class=""card card--clickable[^>]*>.*?</article>", RegexOptions.Singleline);
                var matchesTarjetas = regexTarjeta.Matches(contenidoBloque);

                foreach (Match match in matchesTarjetas)
                {
                    string tarjetaHtml = match.Value;

                    // A) Extraer URL y Congregación
                    var matchEnlace = Regex.Match(tarjetaHtml, @"<a ptrnactiontarget="""" href=""([^""]+)""[^>]*>\s*(.*?)\s*</a>");
                    string url = matchEnlace.Success ? matchEnlace.Groups[1].Value : "";
                    string congre = matchEnlace.Success ? matchEnlace.Groups[2].Value.Trim() : "";

                    // B) Extraer la Fecha (Busca "Enviado el" o "Próxima visita" y captura el valor del <dd>)
                    var matchFecha = Regex.Match(tarjetaHtml, @"<dt[^>]*>.*?(?:Enviado el|Próxima visita).*?</dt>\s*<dd[^>]*>(.*?)</dd>", RegexOptions.Singleline);
                    string fecha = matchFecha.Success ? matchFecha.Groups[1].Value.Trim() : "";

                    // C) Extraer el Hermano Recomendado (Dentro del span de transliteration)
                    var matchHermano = Regex.Match(tarjetaHtml, @"<span class=""transliteration__vernacular[^>]*>(.*?)</span>");
                    string hermano = matchHermano.Success ? matchHermano.Groups[1].Value.Trim() : "";

                    if (!string.IsNullOrEmpty(url))
                    {
                        resultados.Add(new RecomendacionHub
                        {
                            Tipo = tipoVisita,
                            Congregacion = congre,
                            UrlDetalle = url,
                            Fecha = fecha,
                            Hermano = hermano,
                            ArchivosAdjuntos = new List<ArchivoDocumento>() // Lo inicializamos vacío por seguridad
                        });
                    }
                }
            }

            return resultados;
        }
    }
}