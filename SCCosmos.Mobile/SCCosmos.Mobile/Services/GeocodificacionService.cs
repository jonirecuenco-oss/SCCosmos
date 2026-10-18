using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Geocodificación de direcciones mediante Nominatim (OpenStreetMap),
/// el mismo servicio que usa WPF. Respeta la política de uso:
/// una sola petición a la vez y cabecera User-Agent identificativa.
/// </summary>
public static class GeocodificacionService
{
    private static readonly HttpClient Cliente =
        CrearCliente();

    private static HttpClient CrearCliente()
    {
        var cliente =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(15)
            };

        cliente.DefaultRequestHeaders
            .UserAgent
            .ParseAdd(
                "SCCosmos.Mobile/1.0 (gestion de circuito)");

        return cliente;
    }

    public sealed class ResultadoGeocodificacion
    {
        public bool Ok { get; init; }
        public double Latitud { get; init; }
        public double Longitud { get; init; }
        public string Error { get; init; } = "";
    }

    public static async Task<ResultadoGeocodificacion>
        BuscarAsync(
            string direccion,
            CancellationToken cancelacion =
                default)
    {
        if (string.IsNullOrWhiteSpace(
                direccion))
        {
            return new ResultadoGeocodificacion
            {
                Ok = false,
                Error = "La persona no tiene dirección registrada."
            };
        }

        try
        {
            string url =
                "https://nominatim.openstreetmap.org/search" +
                "?format=json&limit=1&q=" +
                Uri.EscapeDataString(
                    direccion.Trim());

            List<ElementoNominatim>? elementos =
                await Cliente.GetFromJsonAsync<
                    List<ElementoNominatim>>(
                        url,
                        cancelacion);

            ElementoNominatim? primero =
                elementos?.FirstOrDefault();

            if (primero is null)
            {
                return new ResultadoGeocodificacion
                {
                    Ok = false,
                    Error =
                        "No se encontró la dirección en el mapa."
                };
            }

            if (!double.TryParse(
                    primero.Lat,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double lat)
                ||
                !double.TryParse(
                    primero.Lon,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double lon))
            {
                return new ResultadoGeocodificacion
                {
                    Ok = false,
                    Error =
                        "La respuesta del servicio no tiene coordenadas válidas."
                };
            }

            return new ResultadoGeocodificacion
            {
                Ok = true,
                Latitud = lat,
                Longitud = lon
            };
        }
        catch (Exception ex)
        {
            return new ResultadoGeocodificacion
            {
                Ok = false,
                Error =
                    "No se pudo consultar el mapa: " +
                    ex.Message
            };
        }
    }

    private sealed class ElementoNominatim
    {
        [JsonPropertyName("lat")]
        public string Lat { get; set; } = "";

        [JsonPropertyName("lon")]
        public string Lon { get; set; } = "";
    }
}
