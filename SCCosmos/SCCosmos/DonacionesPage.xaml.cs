using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SCCosmos
{
    /// <summary>
    /// Donaciones recibidas: registro por persona (con la congregación
    /// rellenada automáticamente), fecha, cantidad y medio de pago
    /// (efectivo o bizum/transferencia). Permite donantes desconocidos y
    /// búsquedas por año, mes, congregación, persona y medio, con el total
    /// calculado de lo que se está viendo.
    /// </summary>
    public partial class DonacionesPage : Page, IActualizablePorSincro
    {
        private sealed class OpcionPersona
        {
            public int Id { get; set; }

            public string Nombre { get; set; } = "";

            public string Visual { get; set; } = "";

            public string CongregacionCodigo { get; set; } = "";

            public string CongregacionNombre { get; set; } = "";

            public override string ToString() => Visual;
        }


        private sealed class OpcionCongregacion
        {
            public string Codigo { get; set; } = "";

            public string Visual { get; set; } = "";

            public override string ToString() => Visual;
        }


        private readonly List<OpcionPersona> _personas =
            new();

        private List<OpcionPersona> _personasFiltradas =
            new();

        private OpcionPersona? _personaSeleccionada;

        private readonly List<OpcionCongregacion> _congregaciones =
            new();

        private List<DatabaseHelper.DonacionBD> _donaciones =
            new();

        private DatabaseHelper.DonacionBD? _enEdicion;

        private bool _cargando = true;


        public DonacionesPage()
        {
            InitializeComponent();
            this.Loaded += DonacionesPage_Loaded;
        }

        private async void DonacionesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Dejar que la página se pinte antes de cargar los datos.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            CargarDatosBase();

            _cargando = false;

            Recargar();
        }


        // ------------------------------------------------------------------
        // CARGA INICIAL Y FILTROS
        // ------------------------------------------------------------------

        public void DatosActualizados()
        {
            try
            {
                CargarDatosBase();
            }
            catch
            {
            }
        }


        private void CargarDatosBase()
        {
            DatabaseHelper.AsegurarTablaDonaciones();

            _personas.Add(
                new OpcionPersona
                {
                    Id = 0,
                    Nombre = "",
                    Visual = "(Desconocido)"
                });

            foreach (
                Persona persona
                in DatabaseHelper
                    .ObtenerPersonas()
                    .Where(p => p != null)
                    .OrderBy(p => p.NombreMostrarTabla))
            {
                _personas.Add(
                    new OpcionPersona
                    {
                        Id = persona.Id,
                        Nombre = persona.NombreMostrarTabla,
                        Visual =
                            string.IsNullOrWhiteSpace(
                                persona.CongregacionNombre)
                                ? persona.NombreMostrarTabla
                                : $"{persona.NombreMostrarTabla} " +
                                  $"({persona.CongregacionNombre})",
                        CongregacionCodigo =
                            persona.CongregacionCodigo ?? "",
                        CongregacionNombre =
                            persona.CongregacionNombre ?? ""
                    });
            }

            _congregaciones.Add(
                new OpcionCongregacion
                {
                    Codigo = "",
                    Visual = "(Sin congregación)"
                });

            foreach (
                Congregacion congre
                in DatabaseHelper
                    .ObtenerCongregaciones()
                    .OrderBy(c => c.Nombre))
            {
                _congregaciones.Add(
                    new OpcionCongregacion
                    {
                        Codigo = congre.Codigo ?? "",
                        Visual = congre.Nombre ?? ""
                    });
            }

            // ---- Filtros ----
            CmbAnio.ItemsSource =
                CrearListaAnios();

            CmbAnio.SelectedIndex = 0;

            var meses =
                new List<string> { "(Todos los meses)" };

            for (int mes = 1;
                 mes <= 12;
                 mes++)
            {
                meses.Add(
                    new DateTime(2000, mes, 1)
                        .ToString(
                            "MMMM",
                            CultureInfo.GetCultureInfo("es-ES")));
            }

            CmbMes.ItemsSource = meses;
            CmbMes.SelectedIndex = 0;

            CmbFiltroCongregacion.ItemsSource =
                _congregaciones;

            CmbFiltroCongregacion.SelectedIndex = 0;

            CmbFiltroMedio.ItemsSource =
                new List<string>
                {
                    "(Todos los medios)",
                    DatabaseHelper.MedioEfectivo,
                    DatabaseHelper.MedioBizumTransferencia
                };

            CmbFiltroMedio.SelectedIndex = 0;

            // ---- Campos del editor ----
            CmbMedio.ItemsSource =
                new List<string>
                {
                    DatabaseHelper.MedioEfectivo,
                    DatabaseHelper.MedioBizumTransferencia
                };

            CmbMedio.SelectedIndex = 0;

            AplicarFiltroPersonasEditor();

            CmbPersona.SelectedIndex = 0;

            _personaSeleccionada =
                CmbPersona.SelectedItem as OpcionPersona;

            CmbCongregacion.ItemsSource = _congregaciones;
            CmbCongregacion.SelectedIndex = 0;
        }


        private List<string> CrearListaAnios()
        {
            var anios =
                new List<string> { "(Todos los años)" };

            var añosConDatos =
                DatabaseHelper
                    .ObtenerDonaciones()
                    .Select(d =>
                        (d.Fecha ?? "").Length >= 4
                            ? d.Fecha.Substring(0, 4)
                            : "")
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct()
                    .OrderByDescending(a => a)
                    .ToList();

            string anioActual =
                DateTime.Now.Year.ToString();

            if (!añosConDatos.Contains(anioActual))
            {
                añosConDatos.Insert(0, anioActual);
            }

            anios.AddRange(añosConDatos);

            return anios;
        }


        private void Recargar()
        {
            string anio =
                CmbAnio.SelectedIndex > 0
                    ? CmbAnio.SelectedItem as string ?? ""
                    : "";

            string mes =
                CmbMes.SelectedIndex > 0
                    ? CmbMes.SelectedIndex.ToString("00")
                    : "";

            string congregacion =
                (CmbFiltroCongregacion.SelectedItem
                    as OpcionCongregacion)
                ?.Codigo ?? "";

            string persona =
                TxtBuscarPersona.Text ?? "";

            string medio =
                CmbFiltroMedio.SelectedIndex > 0
                    ? CmbFiltroMedio.SelectedItem as string ?? ""
                    : "";

            _donaciones =
                DatabaseHelper.ObtenerDonaciones(
                    anio,
                    mes,
                    congregacion,
                    persona,
                    medio);

            var cultura =
                CultureInfo.GetCultureInfo("es-ES");

            // ── Agrupado por semanas con el total al final de cada una ──
            var filasSemana =
                new List<DatabaseHelper.DonacionBD>();

            // El total de cada semana va ARRIBA de sus registros.
            foreach (var semana in _donaciones
                .OrderBy(d => DateTime.TryParse(d.Fecha, out DateTime ff)
                    ? ff
                    : DateTime.MaxValue)
                .GroupBy(d =>
                {
                    DateTime fecha =
                        DateTime.TryParse(d.Fecha, out DateTime f)
                            ? f.Date
                            : DateTime.Today;

                    return fecha.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
                }))
            {
                filasSemana.Add(
                    CrearSeparadorSemana(
                        semana.Key,
                        semana.Sum(d => d.Cantidad),
                        cultura));

                filasSemana.AddRange(semana);
            }

            TablaDonaciones.ItemsSource =
                filasSemana;

            double total =
                DatabaseHelper.CalcularTotalDonaciones(
                    _donaciones);

            double efectivo =
                _donaciones
                    .Where(d => d.MedioPago ==
                                DatabaseHelper.MedioEfectivo)
                    .Sum(d => d.Cantidad);

            double bizum =
                total - efectivo;

            TxtTotal.Text =
                $"Total: {total.ToString("N2", cultura)} €";

            TxtResumen.Text =
                $"{_donaciones.Count} registros · " +
                $"efectivo: {efectivo.ToString("N2", cultura)} € · " +
                $"bizum/transferencia: {bizum.ToString("N2", cultura)} €";
        }


        /// <summary>Fila separadora con el total de la semana.</summary>
        private static DatabaseHelper.DonacionBD CrearSeparadorSemana(
            DateTime lunes,
            double total,
            CultureInfo cultura)
        {
            DateTime domingo = lunes.AddDays(6);

            return new DatabaseHelper.DonacionBD
            {
                EsSeparador = true,
                TextoSeparador =
                    "TOTAL SEMANA "
                    + lunes.ToString("dd/MM", cultura)
                    + " – "
                    + domingo.ToString("dd/MM", cultura),
                Cantidad = total
            };
        }


        private void Filtros_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!_cargando)
            {
                Recargar();
            }
        }


        private void TxtBuscarPersona_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (!_cargando)
            {
                Recargar();
            }
        }


        private void BtnLimpiarFiltros_Click(
            object sender,
            RoutedEventArgs e)
        {
            _cargando = true;

            CmbAnio.SelectedIndex = 0;
            CmbMes.SelectedIndex = 0;
            CmbFiltroCongregacion.SelectedIndex = 0;
            CmbFiltroMedio.SelectedIndex = 0;
            TxtBuscarPersona.Text = "";

            _cargando = false;

            Recargar();
        }


        // ------------------------------------------------------------------
        // ALTA Y EDICIÓN
        // ------------------------------------------------------------------

        private void BtnNueva_Click(
            object sender,
            RoutedEventArgs e)
        {
            _enEdicion = null;

            FechaDonacion.SelectedDate =
                DateTime.Now;

            TxtCantidad.Text = "";
            TxtNotas.Text = "";
            TxtBuscarPersonaEditor.Text = "";
            CmbPersona.SelectedIndex = 0;
            _personaSeleccionada =
                CmbPersona.SelectedItem as OpcionPersona;
            CmbCongregacion.SelectedIndex = 0;
            CmbMedio.SelectedIndex = 0;

            PanelEditor.Visibility =
                Visibility.Visible;

            TxtCantidad.Focus();
        }


        private void BtnEditar_Click(
            object sender,
            RoutedEventArgs e)
        {
            AbrirSeleccionada();
        }


        private void TablaDonaciones_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            AbrirSeleccionada();
        }


        private void AbrirSeleccionada()
        {
            if (TablaDonaciones.SelectedItem
                is not DatabaseHelper.DonacionBD donacion
                || donacion.EsSeparador)
            {
                MessageBox.Show(
                    "Selecciona primero una donación en la tabla.",
                    "Rendimientos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _enEdicion =
                donacion;

            FechaDonacion.SelectedDate =
                DateTime.TryParse(
                    donacion.Fecha,
                    out DateTime fecha)
                    ? fecha
                    : DateTime.Now;

            TxtCantidad.Text =
                donacion.Cantidad.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                    .Replace('.', ',');

            TxtNotas.Text =
                donacion.Notas ?? "";

            TxtBuscarPersonaEditor.Text = "";

            _personaSeleccionada =
                _personas.FirstOrDefault(
                    p => p.Id == donacion.PersonaId)
                ?? _personas[0];

            CmbPersona.SelectedItem =
                _personaSeleccionada;

            CmbCongregacion.SelectedItem =
                _congregaciones.FirstOrDefault(
                    c => c.Codigo == donacion.CongregacionCodigo)
                ?? _congregaciones[0];

            CmbMedio.SelectedItem =
                CmbMedio.Items
                    .Cast<string>()
                    .FirstOrDefault(m => m == donacion.MedioPago)
                ?? DatabaseHelper.MedioEfectivo;

            PanelEditor.Visibility =
                Visibility.Visible;
        }


        private void TxtBuscarPersonaEditor_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            AplicarFiltroPersonasEditor();
        }


        /// <summary>
        /// Filtra la lista de personas del editor con el texto del buscador.
        /// "(Desconocido)" sigue siempre disponible.
        /// </summary>
        private void AplicarFiltroPersonasEditor()
        {
            string texto =
                NormalizarBusqueda(
                    TxtBuscarPersonaEditor.Text);

            _personasFiltradas =
                string.IsNullOrWhiteSpace(texto)
                    ? new List<OpcionPersona>(_personas)
                    : _personas
                        .Where(
                            p => p.Id <= 0
                                 || NormalizarBusqueda(p.Visual)
                                     .Contains(texto))
                        .ToList();

            CmbPersona.ItemsSource =
                _personasFiltradas;

            if (_personaSeleccionada != null
                && _personasFiltradas.Contains(
                    _personaSeleccionada))
            {
                CmbPersona.SelectedItem =
                    _personaSeleccionada;
            }
        }


        /// <summary>
        /// Normaliza texto para buscar: sin tildes y en minúsculas.
        /// </summary>
        private static string NormalizarBusqueda(
            string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return "";
            }

            string descompuesto =
                texto.Normalize(
                    NormalizationForm.FormD);

            var limpias =
                new StringBuilder();

            foreach (char caracter in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                    != UnicodeCategory.NonSpacingMark)
                {
                    limpias.Append(caracter);
                }
            }

            return limpias
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .ToLowerInvariant();
        }


        private void CmbPersona_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (CmbPersona.SelectedItem
                is OpcionPersona seleccion)
            {
                _personaSeleccionada =
                    seleccion;
            }

            // La congregación se rellena automáticamente con la de la persona.
            if (PanelEditor.Visibility != Visibility.Visible)
            {
                return;
            }

            if (CmbPersona.SelectedItem
                is not OpcionPersona persona)
            {
                return;
            }

            if (persona.Id <= 0)
            {
                // Donante desconocido: se puede elegir la congregación.
                return;
            }

            CmbCongregacion.SelectedItem =
                _congregaciones.FirstOrDefault(
                    c => c.Codigo == persona.CongregacionCodigo)
                ?? _congregaciones[0];
        }


        private void BtnCancelarEdicion_Click(
            object sender,
            RoutedEventArgs e)
        {
            PanelEditor.Visibility =
                Visibility.Collapsed;

            TxtBuscarPersonaEditor.Text = "";

            _enEdicion = null;
        }


        private void BtnGuardar_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!IntentarLeerCantidad(
                    TxtCantidad.Text,
                    out double cantidad))
            {
                MessageBox.Show(
                    "Escribe una cantidad válida, por ejemplo 25,50.",
                    "Rendimientos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TxtCantidad.Focus();

                return;
            }

            if (cantidad <= 0)
            {
                MessageBox.Show(
                    "La cantidad debe ser mayor que cero.",
                    "Rendimientos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var persona =
                _personaSeleccionada
                ?? CmbPersona.SelectedItem as OpcionPersona;

            var congregacion =
                CmbCongregacion.SelectedItem
                    as OpcionCongregacion;

            var donacion =
                _enEdicion
                ?? new DatabaseHelper.DonacionBD();

            donacion.Fecha =
                (FechaDonacion.SelectedDate ?? DateTime.Now)
                    .ToString("yyyy-MM-dd");

            donacion.PersonaId =
                persona?.Id ?? 0;

            donacion.NombrePersona =
                persona is null || persona.Id <= 0
                    ? DatabaseHelper.DonanteDesconocido
                    : persona.Nombre;

            donacion.CongregacionCodigo =
                congregacion?.Codigo ?? "";

            donacion.CongregacionNombre =
                string.IsNullOrWhiteSpace(
                    congregacion?.Codigo)
                    ? ""
                    : congregacion?.Visual ?? "";

            donacion.Cantidad = cantidad;

            donacion.MedioPago =
                CmbMedio.SelectedItem as string
                ?? DatabaseHelper.MedioEfectivo;

            donacion.Notas =
                (TxtNotas.Text ?? "").Trim();

            DatabaseHelper.GuardarDonacion(
                donacion);

            _enEdicion = null;

            PanelEditor.Visibility =
                Visibility.Collapsed;

            // Puede haber aparecido un año nuevo en el filtro.
            string anioSeleccionado =
                CmbAnio.SelectedItem as string
                ?? "(Todos los años)";

            _cargando = true;

            CmbAnio.ItemsSource =
                CrearListaAnios();

            CmbAnio.SelectedItem =
                anioSeleccionado;

            _cargando = false;

            Recargar();

            MessageBox.Show(
                "Donación guardada.",
                "Rendimientos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        private void BtnEliminar_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (TablaDonaciones.SelectedItem
                is not DatabaseHelper.DonacionBD donacion
                || donacion.EsSeparador)
            {
                MessageBox.Show(
                    "Selecciona primero una donación en la tabla.",
                    "Rendimientos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (MessageBox.Show(
                    $"¿Eliminar la donación de " +
                    $"{donacion.PersonaVisual} del " +
                    $"{donacion.FechaVisual} " +
                    $"({donacion.CantidadVisual})?",
                    "Eliminar donación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            DatabaseHelper.EliminarDonacion(
                donacion.Id);

            Recargar();
        }


        /// <summary>
        /// Lee la cantidad escrita admitiendo coma o punto como separador
        /// decimal y también separadores de millar (1.500,25 / 1,500.25).
        /// </summary>
        private static bool IntentarLeerCantidad(
            string texto,
            out double cantidad)
        {
            cantidad = 0;

            string limpio =
                (texto ?? "")
                    .Replace("€", "")
                    .Replace(" ", "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(limpio))
            {
                return false;
            }

            int ultimaComa =
                limpio.LastIndexOf(',');

            int ultimoPunto =
                limpio.LastIndexOf('.');

            string normalizado;

            if (ultimaComa >= 0
                &&
                ultimoPunto >= 0)
            {
                // El último separador es el decimal; el otro son millares.
                char decimalSeparador =
                    ultimaComa > ultimoPunto
                        ? ','
                        : '.';

                char milesSeparador =
                    decimalSeparador == ','
                        ? '.'
                        : ',';

                normalizado =
                    limpio
                        .Replace(
                            milesSeparador.ToString(),
                            "")
                        .Replace(
                            decimalSeparador,
                            '.');
            }
            else if (ultimaComa >= 0
                     ||
                     ultimoPunto >= 0)
            {
                char separador =
                    ultimaComa >= 0
                        ? ','
                        : '.';

                string[] partes =
                    limpio.Split(separador);

                if (partes.Length == 2
                    &&
                    partes[1].Length > 0
                    &&
                    partes[1].Length <= 2)
                {
                    // 30,50 o 30.50 -> decimal
                    normalizado =
                        limpio.Replace(
                            separador,
                            '.');
                }
                else
                {
                    // 1.500 -> millares
                    normalizado =
                        limpio.Replace(
                            separador.ToString(),
                            "");
                }
            }
            else
            {
                normalizado = limpio;
            }

            return double.TryParse(
                normalizado,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out cantidad);
        }
    }
}
