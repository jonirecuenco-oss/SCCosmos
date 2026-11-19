#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// DIRECCIONES CON TOMTOM (Search API, búsqueda «fuzzy»)
//
// El buscador de direcciones del formulario de los ancianos cuando
// no hay clave de Google: el navegador se lo pide al PC y el PC a
// TomTom (la clave no sale en la página). Cada sugerencia ya trae
// sus coordenadas: una sola consulta por búsqueda. Plan gratuito:
// 2.500 consultas al día, sin tarjeta (la clave es la misma que usa
// el móvil para los tiempos de viaje).
// ============================================================

public static class LugaresTomTom
{
    /// <summary>La clave de TomTom (Configuración; la misma del móvil para los tiempos de viaje).</summary>
    public const string ClaveApi = "TomTomApiKey";

    private const string UrlBusqueda = "https://api.tomtom.com/search/2/search/";


    /// <summary>
    /// Sugerencias para lo escrito: «[{"texto","lat","lon"}]» (null si TomTom no
    /// contesta bien). centro: la congregación, para que salga antes lo cercano.
    /// </summary>
    public static async Task<string?> SugerenciasAsync(
        HttpClient http,
        string clave,
        string texto,
        (double Lat, double Lon)? centro,
        CancellationToken token)
    {
        // La consulta va en la ruta: sin barras ni caracteres de control.
        string consulta = new string((texto ?? "").Select(c => c is '/' or '\\' || char.IsControl(c) ? ' ' : c).ToArray()).Trim();

        if (clave.Length == 0 || consulta.Length < 3 || consulta.Length > 200)
        {
            return null;
        }

        string url =
            UrlBusqueda + Uri.EscapeDataString(consulta) + ".json"
            + "?key=" + Uri.EscapeDataString(clave)
            + "&countrySet=ES&language=es-ES&limit=5&typeahead=true";

        if (centro is { } c && (c.Lat != 0 || c.Lon != 0))
        {
            url += "&lat=" + c.Lat.ToString(CultureInfo.InvariantCulture)
                + "&lon=" + c.Lon.ToString(CultureInfo.InvariantCulture);
        }

        JsonNode? respuesta;

        try
        {
            using HttpResponseMessage r = await http.GetAsync(url, token);

            if (!r.IsSuccessStatusCode)
            {
                return null;
            }

            respuesta = JsonNode.Parse(await r.Content.ReadAsStringAsync(token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }

        var lista = new JsonArray();

        foreach (JsonNode? resultado in respuesta?["results"]?.AsArray() ?? new JsonArray())
        {
            string direccion = resultado?["address"]?["freeformAddress"]?.GetValue<string>()?.Trim() ?? "";
            string nombre = resultado?["poi"]?["name"]?.GetValue<string>()?.Trim() ?? "";
            double lat = Numero(resultado?["position"]?["lat"]);
            double lon = Numero(resultado?["position"]?["lon"]);

            // Un sitio (bar, salón…): su nombre delante de la dirección.
            string textoLugar = nombre.Length > 0 && direccion.Length > 0 ? $"{nombre}, {direccion}" : nombre + direccion;

            if (textoLugar.Length > 0 && (lat != 0 || lon != 0))
            {
                lista.Add(new JsonObject { ["texto"] = textoLugar, ["lat"] = lat, ["lon"] = lon });
            }
        }

        return lista.ToJsonString();
    }


    private static double Numero(JsonNode? nodo)
    {
        try
        {
            return nodo?.GetValue<double>() ?? 0;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return double.TryParse(nodo?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double valor) ? valor : 0;
        }
    }
}
