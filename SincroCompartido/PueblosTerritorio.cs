#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

/// <summary>Un pueblo del territorio de una congregación (además del del salón).</summary>
public sealed record PuebloTerritorio(
    string Ine,
    double Latitud,
    double Longitud)
{
    public string Nombre => TiempoAemet.NombreMunicipio(Ine, Ine);
}


/// <summary>
/// Los pueblos del territorio de cada congregación, para ver su tiempo y
/// avisar de sus avisos rojos. Van en Configuracion
/// («Tiempo_Pueblos_{código}» = «ine|lat|lon;…»), que se sincroniza entre el
/// PC y el móvil, sin tocar las tablas.
/// </summary>
public static class PueblosTerritorio
{
    public const string Prefijo = "Tiempo_Pueblos_";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly HttpClient Http = CrearHttp();


    private static HttpClient CrearHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // OpenStreetMap pide que cada app diga quién es.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SCCosmos/2.1");

        return http;
    }


    public static string Clave(string codigoCongregacion) =>
        Prefijo + (codigoCongregacion ?? "").Trim();


    public static List<PuebloTerritorio> Leer(string? valor)
    {
        var lista = new List<PuebloTerritorio>();

        foreach (string parte in (valor ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] c = parte.Split('|');

            if (c.Length >= 3
                && c[0].Trim().Length == 5
                && double.TryParse(c[1], NumberStyles.Float, Inv, out double latitud)
                && double.TryParse(c[2], NumberStyles.Float, Inv, out double longitud)
                && lista.All(p => p.Ine != c[0].Trim()))
            {
                lista.Add(new PuebloTerritorio(c[0].Trim(), latitud, longitud));
            }
        }

        return lista;
    }


    public static string Escribir(IEnumerable<PuebloTerritorio> pueblos) =>
        string.Join(
            ";",
            pueblos
                .GroupBy(p => p.Ine)
                .Select(g => g.First())
                .Select(p => $"{p.Ine}|{p.Latitud.ToString("0.####", Inv)}|{p.Longitud.ToString("0.####", Inv)}"));


    /// <summary>
    /// Los pueblos donde viven sus publicadores (el que va tras el código
    /// postal de la dirección), de la provincia del salón y sin el suyo, con
    /// cuántos son: para elegir los del territorio.
    /// </summary>
    public static List<(string Ine, string Nombre, int Personas)> Sugerencias(
        IEnumerable<string> direcciones,
        string ineSalon)
    {
        string provincia = ineSalon.Length >= 2 ? ineSalon[..2] : "--";
        string nombreProvincia = TiempoAemet.ProvinciaDe(ineSalon);

        var cuenta = new Dictionary<string, int>();

        foreach (string direccion in direcciones)
        {
            // «…, 10300 Navalmoral de la Mata», «10310 - TALAYUELA (Cáceres)».
            Match m = Regex.Match(direccion ?? "", @"\b(\d{5})\s*-?\s*([^,(\d]+)");

            if (!m.Success || !m.Groups[1].Value.StartsWith(provincia, StringComparison.Ordinal))
            {
                continue;
            }

            string? ine = TiempoAemet.BuscarIne(m.Groups[2].Value.Trim(' ', '-', '.'), nombreProvincia);

            if (ine is not null
                && ine != ineSalon
                && ine.StartsWith(provincia, StringComparison.Ordinal))
            {
                cuenta[ine] = cuenta.GetValueOrDefault(ine) + 1;
            }
        }

        return cuenta
            .Select(x => (Ine: x.Key, Nombre: TiempoAemet.NombreMunicipio(x.Key, x.Key), Personas: x.Value))
            .OrderByDescending(x => x.Personas)
            .ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }


    /// <summary>
    /// Coordenadas del pueblo (de OpenStreetMap, como las direcciones), para
    /// saber en qué zona de aviso de AEMET cae. Null si no se encuentran.
    /// </summary>
    public static async Task<(double Latitud, double Longitud)?> CoordenadasAsync(
        string ine,
        CancellationToken ct)
    {
        try
        {
            string consulta =
                $"{TiempoAemet.NombreMunicipio(ine, ine)}, {TiempoAemet.ProvinciaDe(ine)}, España";

            using HttpResponseMessage respuesta =
                await Http.GetAsync(
                    "https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=es&q="
                    + Uri.EscapeDataString(consulta),
                    ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument documento =
                JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));

            if (documento.RootElement.ValueKind != JsonValueKind.Array
                || documento.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement sitio = documento.RootElement[0];

            return double.TryParse(sitio.GetProperty("lat").GetString(), NumberStyles.Float, Inv, out double latitud)
                   && double.TryParse(sitio.GetProperty("lon").GetString(), NumberStyles.Float, Inv, out double longitud)
                ? (latitud, longitud)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
