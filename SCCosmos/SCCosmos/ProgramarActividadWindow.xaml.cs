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

        private static readonly string[] DiasPrograma =
        {
            "Miércoles",
            "Jueves",
            "Viernes",
            "Sábado",
            "Domingo"
        };

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

            // El acompañante es opcional. Añadimos una opción explícita
            // para poder dejar el evento sin acompañante o quitar uno existente.
            _personas.Insert(
                0,
                new OpcionPersonaPrograma
                {
                    Id = 0,
                    Texto = "— Sin acompañante —",
                    Persona = null
                });

            CmbAcompanante.ItemsSource =
                _personas;

            CmbAcompanante.SelectedIndex = 0;
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

        private void BtnCoordenadasManuales_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool mostrar =
                PanelCoordenadasManuales.Visibility != Visibility.Visible;

            PanelCoordenadasManuales.Visibility =
                mostrar
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (mostrar)
            {
                TxtLatitudManual.Text =
                    CoordenadasValidas(_latitud, _longitud)
                        ? _latitud.ToString("0.######", CultureInfo.InvariantCulture)
                        : "";

                TxtLongitudManual.Text =
                    CoordenadasValidas(_latitud, _longitud)
                        ? _longitud.ToString("0.######", CultureInfo.InvariantCulture)
                        : "";

                TxtLatitudManual.Focus();
                TxtLatitudManual.SelectAll();
            }
        }

        private void BtnAplicarCoordenadasManuales_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryParseCoordenada(
                    TxtLatitudManual.Text,
                    out double latitud)
                ||
                !TryParseCoordenada(
                    TxtLongitudManual.Text,
                    out double longitud)
                ||
                !CoordenadasValidas(latitud, longitud))
            {
                MessageBox.Show(
                    "Introduce unas coordenadas válidas.\n\n"
                    + "Latitud: entre -90 y 90.\n"
                    + "Longitud: entre -180 y 180.",
                    "Coordenadas manuales",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _latitud = latitud;
            _longitud = longitud;
            ActualizarTextoCoordenadas();
            PanelCoordenadasManuales.Visibility = Visibility.Collapsed;
        }

        private static bool TryParseCoordenada(
            string texto,
            out double valor)
        {
            valor = 0;

            if (string.IsNullOrWhiteSpace(texto))
            {
                return false;
            }

            string normalizado =
                texto.Trim().Replace(',', '.');

            return double.TryParse(
                normalizado,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out valor);
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
            await GuardarActividadAsync(
                replicarTodosLosDias: false);
        }

        private async void BtnReplicarTodosDias_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult confirmar =
                MessageBox.Show(
                    "Se guardará este mismo evento de miércoles a domingo.\n\n"
                    + "Si ya existe una copia idéntica en alguno de esos días, no se duplicará.\n\n"
                    + "¿Quieres continuar?",
                    "Replicar todos los días",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirmar != MessageBoxResult.Yes)
            {
                return;
            }

            await GuardarActividadAsync(
                replicarTodosLosDias: true);
        }

        private async Task GuardarActividadAsync(
            bool replicarTodosLosDias)
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

            // El acompañante es opcional. Id 0 representa "sin acompañante".
            int companeroId =
                opcionAcompanante?.Id ?? 0;

            string responsable =
                ObtenerResponsableSeleccionado();

            string tipoActividad =
                TxtTipoActividad.Text?.Trim() ?? "";

            string notas =
                TxtNotas.Text?.Trim() ?? "";

            string direccion =
                TxtDireccion.Text?.Trim() ?? "";

            // Si el usuario ya fijó coordenadas manualmente, se respetan aunque
            // la dirección completa incluya piso, puerta u otros detalles.
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
                            + "Puedes fijarlas manualmente con el botón 'Fijar manualmente'.\n\n"
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
            BtnReplicarTodosDias.IsEnabled = false;

            try
            {
                if (!replicarTodosLosDias)
                {
                    if (_actividadEditarId > 0)
                    {
                        DatabaseHelper
                            .ActualizarActividadPrograma(
                                _actividadEditarId,
                                responsable,
                                tipoActividad,
                                hora,
                                notas,
                                companeroId,
                                direccion,
                                _latitud,
                                _longitud);
                    }
                    else
                    {
                        GuardarCopiaActividad(
                            _dia,
                            responsable,
                            tipoActividad,
                            hora,
                            notas,
                            companeroId,
                            direccion);
                    }
                }
                else
                {
                    // Si estamos editando, conservamos el Id original y
                    // actualizamos primero el evento del día actual.
                    if (_actividadEditarId > 0)
                    {
                        DatabaseHelper
                            .ActualizarActividadPrograma(
                                _actividadEditarId,
                                responsable,
                                tipoActividad,
                                hora,
                                notas,
                                companeroId,
                                direccion,
                                _latitud,
                                _longitud);
                    }

                    var existentes =
                        DatabaseHelper.ObtenerActividadesProgramaMes(
                            _congreCodigo,
                            _mesReferencia);

                    foreach (string dia in DiasPrograma)
                    {
                        if (_actividadEditarId > 0
                            &&
                            string.Equals(
                                dia,
                                _dia,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        bool yaExiste =
                            existentes.Any(a =>
                                EsMismaActividadReplicada(
                                    a,
                                    dia,
                                    responsable,
                                    tipoActividad,
                                    hora,
                                    notas,
                                    companeroId,
                                    direccion,
                                    _latitud,
                                    _longitud));

                        if (!yaExiste)
                        {
                            GuardarCopiaActividad(
                                dia,
                                responsable,
                                tipoActividad,
                                hora,
                                notas,
                                companeroId,
                                direccion);
                        }
                    }
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
                BtnReplicarTodosDias.IsEnabled = true;
            }
        }

        private void GuardarCopiaActividad(
            string dia,
            string responsable,
            string tipoActividad,
            string hora,
            string notas,
            int companeroId,
            string direccion)
        {
            DatabaseHelper
                .GuardarActividadPrograma(
                    _congreCodigo,
                    _mesReferencia,
                    dia,
                    "General",
                    responsable,
                    tipoActividad,
                    hora,
                    notas,
                    companeroId,
                    direccion,
                    _latitud,
                    _longitud);
        }

        private static bool EsMismaActividadReplicada(
            DatabaseHelper.ActividadPrograma actividad,
            string dia,
            string responsable,
            string tipoActividad,
            string hora,
            string notas,
            int companeroId,
            string direccion,
            double latitud,
            double longitud)
        {
            if (actividad == null)
            {
                return false;
            }

            return
                string.Equals(
                    actividad.DiaSemana?.Trim(),
                    dia?.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    NormalizarResponsable(actividad.Responsable),
                    responsable,
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    actividad.TipoActividad?.Trim() ?? "",
                    tipoActividad ?? "",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    actividad.Hora?.Trim() ?? "",
                    hora ?? "",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    actividad.NotasLibro?.Trim() ?? "",
                    notas ?? "",
                    StringComparison.Ordinal)
                &&
                actividad.CompaneroId == companeroId
                &&
                string.Equals(
                    actividad.Direccion?.Trim() ?? "",
                    direccion ?? "",
                    StringComparison.OrdinalIgnoreCase)
                &&
                Math.Abs(actividad.Latitud - latitud) < 0.000001
                &&
                Math.Abs(actividad.Longitud - longitud) < 0.000001;
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
