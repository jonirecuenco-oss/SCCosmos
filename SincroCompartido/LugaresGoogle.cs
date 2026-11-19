#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// DIRECCIONES CON GOOGLE MAPS (Places API (New))
//
// El buscador de direcciones del formulario de los ancianos: el
// navegador se lo pide al PC (ServidorProgramaWeb) y el PC a Google,
// así la clave no sale nunca en la página. Mientras escriben,
// sugerencias (Autocomplete); al elegir una, sus coordenadas (Place
// Details, solo «location» y «formattedAddress»: el nivel Essentials).
// Las dos peticiones llevan el mismo token de sesión: Google cobra la
// sesión como una sola consulta de detalles. Sin clave o si Google
// falla, TomTom (LugaresTomTom) y, si tampoco, OpenStreetMap (Photon).
// ============================================================

public static class LugaresGoogle
{
    /// <summary>La clave de la API de Google Maps (Configuración; vacía: sin Google).</summary>
    public const string ClaveApi = "GoogleMaps_ClaveApi";

    private const string UrlSugerencias = "https://places.googleapis.com/v1/places:autocomplete";

    private const string UrlLugar = "https://places.googleapis.com/v1/places/";

    // Alrededor de la congregación (las direcciones de fuera también salen, más abajo).
    private const double RadioSesgo = 30000;

    private static readonly Regex IdValido = new("^[A-Za-z0-9_-]{10,300}$", RegexOptions.Compiled);

    private static readonly Regex SesionValida = new("^[A-Za-z0-9_-]{8,64}$", RegexOptions.Compiled);


    public static bool IdLugarValido(string? id) => id is not null && IdValido.IsMatch(id);

    public static bool SesionValidaTexto(string? sesion) => sesion is not null && SesionValida.IsMatch(sesion);


    /// <summary>
    /// Sugerencias para lo escrito: «[{"id","texto"}]» (null si Google no
    /// contesta bien). centro: la congregación, para que salga antes lo cercano.
    /// </summary>
    public static async Task<string?> SugerenciasAsync(
        HttpClient http,
        string clave,
        string texto,
        string sesion,
        (double Lat, double Lon)? centro,
        CancellationToken token)
    {
        texto = (texto ?? "").Trim();

        if (clave.Length == 0 || texto.Length < 3 || texto.Length > 200 || !SesionValidaTexto(sesion))
        {
            return null;
        }

        var cuerpo = new JsonObject
        {
            ["input"] = texto,
            ["languageCode"] = "es",
            ["regionCode"] = "es",
            ["includedRegionCodes"] = new JsonArray(JsonValue.Create("es")),
            ["sessionToken"] = sesion
        };

        if (centro is { } c && (c.Lat != 0 || c.Lon != 0))
        {
            cuerpo["locationBias"] = new JsonObject
            {
                ["circle"] = new JsonObject
                {
                    ["center"] = new JsonObject { ["latitude"] = c.Lat, ["longitude"] = c.Lon },
                    ["radius"] = RadioSesgo
                }
            };
        }

        using var peticion = new HttpRequestMessage(HttpMethod.Post, UrlSugerencias)
        {
            Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json")
        };

        peticion.Headers.Add("X-Goog-Api-Key", clave);
        peticion.Headers.Add("X-Goog-FieldMask", "suggestions.placePrediction.placeId,suggestions.placePrediction.text.text");

        JsonNode? respuesta = await PedirAsync(http, peticion, token);

        if (respuesta is null)
        {
            return null;
        }

        var lista = new JsonArray();

        foreach (JsonNode? sugerencia in respuesta["suggestions"]?.AsArray() ?? new JsonArray())
        {
            string id = sugerencia?["placePrediction"]?["placeId"]?.GetValue<string>() ?? "";
            string nombre = SinPais(sugerencia?["placePrediction"]?["text"]?["text"]?.GetValue<string>() ?? "");

            if (IdLugarValido(id) && nombre.Length > 0)
            {
                lista.Add(new JsonObject { ["id"] = id, ["texto"] = nombre });
            }
        }

        return lista.ToJsonString();
    }


    /// <summary>Las coordenadas del lugar elegido: «{"lat","lon","direccion"}» (null si Google no contesta bien).</summary>
    public static async Task<string?> LugarAsync(
        HttpClient http,
        string clave,
        string id,
        string sesion,
        CancellationToken token)
    {
        if (clave.Length == 0 || !IdLugarValido(id) || !SesionValidaTexto(sesion))
        {
            return null;
        }

        using var peticion = new HttpRequestMessage(
            HttpMethod.Get,
            UrlLugar + id + "?languageCode=es&regionCode=es&sessionToken=" + Uri.EscapeDataString(sesion));

        peticion.Headers.Add("X-Goog-Api-Key", clave);
        peticion.Headers.Add("X-Goog-FieldMask", "location,formattedAddress");

        JsonNode? lugar = await PedirAsync(http, peticion, token);

        double lat = Numero(lugar?["location"]?["latitude"]);
        double lon = Numero(lugar?["location"]?["longitude"]);

        if (lugar is null || (lat == 0 && lon == 0))
        {
            return null;
        }

        return new JsonObject
        {
            ["lat"] = lat,
            ["lon"] = lon,
            ["direccion"] = SinPais(lugar["formattedAddress"]?.GetValue<string>() ?? "")
        }.ToJsonString();
    }


    // «…, Azuaga, España» → «…, Azuaga»: todas son de España.
    private static string SinPais(string texto)
    {
        texto = texto.Trim();

        return texto.EndsWith(", España", StringComparison.OrdinalIgnoreCase)
            ? texto[..^", España".Length].TrimEnd()
            : texto;
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


    private static async Task<JsonNode?> PedirAsync(HttpClient http, HttpRequestMessage peticion, CancellationToken token)
    {
        try
        {
            using HttpResponseMessage respuesta = await http.SendAsync(peticion, token);

            if (!respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            return JsonNode.Parse(await respuesta.Content.ReadAsStringAsync(token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
