using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace SCCosmos
{
    public partial class MapaCoordenadasWindow : Window
    {
        public double LatitudFinal { get; private set; }
        public double LongitudFinal { get; private set; }

        private static readonly HttpClient _httpClient = new HttpClient();

        public MapaCoordenadasWindow(double latInicial, double lonInicial, string direccionContexto)
        {
            InitializeComponent();

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 SCCosmos/1.0");

            LatitudFinal = latInicial;
            LongitudFinal = lonInicial;

            TxtBuscador.Text = direccionContexto;
        }

        private void WbMapa_Loaded(object sender, RoutedEventArgs e)
        {
            // Conectamos el mapa HTML con nuestro código C#
            WbMapa.ObjectForScripting = new MapInterop(this);

            string html = @"
            <!DOCTYPE html>
            <html>
            <head>
                <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                <link rel='stylesheet' href='https://unpkg.com/leaflet@1.7.1/dist/leaflet.css' />
                <script src='https://unpkg.com/leaflet@1.7.1/dist/leaflet.js'></script>
                <style> body, html, #map { margin: 0; padding: 0; height: 100%; width: 100%; background-color: #2A2A2A; } </style>
            </head>
            <body>
                <div id='map'></div>
                <script>
                    var map = L.map('map').setView([40.4168, -3.7038], 6); // Por defecto España
                    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(map);
                    var marker;

                    function setViewAndMarker(lat, lng, zoom) {
                        var latlng = new L.LatLng(lat, lng);
                        map.setView(latlng, zoom);
                        if(marker) map.removeLayer(marker);
                        marker = L.marker(latlng).addTo(map);
                    }

                    map.on('click', function(e) {
                        if(marker) map.removeLayer(marker);
                        marker = L.marker(e.latlng).addTo(map);
                        // Enviamos los datos a C#
                        window.external.SetCoords(e.latlng.lat.toString(), e.latlng.lng.toString());
                    });
                </script>
            </body>
            </html>";

            WbMapa.NavigateToString(html);

            // Si ya teníamos coordenadas, centramos el mapa allí tras un segundo de carga
            if (LatitudFinal != 0 && LongitudFinal != 0)
            {
                TxtCoordenadas.Text = $"{LatitudFinal:F5}, {LongitudFinal:F5}";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    try { WbMapa.InvokeScript("setViewAndMarker", new object[] { LatitudFinal, LongitudFinal, 17 }); } catch { }
                };
                timer.Start();
            }
        }

        // Esta función es llamada desde el Javascript del HTML al hacer clic
        public void ActualizarDesdeMapa(double lat, double lon)
        {
            LatitudFinal = lat;
            LongitudFinal = lon;
            TxtCoordenadas.Text = $"{LatitudFinal:F5}, {LongitudFinal:F5}";
        }

        private async void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtBuscador.Text)) return;

            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(TxtBuscador.Text)}&format=json&limit=1";
                string response = await _httpClient.GetStringAsync(url);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var resultados = JsonSerializer.Deserialize<System.Collections.Generic.List<FormularioPersonaWindow.NominatimResult>>(response, options);

                if (resultados != null && resultados.Count > 0)
                {
                    double lat = double.Parse(resultados[0].lat, System.Globalization.CultureInfo.InvariantCulture);
                    double lon = double.Parse(resultados[0].lon, System.Globalization.CultureInfo.InvariantCulture);

                    LatitudFinal = lat;
                    LongitudFinal = lon;
                    TxtCoordenadas.Text = $"{lat:F5}, {lon:F5}";

                    try { WbMapa.InvokeScript("setViewAndMarker", new object[] { lat, lon, 16 }); } catch { }
                }
                else MessageBox.Show("No se encontró esa dirección.", "Búsqueda", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void TxtBuscador_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnBuscar_Click(null, null);
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (LatitudFinal == 0 && LongitudFinal == 0)
            {
                MessageBox.Show("Debes hacer clic en el mapa para seleccionar un punto.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }

    // Clase puente obligatoria para que el navegador WebBrowser pueda hablar con C#
    [System.Runtime.InteropServices.ComVisible(true)]
    public class MapInterop
    {
        private MapaCoordenadasWindow _window;
        public MapInterop(MapaCoordenadasWindow window) { _window = window; }

        public void SetCoords(string latStr, string lonStr)
        {
            if (double.TryParse(latStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(lonStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon))
            {
                _window.ActualizarDesdeMapa(lat, lon);
            }
        }
    }
}