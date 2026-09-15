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
    public partial class ProgramarActividadWindow
        : Window
    {
        private readonly string
            _responsable;

        private readonly string
            _turno;

        private readonly string
            _dia;

        private readonly string
            _congreCodigo;

        private readonly string
            _mesReferencia;

        private readonly int
            _actividadEditarId;


        private double
            _latitud;

        private double
            _longitud;


        private bool
            _cargandoInicial;

        private bool
            _cargandoDireccion;


        private List<Persona>
            _personas =
                new List<Persona>();


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
            DatabaseHelper.ActividadPrograma
                actividadEditar)
        {
            InitializeComponent();


            _responsable =
                responsable
                ??
                "";

            _turno =
                turno
                ??
                "";

            _dia =
                dia
                ??
                "";

            _congreCodigo =
                congreCodigo
                ??
                "";

            _mesReferencia =
                mesReferencia
                ??
                "";


            _actividadEditarId =
                actividadEditar
                    ?.Id
                ??
                0;


            _cargandoInicial =
                true;


            TxtContexto.Text =
                $"{_dia} · {_turno} · {_responsable}";


            CargarPersonas();


            if (actividadEditar is not null)
            {
                TxtTitulo.Text =
                    "EDITAR ACTIVIDAD";

                TxtHora.Text =
                    actividadEditar.Hora
                    ??
                    "";

                TxtTipoActividad.Text =
                    actividadEditar
                        .TipoActividad
                    ??
                    "";

                TxtNotas.Text =
                    actividadEditar
                        .NotasLibro
                    ??
                    "";

                CmbAcompanante.SelectedValue =
                    actividadEditar
                        .CompaneroId;


                _latitud =
                    actividadEditar
                        .Latitud;

                _longitud =
                    actividadEditar
                        .Longitud;


                _cargandoDireccion =
                    true;

                TxtDireccion.Text =
                    actividadEditar
                        .Direccion
                    ??
                    "";

                _cargandoDireccion =
                    false;
            }
            else
            {
                TxtTitulo.Text =
                    "PROGRAMAR ACTIVIDAD";
            }


            _cargandoInicial =
                false;

            ActualizarTextoCoordenadas();
        }


        // =====================================================
        // PERSONAS
        // =====================================================

        private void CargarPersonas()
        {
            _personas =
                DatabaseHelper
                    .ObtenerPersonasHistoricasPorMes(
                        _congreCodigo,
                        _mesReferencia)
                    .OrderBy(
                        p =>
                            p.Nombre)
                    .ToList();


            CmbAcompanante.ItemsSource =
                _personas;
        }


        private Persona ObtenerPersonaSeleccionada()
        {
            return
                CmbAcompanante
                    .SelectedItem
                as Persona;
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


            if (persona is null)
            {
                return;
            }


            _cargandoDireccion =
                true;

            TxtDireccion.Text =
                persona.Direccion
                ??
                "";

            _cargandoDireccion =
                false;


            _latitud =
                persona.Latitud;

            _longitud =
                persona.Longitud;


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


            // Si el usuario escribe una dirección distinta,
            // las coordenadas anteriores ya no se consideran válidas.
            _latitud =
                0;

            _longitud =
                0;


            ActualizarTextoCoordenadas();
        }


        private async void BtnBuscarCoordenadas_Click(
            object sender,
            RoutedEventArgs e)
        {
            await BuscarCoordenadasAsync(
                mostrarMensajes:
                    true);
        }


        private async Task<bool>
            BuscarCoordenadasAsync(
                bool mostrarMensajes)
        {
            string direccion =
                TxtDireccion.Text
                    ?.Trim()
                ??
                "";


            if (string.IsNullOrWhiteSpace(
                    direccion))
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


            BtnBuscarCoordenadas.IsEnabled =
                false;

            BtnBuscarCoordenadas.Content =
                "Buscando...";


            try
            {
                using var client =
                    new HttpClient();

                client.DefaultRequestHeaders
                    .UserAgent
                    .ParseAdd(
                        "SCCosmos/1.0 " +
                        "(Windows; visita)");


                string url =
                    "https://nominatim.openstreetmap.org/search"
                    +
                    "?format=json"
                    +
                    "&limit=1"
                    +
                    "&countrycodes=es"
                    +
                    "&q="
                    +
                    Uri.EscapeDataString(
                        direccion);


                string json =
                    await client
                        .GetStringAsync(
                            url);


                var opciones =
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    };


                List<NominatimResultado> resultados =
                    JsonSerializer
                        .Deserialize<
                            List<NominatimResultado>>(
                                json,
                                opciones)
                    ??
                    new List<NominatimResultado>();


                NominatimResultado resultado =
                    resultados
                        .FirstOrDefault();


                if (resultado is null)
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


                _latitud =
                    latitud;

                _longitud =
                    longitud;


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
                        +
                        ex.Message,
                        "Dirección",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }
            finally
            {
                BtnBuscarCoordenadas.IsEnabled =
                    true;

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
                    Math.Abs(latitud) >
                    0.000001
                    ||
                    Math.Abs(longitud) >
                    0.000001
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
                TxtHora.Text
                    ?.Trim()
                ??
                "";


            if (string.IsNullOrWhiteSpace(
                    hora))
            {
                MessageBox.Show(
                    "Debes indicar una hora.",
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            Persona acompanante =
                ObtenerPersonaSeleccionada();


            if (acompanante is null)
            {
                MessageBox.Show(
                    "Debes seleccionar un acompañante o familia.",
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            string direccion =
                TxtDireccion.Text
                    ?.Trim()
                ??
                "";


            if (!string.IsNullOrWhiteSpace(
                    direccion)
                &&
                !CoordenadasValidas(
                    _latitud,
                    _longitud))
            {
                bool localizadas =
                    await BuscarCoordenadasAsync(
                        mostrarMensajes:
                            false);


                if (!localizadas)
                {
                    MessageBoxResult continuar =
                        MessageBox.Show(
                            "No se pudieron obtener coordenadas para la dirección indicada.\n\n"
                            +
                            "¿Quieres guardar la actividad con la dirección, pero sin coordenadas?",
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


            BtnGuardar.IsEnabled =
                false;


            try
            {
                if (_actividadEditarId >
                    0)
                {
                    DatabaseHelper
                        .ActualizarActividadPrograma(
                            _actividadEditarId,
                            TxtTipoActividad.Text
                                ?.Trim()
                            ??
                            "",
                            hora,
                            TxtNotas.Text
                                ?.Trim()
                            ??
                            "",
                            acompanante.Id,
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
                            _turno,
                            _responsable,
                            TxtTipoActividad.Text
                                ?.Trim()
                            ??
                            "",
                            hora,
                            TxtNotas.Text
                                ?.Trim()
                            ??
                            "",
                            acompanante.Id,
                            direccion,
                            _latitud,
                            _longitud);
                }


                DialogResult =
                    true;

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo guardar la actividad.\n\n"
                    +
                    ex.Message,
                    "Programa de la visita",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                BtnGuardar.IsEnabled =
                    true;
            }
        }


        private void BtnCancelar_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;

            Close();
        }


        private sealed class
            NominatimResultado
        {
            public string lat
            { get; set; }

            public string lon
            { get; set; }

            public string display_name
            { get; set; }
        }
    }
}
