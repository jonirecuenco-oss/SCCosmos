using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class ProgramarActividadWindow : Window
    {
        private readonly string _dia;
        private readonly string _congreCodigo;
        private readonly string _mesReferencia;
        private readonly int _actividadEditarId;

        private double _latitud;
        private double _longitud;

        private bool _cargandoInicial;
        private bool _cargandoDireccion;

        private List<OpcionPersonaPrograma> _personas =
            new List<OpcionPersonaPrograma>();

        // =====================================================
        // ALTA
        // =====================================================
        public ProgramarActividadWindow(
            string responsable,
            string turno,
            string dia,
            string congreCodigo,
            string mesReferencia)
            : this(
                responsable,
                turno,
                dia,
                congreCodigo,
                mesReferencia,
                null)
        {
        }

        // =====================================================
        // EDICIÓN
        // =====================================================
        public ProgramarActividadWindow(
            string responsable,
            string turno,
            string dia,
            string congreCodigo,
            string mesReferencia,
            DatabaseHelper.ActividadPrograma actividadEditar)
        {
            InitializeComponent();

            _dia = dia ?? "";
            _congreCodigo = congreCodigo ?? "";
            _mesReferencia = mesReferencia ?? "";
            _actividadEditarId = actividadEditar?.Id ?? 0;

            _cargandoInicial = true;

            string nombreCongregacion =
                DatabaseHelper
                    .ObtenerCongregaciones()
                    .FirstOrDefault(c => c.Codigo == _congreCodigo)
                    ?.Nombre
                ?? _congreCodigo;

            TxtContexto.Text =
                string.IsNullOrWhiteSpace(nombreCongregacion)
                    ? _dia
                    : $"{_dia} · {nombreCongregacion}";

            CargarPersonas();

            string responsableInicial =
                NormalizarResponsable(
                    actividadEditar?.Responsable
                    ?? responsable);

            SeleccionarResponsable(responsableInicial);

            if (actividadEditar != null)
            {
                TxtTitulo.Text = "EDITAR ACTIVIDAD";

                TxtHora.Text =
                    actividadEditar.Hora ?? "";

                string tipo =
                    actividadEditar.TipoActividad ?? "";

                // Compatibilidad con registros del programa anterior,
                // donde Comida/Cenar/Dormir estaban guardados en Responsable.
                if (string.IsNullOrWhiteSpace(tipo)
                    && EsResponsableLegacyActividad(
                        actividadEditar.Responsable))
                {
                    tipo = actividadEditar.Responsable;
                }

                TxtTipoActividad.Text = tipo;

                TxtNotas.Text =
                    actividadEditar.NotasLibro ?? "";

                int companeroSeleccionado =
                    ResolverIdRaizPersona(
                        actividadEditar.CompaneroId);

                CmbAcompanante.SelectedValue =
                    companeroSeleccionado;

                _latitud = actividadEditar.Latitud;
                _longitud = actividadEditar.Longitud;

                _cargandoDireccion = true;
                TxtDireccion.Text =
                    actividadEditar.Direccion ?? "";
                _cargandoDireccion = false;
            }
            else
            {
                TxtTitulo.Text = "PROGRAMAR ACTIVIDAD";
            }

            _cargandoInicial = false;
            ActualizarTextoCoordenadas();
        }

        // =====================================================
        // RESPONSABLE
        // =====================================================
        private static string NormalizarResponsable(
            string responsable)
        {
            string valor =
                responsable?.Trim() ?? "";

            if (string.Equals(
                    valor,
                    "Ambos",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Ambos";
            }

            if (string.Equals(
                    valor,
                    "Rebe",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    valor,
                    "Esposa",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Esposa";
            }

            return "SC";
        }

        private static bool EsResponsableLegacyActividad(
            string valor)
        {
            return
                string.Equals(
                    valor,
                    "Comida",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    valor,
                    "Cenar",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    valor,
                    "Dormir",
                    StringComparison.OrdinalIgnoreCase);
        }

        private void SeleccionarResponsable(
            string responsable)
        {
            foreach (ComboBoxItem item in
                     CmbResponsable.Items)
            {
                if (string.Equals(
                        item.Content?.ToString(),
                        responsable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CmbResponsable.SelectedItem = item;
                    return;
                }
            }

            CmbResponsable.SelectedIndex = 0;
        }

        private string ObtenerResponsableSeleccionado()
        {
            return
                (CmbResponsable.SelectedItem as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "SC";
        }

        // =====================================================
        // PERSONAS
        // =====================================================
        private void CargarPersonas()
        {
            var historicas =
                DatabaseHelper
                    .ObtenerPersonasHistoricasPorMes(
                        _congreCodigo,
                        _mesReferencia);

            var actuales =
                DatabaseHelper
                    .ObtenerPersonasPorCongregacion(
                        _congreCodigo);

            var actualesPorId =
                actuales
                    .GroupBy(p => p.Id)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First());

            var opciones =
                new Dictionary<int, OpcionPersonaPrograma>();

            foreach (Persona historica in historicas)
            {
                int raizId =
                    historica.PersonaRaizId > 0
                        ? historica.PersonaRaizId
                        : historica.Id;

                Persona personaMostrar = historica;

                if (actualesPorId.TryGetValue(
                        raizId,
                        out Persona actual))
                {
                    personaMostrar = actual;
                }
                else if (raizId > 0)
                {
                    Persona raizActual =
                        DatabaseHelper.ObtenerPersonaPorId(
                            raizId);

                    if (raizActual != null)
                    {
                        personaMostrar = raizActual;
                    }
                }

                string nombre =
                    ObtenerNombrePreferido(
                        personaMostrar);

                if (string.IsNullOrWhiteSpace(nombre))
                {
                    continue;
                }

                opciones[raizId] =
                    new OpcionPersonaPrograma
                    {
                        Id = raizId,
                        Texto = nombre,
                        Persona = personaMostrar
                    };
            }

            // Si no hubiera instantánea para alguna ficha actual, también
            // permitimos seleccionarla como acompañante.
            foreach (Persona actual in actuales)
            {
                if (opciones.ContainsKey(actual.Id))
                {
                    continue;
                }

                string nombre =
                    ObtenerNombrePreferido(actual);

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    opciones[actual.Id] =
                        new OpcionPersonaPrograma
                        {
                            Id = actual.Id,
                            Texto = nombre,
                            Persona = actual
                        };
                }
            }

            _personas =
                opciones
                    .Values
                    .OrderBy(
                        p => p.Texto,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            CmbAcompanante.ItemsSource =
                _personas;
        }

        private static string ObtenerNombrePreferido(
            Persona persona)
        {
            if (persona == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(
                    persona.NombreComun))
            {
                return persona.NombreComun.Trim();
            }

            if (!string.IsNullOrWhiteSpace(
                    persona.NombreJWHub))
            {
                return persona.NombreJWHub.Trim();
            }

            return persona.Nombre?.Trim() ?? "";
        }

        private int ResolverIdRaizPersona(
            int personaId)
        {
            if (personaId <= 0)
            {
                return 0;
            }

            Persona persona =
                DatabaseHelper.ObtenerPersonaPorId(
                    personaId);

            return
                persona != null
                && persona.PersonaRaizId > 0
                    ? persona.PersonaRaizId
                    : personaId;
        }

        private OpcionPersonaPrograma ObtenerOpcionPersonaSeleccionada()
        {
            return
                CmbAcompanante.SelectedItem
                as OpcionPersonaPrograma;
        }

        private Persona ObtenerPersonaSeleccionada()
        {
            return
                ObtenerOpcionPersonaSeleccionada()
                ?.Persona;
        }

        private void CmbAcompanante_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_cargandoInicial)
            {
                return;
            }

            AplicarDireccionPersonaSeleccionada();
        }

        private void BtnUsarDireccionAcompanante_Click(
            object sender,
            RoutedEventArgs e)
        {
            AplicarDireccionPersonaSeleccionada();
        }

        private void AplicarDireccionPersonaSeleccionada()
        {
            Persona persona =
                ObtenerPersonaSeleccionada();

            if (persona == null)
            {
                return;
            }

            _cargandoDireccion = true;

            TxtDireccion.Text =
                persona.Direccion ?? "";

            _cargandoDireccion = false;

            _latitud = persona.Latitud;
            _longitud = persona.Longitud;

            ActualizarTextoCoordenadas();
        }

        // =====================================================
        // SALÓN DEL REINO
        // =====================================================
        private void BtnUsarDireccionSR_Click(
            object sender,
            RoutedEventArgs e)
        {
            var congregacion =
                DatabaseHelper
                    .ObtenerCongregaciones()
                    .FirstOrDefault(
                        c => c.Codigo == _congreCodigo);

            if (congregacion == null)
            {
                MessageBox.Show(
                    "No se ha encontrado la congregación en la base de datos.",
                    "Salón del Reino",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool hayDireccion =
                !string.IsNullOrWhiteSpace(
                    congregacion.DireccionSR);

            bool hayCoordenadas =
                CoordenadasValidas(
                    congregacion.Latitud,
                    congregacion.Longitud);

            if (!hayDireccion && !hayCoordenadas)
            {
                MessageBox.Show(
                    "Esta congregación no tiene guardada la dirección ni las coordenadas del Salón del Reino.",
                    "Salón del Reino",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _cargandoDireccion = true;

            TxtDireccion.Text =
                congregacion.DireccionSR ?? "";

            _cargandoDireccion = false;

            _latitud = congregacion.Latitud;
            _longitud = congregacion.Longitud;

            ActualizarTextoCoordenadas();
        }

        // =====================================================
        // DIRECCIÓN Y COORDENADAS
        // =====================================================
        private void TxtDireccion_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_cargandoDireccion
                ||
                _cargandoInicial)
            {
                return;
            }

            // Al escribir manualmente una dirección distinta, las coordenadas
            // anteriores dejan de considerarse válidas.
            _latitud = 0;
            _longitud = 0;

            ActualizarTextoCoordenadas();
        }

        private async void BtnBuscarCoordenadas_Click(
            object sender,
            RoutedEventArgs e)
        {
            await BuscarCoordenadasAsync(
                mostrarMensajes: true);
        }

        private async Task<bool> BuscarCoordenadasAsync(
            bool mostrarMensajes)
        {
            string direccion =
                TxtDireccion.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(direccion))
            {
                if (mostrarMensajes)
                {
                    MessageBox.Show(
                        "Escribe una dirección primero.",
                        "Dirección",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return false;
            }

            BtnBuscarCoordenadas.IsEnabled = false;
            BtnBuscarCoordenadas.Content = "Buscando...";

            try
            {
                using var client = new HttpClient();

                client.DefaultRequestHeaders
                    .UserAgent
                    .ParseAdd(
                        "SCCosmos/1.0 (Windows; visita)");

                string url =
                    "https://nominatim.openstreetmap.org/search"
                    + "?format=json"
                    + "&limit=1"
                    + "&countrycodes=es"
                    + "&q="
                    + Uri.EscapeDataString(direccion);

                string json =
                    await client.GetStringAsync(url);

                var opciones =
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                List<NominatimResultado> resultados =
                    JsonSerializer.Deserialize<
                        List<NominatimResultado>>(
                            json,
                            opciones)
                    ?? new List<NominatimResultado>();

                NominatimResultado resultado =
                    resultados.FirstOrDefault();

                if (resultado == null)
                {
                    if (mostrarMensajes)
                    {
                        MessageBox.Show(
                            "No se han encontrado coordenadas para esa dirección.",
                            "Dirección",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    return false;
                }

                if (!double.TryParse(
                        resultado.lat,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double latitud)
                    ||
                    !double.TryParse(
                        resultado.lon,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double longitud))
                {
                    return false;
                }

                _latitud = latitud;
                _longitud = longitud;

                ActualizarTextoCoordenadas();

                if (mostrarMensajes)
                {
                    MessageBox.Show(
                        "Coordenadas encontradas.",
                        "Dirección",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (mostrarMensajes)
                {
                    MessageBox.Show(
                        "No se pudieron obtener las coordenadas.\n\n"
                        + ex.Message,
                        "Dirección",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }
            finally
            {
                BtnBuscarCoordenadas.IsEnabled = true;
                BtnBuscarCoordenadas.Content =
                    "Buscar coordenadas";
            }
        }

        private void ActualizarTextoCoordenadas()
        {
            bool validas =
                CoordenadasValidas(
                    _latitud,
                    _longitud);

            TxtCoordenadas.Text =
                validas
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Coordenadas: {0:0.000000}, {1:0.000000}",
                        _latitud,
                        _longitud)
                    : "Coordenadas: sin fijar";
        }

        private static bool CoordenadasValidas(
            double latitud,
            double longitud)
        {
            return
                latitud >= -90
                &&
                latitud <= 90
                &&
                longitud >= -180
                &&
                longitud <= 180
                &&
                (
                    Math.Abs(latitud) > 0.000001
                    ||
                    Math.Abs(longitud) > 0.000001
                );
        }

        // =====================================================
        // GUARDAR
        // =====================================================
        private async void BtnGuardar_Click(
            object sender,
            RoutedEventArgs e)
        {
            string hora =
                TxtHora.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(hora))
            {
                MessageBox.Show(
                    "Debes indicar una hora.",
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            OpcionPersonaPrograma opcionAcompanante =
                ObtenerOpcionPersonaSeleccionada();

            Persona acompanante =
                opcionAcompanante?.Persona;

            if (acompanante == null)
            {
                MessageBox.Show(
                    "Debes seleccionar un acompañante o familia.",
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string responsable =
                ObtenerResponsableSeleccionado();

            string direccion =
                TxtDireccion.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(direccion)
                &&
                !CoordenadasValidas(
                    _latitud,
                    _longitud))
            {
                bool localizadas =
                    await BuscarCoordenadasAsync(
                        mostrarMensajes: false);

                if (!localizadas)
                {
                    MessageBoxResult continuar =
                        MessageBox.Show(
                            "No se pudieron obtener coordenadas para la dirección indicada.\n\n"
                            + "¿Quieres guardar la actividad con la dirección, pero sin coordenadas?",
                            "Dirección sin coordenadas",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                    if (continuar !=
                        MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
            }

            BtnGuardar.IsEnabled = false;

            try
            {
                if (_actividadEditarId > 0)
                {
                    DatabaseHelper
                        .ActualizarActividadPrograma(
                            _actividadEditarId,
                            responsable,
                            TxtTipoActividad.Text?.Trim() ?? "",
                            hora,
                            TxtNotas.Text?.Trim() ?? "",
                            opcionAcompanante.Id,
                            direccion,
                            _latitud,
                            _longitud);
                }
                else
                {
                    DatabaseHelper
                        .GuardarActividadPrograma(
                            _congreCodigo,
                            _mesReferencia,
                            _dia,
                            "General",
                            responsable,
                            TxtTipoActividad.Text?.Trim() ?? "",
                            hora,
                            TxtNotas.Text?.Trim() ?? "",
                            opcionAcompanante.Id,
                            direccion,
                            _latitud,
                            _longitud);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo guardar la actividad.\n\n"
                    + ex.Message,
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                BtnGuardar.IsEnabled = true;
            }
        }

        private void BtnCancelar_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private sealed class OpcionPersonaPrograma
        {
            public int Id { get; set; }
            public string Texto { get; set; }
            public Persona Persona { get; set; }
        }

        private sealed class NominatimResultado
        {
            public string lat { get; set; }
            public string lon { get; set; }
            public string display_name { get; set; }
        }
    }
}
