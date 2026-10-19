using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

using ITextAlign = iText.Layout.Properties.TextAlignment;
using ITextCell = iText.Layout.Element.Cell;

namespace SCCosmos
{
    public partial class EscuelasPage : Page, IActualizablePorSincro
    {
        private bool _viendoDescartados = false;

        // ===== Clases (escuelas por semana) =====
        private EscuelaClase _escuelaDetalle;
        private EscuelaClase _escuelaEditando;
        private bool _recargandoFormulario;
        private bool _ajustandoSemana;
        private DateTime _semanaSeleccionada = DateTime.Today;
        private List<ItemPersonaEscuela> _instructoresTodos = new List<ItemPersonaEscuela>();
        private List<ItemPersonaEscuela> _sustitutosTodos = new List<ItemPersonaEscuela>();
        private List<AlumnoPosibleEscuela> _posiblesAlumnos = new List<AlumnoPosibleEscuela>();
        private List<EscuelaAlumno> _alumnosAsientos = new List<EscuelaAlumno>();
        private AsientoEscuela _asientoElegido;
        private List<EscuelaAlumno> _alumnosLimpieza = new List<EscuelaAlumno>();
        private bool _recargandoLimpieza;

        public EscuelasPage()
        {
            InitializeComponent();

            CmbInstructor.Loaded += CmbInstructor_Loaded;
            CmbSustituto.Loaded += CmbSustituto_Loaded;

            this.Loaded += EscuelasPage_Loaded;
        }

        private async void EscuelasPage_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarEstudiantesAsync();
            await CargarClasesAsync();
        }

        public void DatosActualizados()
        {
            try
            {
                _ = CargarEstudiantesAsync();
                _ = CargarClasesAsync();

                if (OverlayDetalleEscuela.Visibility == Visibility.Visible)
                {
                    _ = RefrescarDetalleAsync();
                }
            }
            catch
            {
            }
        }


        private async Task CargarEstudiantesAsync()
        {
            try
            {
                // Dejar que la página se pinte antes de cargar los datos.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                // Obtenemos todas las personas de la BD
                var personasDB = await Task.Run(() => DatabaseHelper.ObtenerPersonas());

                DateTime hoy = DateTime.Today;

                // =========================================================================
                // 1. CÁLCULO DE FECHAS CLAVE SEGÚN TUS REQUISITOS
                // =========================================================================

                // Año de servicio en vigor actual (ej: si hoy es 01/09/2026, el año de servicio empezó el 01/09/2026)
                int anioInicioServicio = hoy.Month >= 9 ? hoy.Year : hoy.Year - 1;
                DateTime fechaLimiteInicioPrec = new DateTime(anioInicioServicio, 9, 1); // Ej: 01/09/2026

                // Límite de los 4 años para la última escuela
                DateTime fechaCorteUltimaEscuela = new DateTime(anioInicioServicio - 4, 8, 31);

                // =========================================================================
                // 2. RECUPERAR LOS DESCARTADOS DE ESTE AÑO DE SERVICIO
                // =========================================================================
                string keyDescartados = "DescartadosEscuelas_" + anioInicioServicio;
                string descartadosStr = DatabaseHelper.ObtenerConfiguracion(keyDescartados, "");
                var descartadosList = string.IsNullOrWhiteSpace(descartadosStr)
                    ? new List<int>()
                    : descartadosStr.Split(',').Select(s => int.TryParse(s, out int n) ? n : 0).Where(n => n > 0).ToList();

                var listaCandidatos = new List<EstudianteEscuela>();

                foreach (var p in personasDB)
                {
                    // REQUISITO 1: Precursor regular, precursor especial o misionero
                    if (string.IsNullOrEmpty(p.Servicio) ||
                        (!p.Servicio.Contains("Precursor regular", StringComparison.OrdinalIgnoreCase) &&
                         !p.Servicio.Contains("Precursor especial", StringComparison.OrdinalIgnoreCase) &&
                         !p.Servicio.Contains("Misionero", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // REQUISITO 2: Fecha de inicio de precursorado <= 1 de sept del año en curso
                    if (string.IsNullOrWhiteSpace(p.FechaInicioPrec) || !TryParseFechaFlexible(p.FechaInicioPrec, out DateTime fechaInicioPrec))
                    {
                        continue;
                    }

                    if (fechaInicioPrec > fechaLimiteInicioPrec)
                    {
                        continue;
                    }

                    // REQUISITO 3: Última escuela <= 31-08-2022 (o nunca haber asistido)
                    bool cumpleFiltroEscuela = true;

                    if (!string.IsNullOrWhiteSpace(p.FechaUltimaEscuela) && TryParseFechaFlexible(p.FechaUltimaEscuela, out DateTime fechaUltimaEscuela))
                    {
                        if (fechaUltimaEscuela > fechaCorteUltimaEscuela)
                        {
                            cumpleFiltroEscuela = false;
                        }
                    }

                    if (!cumpleFiltroEscuela) continue;

                    // =====================================================================
                    // 3. FILTRO DE DESCARTADOS
                    // =====================================================================
                    bool isDescartado = descartadosList.Contains(p.Id);

                    // Si estamos viendo la lista normal, nos saltamos los descartados
                    if (!_viendoDescartados && isDescartado) continue;

                    // Si estamos viendo la lista negra, nos saltamos los normales
                    if (_viendoDescartados && !isDescartado) continue;

                    // ¡PASÓ TODOS LOS FILTROS! AÑADIMOS AL LISTADO
                    listaCandidatos.Add(new EstudianteEscuela
                    {
                        Id = p.Id,
                        NombreComun = string.IsNullOrWhiteSpace(p.NombreComun) ? p.Nombre : p.NombreComun,
                        Congregacion = string.IsNullOrWhiteSpace(p.CongregacionNombre) ? p.CongregacionCodigo : p.CongregacionNombre,
                        Email = string.IsNullOrWhiteSpace(p.Email) ? p.EmailJW : p.Email,
                        Telefono = p.Telefono,
                        Edad = CalcularEdad(p.FechaNacimiento, hoy),
                        FechaInicio = p.FechaInicioPrec,
                        FechaUltimaEscuela = string.IsNullOrWhiteSpace(p.FechaUltimaEscuela) ? "Nunca" : p.FechaUltimaEscuela, // <--- NUEVA LÍNEA AÑADIDA AQUÍ
                        IsDescartado = isDescartado
                    });
                }

                // Ordenamos alfabéticamente por Congregación y después por Nombre Común
                listaCandidatos = listaCandidatos.OrderBy(c => c.Congregacion).ThenBy(c => c.NombreComun).ToList();

                // Mostramos los resultados en la interfaz
                TablaEstudiantes.ItemsSource = listaCandidatos;
                TxtTotalEstudiantes.Text = $"({listaCandidatos.Count} encontrados)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error cargando los estudiantes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // ACCIONES DE BOTONES
        // =========================================================
        private async void BtnVerDescartados_Click(object sender, RoutedEventArgs e)
        {
            _viendoDescartados = !_viendoDescartados;

            if (_viendoDescartados)
            {
                BtnVerDescartados.Content = "Volver a lista oficial";
                BtnVerDescartados.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            else
            {
                BtnVerDescartados.Content = "Ver descartados";
                BtnVerDescartados.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            }

            await CargarEstudiantesAsync();
        }

        private async void BtnAccionEstudiante_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Wpf.Ui.Controls.Button ?? sender as Button;
            if (btn?.Tag is EstudianteEscuela est)
            {
                DateTime hoy = DateTime.Today;
                int anioInicioServicio = hoy.Month >= 9 ? hoy.Year : hoy.Year - 1;
                string keyDescartados = "DescartadosEscuelas_" + anioInicioServicio;

                // Recuperamos la lista actual
                string descartadosStr = DatabaseHelper.ObtenerConfiguracion(keyDescartados, "");
                var descartadosList = string.IsNullOrWhiteSpace(descartadosStr)
                    ? new List<int>()
                    : descartadosStr.Split(',').Select(s => int.TryParse(s, out int n) ? n : 0).Where(n => n > 0).ToList();

                // Intercambiamos su estado
                if (est.IsDescartado)
                {
                    descartadosList.Remove(est.Id);
                }
                else
                {
                    if (!descartadosList.Contains(est.Id)) descartadosList.Add(est.Id);
                }

                // Lo guardamos en la base de datos
                DatabaseHelper.GuardarConfiguracion(keyDescartados, string.Join(",", descartadosList));

                // Recargamos la tabla visual
                await CargarEstudiantesAsync();
            }
        }

        // =========================================================
        // FUNCIONES AUXILIARES
        // =========================================================
        private bool TryParseFechaFlexible(string textoFecha, out DateTime fechaResultado)
        {
            fechaResultado = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(textoFecha)) return false;

            string[] formatosSoporte = { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "yyyy.MM.dd" };

            if (DateTime.TryParseExact(textoFecha.Trim(), formatosSoporte, CultureInfo.InvariantCulture, DateTimeStyles.None, out fechaResultado))
            {
                return true;
            }

            return DateTime.TryParse(textoFecha, out fechaResultado);
        }

        private string CalcularEdad(string fechaNacimientoTexto, DateTime fechaActual)
        {
            if (string.IsNullOrWhiteSpace(fechaNacimientoTexto) || !TryParseFechaFlexible(fechaNacimientoTexto, out DateTime fechaNac))
            {
                return "-";
            }

            int edad = fechaActual.Year - fechaNac.Year;
            if (fechaNac.Date > fechaActual.AddYears(-edad))
            {
                edad--;
            }

            return edad.ToString();
        }

        // =========================================================
        // CLASES: LISTA
        // =========================================================

        public static List<string> DiasLimpieza { get; } = new List<string>
        {
            "Sin limpieza",
            "Lunes",
            "Martes",
            "Miércoles",
            "Jueves",
            "Viernes",
            "Sábado"
        };


        private async Task CargarClasesAsync()
        {
            try
            {
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var clases = await Task.Run(() => DatabaseHelper.ObtenerEscuelasClases());

                ListaEscuelasClases.ItemsSource = clases;
                TxtSinClases.Visibility = clases.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error cargando las clases: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static DateTime LunesDe(DateTime fecha) =>
            fecha.Date.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));


        private void TarjetaEscuela_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (boton?.Tag is EscuelaClase escuela)
            {
                _ = AbrirDetalleAsync(escuela);
            }
        }


        private async Task AbrirDetalleAsync(EscuelaClase escuela)
        {
            _escuelaDetalle = escuela;
            await RefrescarDetalleAsync();
            OverlayDetalleEscuela.Visibility = Visibility.Visible;
        }


        private async Task RefrescarDetalleAsync()
        {
            try
            {
                if (_escuelaDetalle == null) return;

                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                string id = _escuelaDetalle.Id;

                var resultados = await Task.Run(() => (
                    DatabaseHelper.ObtenerEscuelaClase(id),
                    DatabaseHelper.ObtenerAlumnosEscuela(id)));

                EscuelaClase escuela = resultados.Item1;

                if (escuela == null) return;

                _escuelaDetalle = escuela;
                TituloDetalleEscuela.Text = escuela.SemanaTexto;

                InfoDetalleEscuela.Text =
                    "Salón: " + (string.IsNullOrWhiteSpace(escuela.Congregacion) ? "sin indicar" : escuela.Congregacion)
                    + "   ·   Instructor: " + escuela.InstructorTexto
                    + "   ·   Sustituto: " + escuela.SustitutoTexto
                    + "   ·   " + (escuela.Alumnos == 1 ? "1 estudiante" : escuela.Alumnos + " estudiantes")
                    + "   ·   " + (escuela.Asientos < 1 ? 12 : escuela.Asientos) + " butacas";

                ListaAlumnosEscuela.ItemsSource = resultados.Item2;
                NotasDetalleEscuela.Text = escuela.NotasTexto;
            }
            catch
            {
            }
        }


        private void BtnCerrarDetalle_Click(object sender, RoutedEventArgs e)
        {
            OverlayDetalleEscuela.Visibility = Visibility.Collapsed;
            _escuelaDetalle = null;
        }


        private void BtnProximamente_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Esta parte se programará más adelante (lo dejamos preparado en la clase).",
                "Próximamente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        private async void BtnQuitarAlumno_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (btn?.Tag is not EscuelaAlumno alumno || _escuelaDetalle == null)
            {
                return;
            }

            var confirmar = MessageBox.Show(
                $"¿Quitar a {alumno.Nombre} de esta clase?",
                "Quitar estudiante",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmar != MessageBoxResult.Yes)
            {
                return;
            }

            await Task.Run(() => DatabaseHelper.QuitarAlumnoEscuela(alumno.Id));

            await RefrescarDetalleAsync();
            await CargarClasesAsync();
        }


        private async void BtnEliminarEscuela_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            var confirmar = MessageBox.Show(
                $"¿Eliminar la {_escuelaDetalle.SemanaTexto.ToLower()}? Se quitarán también sus estudiantes apuntados.",
                "Eliminar escuela",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmar != MessageBoxResult.Yes)
            {
                return;
            }

            string id = _escuelaDetalle.Id;

            await Task.Run(() => DatabaseHelper.EliminarEscuelaClase(id));

            OverlayDetalleEscuela.Visibility = Visibility.Collapsed;
            _escuelaDetalle = null;

            await CargarClasesAsync();
        }


        private void BtnEditarEscuela_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            AbrirFormEscuela(_escuelaDetalle);
        }


        // =========================================================
        // CLASES: FORMULARIO (CREAR / EDITAR)
        // =========================================================

        private void BtnNuevaEscuela_Click(object sender, RoutedEventArgs e)
        {
            AbrirFormEscuela(null);
        }


        private void AbrirFormEscuela(EscuelaClase escuela)
        {
            try
            {
                _escuelaEditando = escuela;
                _recargandoFormulario = true;

                TituloFormEscuela.Text = escuela == null
                    ? "Nueva escuela"
                    : "Editar escuela";

                // Instructor y sustituto (con su congregación entre paréntesis).
                var instructores = new List<ItemPersonaEscuela>();

                foreach (var p in DatabaseHelper.ObtenerInstructoresEscuela())
                {
                    string nombre = string.IsNullOrWhiteSpace(p.NombreComun) ? p.Nombre : p.NombreComun;

                    string congregacion = string.IsNullOrWhiteSpace(p.CongregacionNombre)
                        ? p.CongregacionCodigo
                        : p.CongregacionNombre;

                    instructores.Add(new ItemPersonaEscuela
                    {
                        Persona = p,
                        NombreSolo = nombre,
                        Visual = string.IsNullOrWhiteSpace(congregacion)
                            ? nombre
                            : $"{nombre} ({congregacion})"
                    });
                }

                _instructoresTodos = instructores;

                _sustitutosTodos = new List<ItemPersonaEscuela>
                {
                    new ItemPersonaEscuela { Persona = null, Visual = "(sin sustituto)" }
                };

                _sustitutosTodos.AddRange(instructores.Select(i => new ItemPersonaEscuela
                {
                    Persona = i.Persona,
                    NombreSolo = i.NombreSolo,
                    Visual = i.Visual
                }));

                // Los selectores muestran todo; se escribe dentro para buscar.
                _recargandoFormulario = true;

                CmbInstructor.ItemsSource = _instructoresTodos;
                CmbInstructor.SelectedItem = _instructoresTodos.FirstOrDefault(i => i.Persona.Id == (escuela?.InstructorId ?? 0));

                CmbSustituto.ItemsSource = _sustitutosTodos;
                CmbSustituto.SelectedItem = escuela == null || escuela.SustitutoId == 0
                    ? _sustitutosTodos[0]
                    : _sustitutosTodos.FirstOrDefault(i => i.Persona != null && i.Persona.Id == escuela.SustitutoId) ?? _sustitutosTodos[0];

                _recargandoFormulario = false;

                // Congregaciones (salón del reino) — se puede escribir otra.
                try
                {
                    CmbCongregacion.ItemsSource = DatabaseHelper
                        .ObtenerCongregaciones()
                        .OrderBy(c => c.Nombre)
                        .Select(c => c.Nombre)
                        .ToList();
                }
                catch
                {
                }

                CmbCongregacion.Text = escuela?.Congregacion ?? "";

                // Fecha de la semana (lunes a sábado): se marca entera en el calendario.
                DateTime fecha =
                    escuela != null && DateTime.TryParse(escuela.SemanaInicio, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parseada)
                        ? parseada
                        : DateTime.Today;

                MarcarSemana(LunesDe(fecha));

                TxtNotasEscuela.Text = escuela?.Notas ?? "";

                _recargandoFormulario = false;

                OverlayFormEscuela.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _recargandoFormulario = false;

                MessageBox.Show($"No se pudo abrir el formulario: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // =========================================================
        // BÚSQUEDA INTEGRADA EN LOS DESPLEGABLES
        // =========================================================

        private bool _filtrandoInstructor;
        private bool _filtrandoSustituto;


        private void CmbInstructor_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbInstructor.Template.FindName("PART_EditableTextBox", CmbInstructor) is TextBox caja)
                {
                    caja.TextChanged -= CmbInstructorTexto_TextChanged;
                    caja.TextChanged += CmbInstructorTexto_TextChanged;
                }
            }
            catch
            {
            }
        }


        private void CmbSustituto_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbSustituto.Template.FindName("PART_EditableTextBox", CmbSustituto) is TextBox caja)
                {
                    caja.TextChanged -= CmbSustitutoTexto_TextChanged;
                    caja.TextChanged += CmbSustitutoTexto_TextChanged;
                }
            }
            catch
            {
            }
        }


        private void CmbInstructorTexto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_filtrandoInstructor)
            {
                return;
            }

            if (CmbInstructor.SelectedItem is ItemPersonaEscuela actual
                && string.Equals(
                    actual.Visual,
                    (sender as TextBox)?.Text ?? "",
                    StringComparison.Ordinal))
            {
                return;
            }

            _filtrandoInstructor =
                true;

            try
            {
                FiltrarComboPersonas(
                    CmbInstructor,
                    _instructoresTodos);
            }
            finally
            {
                _filtrandoInstructor =
                    false;
            }
        }


        private void CmbSustitutoTexto_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_filtrandoSustituto)
            {
                return;
            }

            if (CmbSustituto.SelectedItem is ItemPersonaEscuela actual
                && string.Equals(
                    actual.Visual,
                    (sender as TextBox)?.Text ?? "",
                    StringComparison.Ordinal))
            {
                return;
            }

            _filtrandoSustituto =
                true;

            try
            {
                FiltrarComboPersonas(
                    CmbSustituto,
                    _sustitutosTodos);
            }
            finally
            {
                _filtrandoSustituto =
                    false;
            }
        }


        /// <summary>
        /// Filtra el desplegable mientras se escribe dentro de él
        /// (nombre o congregación) y abre la lista con lo que coincide.
        /// </summary>
        private void FiltrarComboPersonas(
            ComboBox combo,
            List<ItemPersonaEscuela> todos)
        {
            try
            {
                var caja =
                    combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;

                string texto =
                    caja?.Text ?? "";

                string filtro =
                    texto.Trim();

                List<ItemPersonaEscuela> visibles =
                    string.IsNullOrWhiteSpace(filtro)
                        ? todos
                        : todos
                            .Where(i => CoincideBusqueda(i, filtro))
                            .ToList();

                combo.SelectedItem =
                    null;

                combo.ItemsSource =
                    visibles;

                combo.IsDropDownOpen =
                    visibles.Count > 0;

                if (caja != null)
                {
                    caja.Text =
                        texto;

                    caja.CaretIndex =
                        texto.Length;
                }

                // Si lo escrito coincide exactamente con una opción, se elige sola.
                var exacta =
                    visibles.FirstOrDefault(
                        i => string.Equals(
                            i.Visual,
                            filtro,
                            StringComparison.OrdinalIgnoreCase));

                if (exacta != null)
                {
                    combo.SelectedItem =
                        exacta;
                }
            }
            catch
            {
            }
        }


        private static bool CoincideBusqueda(
            ItemPersonaEscuela item,
            string filtro) =>
            (item.Visual ?? "")
                .IndexOf(
                    filtro,
                    StringComparison.OrdinalIgnoreCase) >= 0;


        private void CalendarioSemana_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_ajustandoSemana)
                {
                    return;
                }

                DateTime? elegida = null;

                foreach (object fecha in e.AddedItems)
                {
                    if (fecha is DateTime dia)
                    {
                        elegida = dia;
                        break;
                    }
                }

                if (elegida == null)
                {
                    if (CalendarioSemana.SelectedDates.Count > 0)
                    {
                        elegida = CalendarioSemana.SelectedDates[0];
                    }
                    else
                    {
                        elegida = _semanaSeleccionada;
                    }
                }

                MarcarSemana(LunesDe(elegida.Value));
            }
            catch
            {
            }
        }


        /// <summary>
        /// Marca la semana completa (lunes a sábado) en el calendario.
        /// </summary>
        private void MarcarSemana(DateTime lunes)
        {
            _ajustandoSemana = true;

            try
            {
                _semanaSeleccionada = lunes;

                CalendarioSemana.SelectedDates.Clear();

                for (int i = 0; i < 6; i++)
                {
                    CalendarioSemana.SelectedDates.Add(lunes.AddDays(i));
                }

                CalendarioSemana.DisplayDate = lunes;
            }
            catch
            {
            }
            finally
            {
                _ajustandoSemana = false;
            }

            if (LblSemanaEscuela != null)
            {
                LblSemanaEscuela.Text =
                    EscuelaClase.FormatearSemana(lunes.ToString("yyyy-MM-dd"))
                    + " (lunes a sábado)";
            }
        }


        private void BtnCancelarFormEscuela_Click(object sender, RoutedEventArgs e)
        {
            OverlayFormEscuela.Visibility = Visibility.Collapsed;
            _escuelaEditando = null;
        }


        private async void BtnGuardarFormEscuela_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbInstructor.SelectedItem is not ItemPersonaEscuela instructor
                    || instructor.Persona == null)
                {
                    MessageBox.Show("Elige el instructor de la escuela.", "Falta el instructor", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ItemPersonaEscuela sustituto = CmbSustituto.SelectedItem as ItemPersonaEscuela;

                // Las butacas se ajustan desde la vista de asientos.
                int asientos =
                    _escuelaEditando != null && _escuelaEditando.Asientos > 0
                        ? _escuelaEditando.Asientos
                        : 12;

                var escuela = new EscuelaClase
                {
                    Id = _escuelaEditando?.Id ?? "",
                    FechaCreacion = _escuelaEditando?.FechaCreacion ?? "",
                    SemanaInicio = _semanaSeleccionada.ToString("yyyy-MM-dd"),
                    Congregacion = (CmbCongregacion.Text ?? "").Trim(),
                    InstructorId = instructor.Persona.Id,
                    InstructorNombre = string.IsNullOrWhiteSpace(instructor.NombreSolo) ? instructor.Visual : instructor.NombreSolo,
                    SustitutoId = sustituto?.Persona?.Id ?? 0,
                    SustitutoNombre = sustituto?.Persona == null
                        ? ""
                        : (string.IsNullOrWhiteSpace(sustituto.NombreSolo) ? sustituto.Visual : sustituto.NombreSolo),
                    Asientos = asientos,
                    Notas = (TxtNotasEscuela.Text ?? "").Trim()
                };

                await Task.Run(() => DatabaseHelper.GuardarEscuelaClase(escuela));

                OverlayFormEscuela.Visibility = Visibility.Collapsed;
                _escuelaEditando = null;

                await CargarClasesAsync();

                if (_escuelaDetalle != null
                    && OverlayDetalleEscuela.Visibility == Visibility.Visible)
                {
                    await RefrescarDetalleAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo guardar la escuela: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // =========================================================
        // CLASES: AÑADIR ESTUDIANTES
        // =========================================================

        private async void BtnAnadirEstudiantes_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            try
            {
                string escuelaId = _escuelaDetalle.Id;

                var yaApuntados = await Task.Run(() =>
                    DatabaseHelper.ObtenerAlumnosEscuela(escuelaId)
                        .Select(a => a.PersonaId)
                        .ToHashSet());

                var personas = await Task.Run(() => DatabaseHelper.ObtenerPersonas());

                // Filtros de la sección «Estudiantes»: solo los que les toca
                // hacer la escuela (precursorado y última escuela según el
                // año de servicio) y que no estén descartados.
                int anioInicioServicio =
                    DateTime.Today.Month >= 9
                        ? DateTime.Today.Year
                        : DateTime.Today.Year - 1;

                DateTime fechaLimiteInicioPrec =
                    new DateTime(anioInicioServicio, 9, 1);

                DateTime fechaCorteUltimaEscuela =
                    new DateTime(anioInicioServicio - 4, 8, 31);

                string descartadosStr =
                    DatabaseHelper.ObtenerConfiguracion(
                        "DescartadosEscuelas_" + anioInicioServicio,
                        "");

                var descartadosLista =
                    string.IsNullOrWhiteSpace(descartadosStr)
                        ? new List<int>()
                        : descartadosStr
                            .Split(',')
                            .Select(s => int.TryParse(s, out int n) ? n : 0)
                            .Where(n => n > 0)
                            .ToList();

                _posiblesAlumnos = personas
                    .Where(p =>
                        !yaApuntados.Contains(p.Id)
                        && !descartadosLista.Contains(p.Id)
                        && (p.Servicio ?? "").Length > 0
                        && ((p.Servicio ?? "").IndexOf("Precursor regular", StringComparison.OrdinalIgnoreCase) >= 0
                            || (p.Servicio ?? "").IndexOf("Precursor especial", StringComparison.OrdinalIgnoreCase) >= 0
                            || (p.Servicio ?? "").IndexOf("Misionero", StringComparison.OrdinalIgnoreCase) >= 0)
                        && !string.IsNullOrWhiteSpace(p.FechaInicioPrec)
                        && TryParseFechaFlexible(p.FechaInicioPrec, out DateTime fechaPrecAlumno)
                        && fechaPrecAlumno <= fechaLimiteInicioPrec
                        && (string.IsNullOrWhiteSpace(p.FechaUltimaEscuela)
                            || !TryParseFechaFlexible(p.FechaUltimaEscuela, out DateTime fechaEscuelaAlumno)
                            || fechaEscuelaAlumno <= fechaCorteUltimaEscuela))
                    .Select(p => new AlumnoPosibleEscuela
                    {
                        Persona = p,
                        Nombre = string.IsNullOrWhiteSpace(p.NombreComun) ? p.Nombre : p.NombreComun,
                        Detalle = string.IsNullOrWhiteSpace(p.CongregacionNombre) ? p.CongregacionCodigo : p.CongregacionNombre
                    })
                    .OrderBy(a => a.Nombre)
                    .ToList();

                SubtituloAlumnos.Text = _escuelaDetalle.SemanaTexto;
                TxtBuscarAlumno.Text = "";

                AplicarFiltroAlumnos();

                OverlayAlumnos.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la lista de estudiantes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void TxtBuscarAlumno_TextChanged(object sender, TextChangedEventArgs e)
        {
            AplicarFiltroAlumnos();
        }


        private void AplicarFiltroAlumnos()
        {
            string busqueda = (TxtBuscarAlumno.Text ?? "").Trim();

            var visibles = string.IsNullOrWhiteSpace(busqueda)
                ? _posiblesAlumnos
                : _posiblesAlumnos
                    .Where(a =>
                        a.Nombre.IndexOf(busqueda, StringComparison.OrdinalIgnoreCase) >= 0
                        || (a.Detalle ?? "").IndexOf(busqueda, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            ListaPosiblesAlumnos.ItemsSource = visibles;
        }


        private void BtnCancelarAlumnos_Click(object sender, RoutedEventArgs e)
        {
            OverlayAlumnos.Visibility = Visibility.Collapsed;
        }


        private async void BtnConfirmarAlumnos_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            var seleccionados = _posiblesAlumnos
                .Where(a => a.Seleccionado)
                .Select(a => a.Persona)
                .ToList();

            if (seleccionados.Count == 0)
            {
                MessageBox.Show("Marca al menos un estudiante para añadirlo.", "Añadir estudiantes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int agregados = await Task.Run(() =>
                    DatabaseHelper.AgregarAlumnosEscuela(_escuelaDetalle.Id, seleccionados));

                OverlayAlumnos.Visibility = Visibility.Collapsed;

                await RefrescarDetalleAsync();
                await CargarClasesAsync();

                if (agregados > 0)
                {
                    MessageBox.Show(
                        agregados == 1
                            ? "Se añadió 1 estudiante a la clase."
                            : $"Se añadieron {agregados} estudiantes a la clase.",
                        "Añadir estudiantes",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron añadir: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // =========================================================
        // CLASES: ASIENTOS (BUTACAS)
        // =========================================================

        private async void BtnAsientos_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            _asientoElegido = null;
            LblAsientoElegido.Text = "Ninguna butaca elegida";

            TituloAsientos.Text = "BUTACAS · " + _escuelaDetalle.SemanaTexto;

            await RefrescarAsientosAsync();

            OverlayAsientos.Visibility = Visibility.Visible;
        }


        private async Task RefrescarAsientosAsync()
        {
            try
            {
                if (_escuelaDetalle == null) return;

                string id = _escuelaDetalle.Id;

                var resultados = await Task.Run(() => (
                    DatabaseHelper.ObtenerEscuelaClase(id),
                    DatabaseHelper.ObtenerAlumnosEscuela(id)));

                if (resultados.Item1 != null)
                {
                    _escuelaDetalle = resultados.Item1;
                }

                _alumnosAsientos = resultados.Item2;

                // Lista del selector inferior.
                var items = _alumnosAsientos
                    .Select(a => new ItemAlumnoEscuela
                    {
                        Alumno = a,
                        Visual = a.Nombre + (a.Asiento > 0 ? $" (asiento {a.Asiento})" : "")
                    })
                    .ToList();

                CmbAlumnoAsiento.ItemsSource = items;

                if (items.Count > 0 && CmbAlumnoAsiento.SelectedIndex < 0)
                {
                    CmbAlumnoAsiento.SelectedIndex = 0;
                }

                // Sala: mesa del instructor centrada + filas de butacas.
                ConstruirSala(
                    _escuelaDetalle,
                    _alumnosAsientos);
            }
            catch
            {
            }
        }


        /// <summary>
        /// Dibuja la sala: mesa del instructor centrada delante de la
        /// primera fila, las filas de butacas y los pasillos.
        /// </summary>
        private void ConstruirSala(
            EscuelaClase escuela,
            List<EscuelaAlumno> alumnos)
        {
            try
            {
                int filas =
                    escuela.FilasEfectivas;

                int columnas =
                    escuela.ColumnasEfectivas;

                List<int> pasillos =
                    escuela.PasillosLista;

                TxtNumFilas.Text =
                    filas.ToString();

                TxtNumColumnas.Text =
                    columnas.ToString();

                // Botones de pasillos (entre columna y columna).
                PanelPasillos.Children.Clear();

                for (int k = 1; k < columnas; k++)
                {
                    bool activo =
                        pasillos.Contains(k);

                    var boton =
                        new Wpf.Ui.Controls.Button
                        {
                            Content = $"col. {k}|{k + 1}",
                            Appearance = activo
                                ? Wpf.Ui.Controls.ControlAppearance.Primary
                                : Wpf.Ui.Controls.ControlAppearance.Secondary,
                            Padding = new Thickness(8, 3, 8, 3),
                            FontSize = 11.5,
                            Margin = new Thickness(0, 0, 6, 0),
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Tag = k
                        };

                    boton.Click += BtnPasillo_Click;

                    System.Windows.Automation.AutomationProperties.SetName(
                        boton,
                        $"Pasillo tras la columna {k}");

                    PanelPasillos.Children.Add(
                        boton);
                }

                PanelFilasAsientos.Children.Clear();

                // Mesa del instructor (centrada, delante de la primera fila).
                var mesa =
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x2A, 0x34)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0xA3, 0xE3)),
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(10),
                        Width = 260,
                        Height = 46,
                        Margin = new Thickness(0, 0, 0, 20),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "MESA DEL INSTRUCTOR",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xDD, 0xF5)),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 12.5,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                System.Windows.Automation.AutomationProperties.SetName(
                    mesa,
                    "Mesa del instructor");

                PanelFilasAsientos.Children.Add(
                    mesa);

                int numero =
                    0;

                for (int f = 1; f <= filas; f++)
                {
                    var fila =
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 8)
                        };

                    for (int c = 1; c <= columnas; c++)
                    {
                        numero++;

                        var ocupante =
                            alumnos.FirstOrDefault(
                                a => a.Asiento == numero);

                        bool ocupado =
                            ocupante != null;

                        var asiento =
                            new AsientoEscuela
                            {
                                Numero = numero,
                                Ocupante = ocupado
                                    ? ocupante!.Nombre
                                    : "Libre",
                                Fondo = new SolidColorBrush(
                                    ocupado
                                        ? Color.FromRgb(0x1B, 0x2A, 0x3E)
                                        : Color.FromRgb(0x14, 0x18, 0x20)),
                                Borde = new SolidColorBrush(
                                    ocupado
                                        ? Color.FromRgb(0x4B, 0xA3, 0xE3)
                                        : Color.FromRgb(0x2A, 0x2F, 0x38))
                            };

                        var botonAsiento =
                            new Wpf.Ui.Controls.Button
                            {
                                Background = System.Windows.Media.Brushes.Transparent,
                                BorderThickness = new Thickness(0),
                                Padding = new Thickness(0),
                                Margin = new Thickness(
                                    0,
                                    0,
                                    c == columnas ? 0 : 8,
                                    0),
                                Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = asiento,
                                Width = 110,
                                Height = 74
                            };

                        var interior =
                            new Border
                            {
                                CornerRadius = new CornerRadius(10),
                                Background = asiento.Fondo,
                                BorderBrush = asiento.Borde,
                                BorderThickness = new Thickness(1.5)
                            };

                        var contenido =
                            new StackPanel
                            {
                                VerticalAlignment = VerticalAlignment.Center
                            };

                        contenido.Children.Add(
                            new TextBlock
                            {
                                Text = "Butaca " + numero,
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA0)),
                                HorizontalAlignment = HorizontalAlignment.Center
                            });

                        contenido.Children.Add(
                            new TextBlock
                            {
                                Text = asiento.Ocupante,
                                FontSize = 12.5,
                                Foreground = System.Windows.Media.Brushes.White,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(4, 3, 4, 0)
                            });

                        interior.Child =
                            contenido;

                        botonAsiento.Content =
                            interior;

                        botonAsiento.Click +=
                            Butaca_Click;

                        System.Windows.Automation.AutomationProperties.SetName(
                            botonAsiento,
                            asiento.NombreAccesible);

                        fila.Children.Add(
                            botonAsiento);

                        if (pasillos.Contains(c))
                        {
                            fila.Children.Add(
                                new Border
                                {
                                    Width = 26
                                });
                        }
                    }

                    PanelFilasAsientos.Children.Add(
                        fila);
                }
            }
            catch
            {
            }
        }


        private async void BtnMenosFila_Click(object sender, RoutedEventArgs e)
        {
            await AjustarDisposicionAsync(-1, 0);
        }


        private async void BtnMasFila_Click(object sender, RoutedEventArgs e)
        {
            await AjustarDisposicionAsync(1, 0);
        }


        private async void BtnMenosColumna_Click(object sender, RoutedEventArgs e)
        {
            await AjustarDisposicionAsync(0, -1);
        }


        private async void BtnMasColumna_Click(object sender, RoutedEventArgs e)
        {
            await AjustarDisposicionAsync(0, 1);
        }


        private async Task AjustarDisposicionAsync(
            int deltaFilas,
            int deltaColumnas)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                int filas =
                    Math.Clamp(
                        _escuelaDetalle.FilasEfectivas + deltaFilas,
                        1,
                        15);

                int columnas =
                    Math.Clamp(
                        _escuelaDetalle.ColumnasEfectivas + deltaColumnas,
                        1,
                        20);

                if (filas == _escuelaDetalle.FilasEfectivas
                    && columnas == _escuelaDetalle.ColumnasEfectivas)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                string pasillos =
                    string.Join(
                        ",",
                        _escuelaDetalle.PasillosLista
                            .Where(p => p < columnas));

                await Task.Run(
                    () => DatabaseHelper.EstablecerDisposicionEscuela(
                        id,
                        filas,
                        columnas,
                        pasillos));

                await RefrescarAsientosAsync();
            }
            catch
            {
            }
        }


        private async void BtnPasillo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                var boton =
                    sender as Wpf.Ui.Controls.Button ?? sender as Button;

                if (boton?.Tag is not int columna)
                {
                    return;
                }

                List<int> lista =
                    _escuelaDetalle.PasillosLista;

                if (lista.Contains(columna))
                {
                    lista.Remove(columna);
                }
                else
                {
                    lista.Add(columna);
                }

                string id =
                    _escuelaDetalle.Id;

                string pasillos =
                    string.Join(
                        ",",
                        lista.OrderBy(n => n));

                await Task.Run(
                    () => DatabaseHelper.EstablecerDisposicionEscuela(
                        id,
                        _escuelaDetalle.FilasEfectivas,
                        _escuelaDetalle.ColumnasEfectivas,
                        pasillos));

                await RefrescarAsientosAsync();
            }
            catch
            {
            }
        }


        // =========================================================
        // CLASES: PDF DE LA DISPOSICIÓN DE BUTACAS
        // =========================================================

        private async void BtnPdfAsientos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                var resultados =
                    await Task.Run(() => (
                        DatabaseHelper.ObtenerEscuelaClase(id),
                        DatabaseHelper.ObtenerAlumnosEscuela(id)));

                if (resultados.Item1 == null)
                {
                    return;
                }

                EscuelaClase escuela =
                    resultados.Item1;

                List<EscuelaAlumno> alumnos =
                    resultados.Item2;

                string carpeta =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "SCCosmos",
                        "PDFs");

                if (!Directory.Exists(carpeta))
                {
                    Directory.CreateDirectory(carpeta);
                }

                string ruta =
                    Path.Combine(
                        carpeta,
                        $"Butacas_{escuela.SemanaInicio}.pdf");

                await Task.Run(
                    () => GenerarPdfDisposicionButacas(
                        escuela,
                        alumnos,
                        ruta));

                DatabaseHelper.AnotarActividad(
                    "Escuelas",
                    "Generó el PDF de la disposición de butacas de la escuela de la "
                    + escuela.SemanaTexto.ToLower());

                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(ruta)
                        {
                            UseShellExecute = true
                        });
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo generar el PDF:\n\n{ex.Message}", "PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static void GenerarPdfDisposicionButacas(
            EscuelaClase escuela,
            List<EscuelaAlumno> alumnos,
            string ruta)
        {
            int filas =
                escuela.FilasEfectivas;

            int columnas =
                escuela.ColumnasEfectivas;

            List<int> pasillos =
                escuela.PasillosLista;

            using (var pdf = new PdfDocument(new PdfWriter(ruta)))
            {
                using var documento =
                    new Document(
                        pdf,
                        iText.Kernel.Geom.PageSize.A4.Rotate());

                documento.SetMargins(28, 28, 28, 28);

                documento.Add(
                    new Paragraph("DISPOSICIÓN DE BUTACAS")
                        .SetFontSize(16)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(
                            escuela.SemanaTexto
                            + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                                ? ""
                                : " · " + escuela.Congregacion)
                            + (string.IsNullOrWhiteSpace(escuela.InstructorNombre)
                                ? ""
                                : " · Instructor: " + escuela.InstructorNombre))
                        .SetFontSize(10.5f)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(" ")
                        .SetFontSize(8));

                // Anchos: una columna por butaca + un hueco por pasillo.
                var anchos =
                    new List<float>();

                for (int c = 1; c <= columnas; c++)
                {
                    anchos.Add(1f);

                    if (pasillos.Contains(c))
                    {
                        anchos.Add(0.22f);
                    }
                }

                var tabla =
                    new Table(
                            iText.Layout.Properties.UnitValue.CreatePercentArray(
                                anchos.ToArray()))
                        .UseAllAvailableWidth();

                // Mesa del instructor centrada, delante de la primera fila.
                tabla.AddCell(
                    new Cell(1, anchos.Count)
                        .Add(new Paragraph("MESA DEL INSTRUCTOR")
                            .SetFontSize(11)
                            .SetTextAlignment(ITextAlign.CENTER))
                        .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(0xCC, 0xD9, 0xE8))
                        .SetPadding(6));

                int numero =
                    0;

                for (int f = 1; f <= filas; f++)
                {
                    for (int c = 1; c <= columnas; c++)
                    {
                        numero++;

                        var ocupante =
                            alumnos.FirstOrDefault(
                                a => a.Asiento == numero);

                        var celda =
                            new Cell()
                                .SetPadding(4);

                        celda.Add(
                            new Paragraph(numero.ToString())
                                .SetFontSize(12)
                                .SetMargin(0)
                                .SetTextAlignment(ITextAlign.CENTER));

                        celda.Add(
                            new Paragraph(ocupante == null ? "Libre" : ocupante.Nombre)
                                .SetFontSize(8.5f)
                                .SetMargin(0)
                                .SetTextAlignment(ITextAlign.CENTER));

                        if (ocupante != null)
                        {
                            celda.SetBackgroundColor(
                                new iText.Kernel.Colors.DeviceRgb(0xDD, 0xE8, 0xF5));
                        }

                        tabla.AddCell(
                            celda);

                        if (pasillos.Contains(c))
                        {
                            tabla.AddCell(
                                new Cell()
                                    .SetBorder(iText.Layout.Borders.Border.NO_BORDER));
                        }
                    }
                }

                documento.Add(
                    tabla);

                documento.Add(
                    new Paragraph("Butacas ocupadas: sombreadas con el nombre. Libres: solo el número.")
                        .SetFontSize(8.5f)
                        .SetTextAlignment(ITextAlign.CENTER)
                        .SetMarginTop(10));
            }
        }


        // =========================================================
        // CLASES: TALLERES
        // =========================================================

        private List<ItemPersonaEscuela> _personasTodas;
        private EscuelaTallerPunto _puntoTallerPendiente;
        private EscuelaHorarioDia _diaHorarioPendiente;
        private string _campoPendiente = "";


        private async void BtnTalleres_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            try
            {
                string id =
                    _escuelaDetalle.Id;

                var puntos =
                    await Task.Run(
                        () => DatabaseHelper.ObtenerTalleresEscuela(id));

                SubtituloTalleres.Text =
                    _escuelaDetalle.SemanaTexto
                    + " · asigna a cada punto su publicador, acompañante y hora de ensayo";

                ConstruirPanelTalleres(
                    puntos);

                OverlayTalleres.Visibility =
                    Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron abrir los talleres:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ConstruirPanelTalleres(
            List<EscuelaTallerPunto> puntos)
        {
            try
            {
                PanelTalleres.Children.Clear();

                foreach (var grupo in
                         puntos.GroupBy(p => p.Taller).OrderBy(g => g.Key))
                {
                    PanelTalleres.Children.Add(
                        new TextBlock
                        {
                            Text = $"TALLER {grupo.Key} (T{grupo.Key})",
                            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0xA3, 0xE3)),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 13,
                            Margin = new Thickness(0, 10, 0, 6)
                        });

                    foreach (EscuelaTallerPunto punto in grupo)
                    {
                        var fila =
                            new Border
                            {
                                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x20)),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(10, 8, 10, 8),
                                Margin = new Thickness(0, 0, 0, 6)
                            };

                        var grid =
                            new Grid();

                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

                        var texto =
                            new TextBlock
                            {
                                Text = punto.Titulo,
                                Foreground = System.Windows.Media.Brushes.White,
                                FontSize = 12.5,
                                TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 10, 0)
                            };

                        Grid.SetColumn(texto, 0);
                        grid.Children.Add(texto);

                        var btnAsignado =
                            new Wpf.Ui.Controls.Button
                            {
                                Content = punto.AsignadoTexto,
                                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                                Padding = new Thickness(10, 6, 10, 6),
                                FontSize = 12,
                                Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = punto,
                                Margin = new Thickness(0, 0, 8, 0),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };

                        btnAsignado.Click +=
                            BtnElegirAsignadoTaller_Click;

                        System.Windows.Automation.AutomationProperties.SetName(
                            btnAsignado,
                            $"Asignado del punto {punto.Numero}");

                        Grid.SetColumn(btnAsignado, 1);
                        grid.Children.Add(btnAsignado);

                        var btnAcompanante =
                            new Wpf.Ui.Controls.Button
                            {
                                Content = punto.AcompananteTexto,
                                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                                Padding = new Thickness(10, 6, 10, 6),
                                FontSize = 12,
                                Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = punto,
                                Margin = new Thickness(0, 0, 8, 0),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };

                        btnAcompanante.Click +=
                            BtnElegirAcompananteTaller_Click;

                        System.Windows.Automation.AutomationProperties.SetName(
                            btnAcompanante,
                            $"Acompañante del punto {punto.Numero}");

                        Grid.SetColumn(btnAcompanante, 2);
                        grid.Children.Add(btnAcompanante);

                        var ensayo =
                            new TextBox
                            {
                                Text = punto.HoraEnsayo,
                                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                                Foreground = System.Windows.Media.Brushes.White,
                                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                                Padding = new Thickness(6, 4, 6, 4),
                                FontSize = 12,
                                Tag = punto,
                                ToolTip = "Hora del ensayo (17:00)"
                            };

                        ensayo.LostFocus +=
                            EnsaYo_LostFocus;

                        System.Windows.Automation.AutomationProperties.SetName(
                            ensayo,
                            $"Ensayo del punto {punto.Numero}");

                        Grid.SetColumn(ensayo, 3);
                        grid.Children.Add(ensayo);

                        fila.Child =
                            grid;

                        PanelTalleres.Children.Add(
                            fila);
                    }
                }
            }
            catch
            {
            }
        }


        private void BtnCerrarTalleres_Click(object sender, RoutedEventArgs e)
        {
            OverlayTalleres.Visibility =
                Visibility.Collapsed;
        }


        private async Task RefrescarTalleresAbiertosAsync()
        {
            if (_escuelaDetalle == null
                || OverlayTalleres.Visibility != Visibility.Visible)
            {
                return;
            }

            string id =
                _escuelaDetalle.Id;

            var puntos =
                await Task.Run(
                    () => DatabaseHelper.ObtenerTalleresEscuela(id));

            ConstruirPanelTalleres(
                puntos);
        }


        private void AsegurarPersonasTodas()
        {
            if (_personasTodas != null)
            {
                return;
            }

            _personasTodas =
                new List<ItemPersonaEscuela>();

            foreach (Persona p in
                     DatabaseHelper.ObtenerPersonas())
            {
                string nombre =
                    string.IsNullOrWhiteSpace(p.NombreComun)
                        ? p.Nombre
                        : p.NombreComun;

                string congregacion =
                    string.IsNullOrWhiteSpace(p.CongregacionNombre)
                        ? p.CongregacionCodigo
                        : p.CongregacionNombre;

                _personasTodas.Add(
                    new ItemPersonaEscuela
                    {
                        Persona = p,
                        NombreSolo = nombre,
                        Visual = string.IsNullOrWhiteSpace(congregacion)
                            ? nombre
                            : $"{nombre} ({congregacion})"
                    });
            }

            _personasTodas =
                _personasTodas
                    .OrderBy(i => i.Visual)
                    .ToList();
        }


        private void AbrirElegirPersona(
            string titulo)
        {
            AsegurarPersonasTodas();

            TituloElegirPersona.Text =
                titulo;

            TxtBuscarPersona.Text =
                "";

            AplicarFiltroPersonas();

            ListaElegirPersona.SelectedItem =
                null;

            OverlayElegirPersona.Visibility =
                Visibility.Visible;
        }


        private void AplicarFiltroPersonas()
        {
            string filtro =
                (TxtBuscarPersona.Text ?? "").Trim();

            ListaElegirPersona.ItemsSource =
                string.IsNullOrWhiteSpace(filtro)
                    ? _personasTodas
                    : _personasTodas
                        .Where(i => CoincideBusqueda(i, filtro))
                        .ToList();
        }


        private void TxtBuscarPersona_TextChanged(object sender, TextChangedEventArgs e)
        {
            AplicarFiltroPersonas();
        }


        private void BtnElegirAsignadoTaller_Click(object sender, RoutedEventArgs e)
        {
            var boton =
                sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (boton?.Tag is not EscuelaTallerPunto punto)
            {
                return;
            }

            _puntoTallerPendiente =
                punto;

            _campoPendiente =
                "Asignado";

            AbrirElegirPersona(
                $"Asignado del punto {punto.Numero} (taller {punto.TallerTexto})");
        }


        private void BtnElegirAcompananteTaller_Click(object sender, RoutedEventArgs e)
        {
            var boton =
                sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (boton?.Tag is not EscuelaTallerPunto punto)
            {
                return;
            }

            _puntoTallerPendiente =
                punto;

            _campoPendiente =
                "Acompañante";

            AbrirElegirPersona(
                $"Acompañante del punto {punto.Numero} (taller {punto.TallerTexto})");
        }


        private async void EnsaYo_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not TextBox caja
                    || caja.Tag is not EscuelaTallerPunto punto)
                {
                    return;
                }

                string nuevo =
                    (caja.Text ?? "").Trim();

                if (string.Equals(
                        punto.HoraEnsayo,
                        nuevo,
                        StringComparison.Ordinal))
                {
                    return;
                }

                punto.HoraEnsayo =
                    nuevo;

                string semana =
                    _escuelaDetalle?.SemanaTexto.ToLower() ?? "semana sin fecha";

                await Task.Run(
                    () => DatabaseHelper.GuardarTallerPunto(
                        punto,
                        $"Cambió la hora de ensayo del punto {punto.Numero} (taller {punto.TallerTexto}) a {(nuevo.Length == 0 ? "sin hora" : nuevo)} en la escuela de la {semana}"));
            }
            catch
            {
            }
        }


        private async void BtnElegirPersona_Click(object sender, RoutedEventArgs e)
        {
            await AplicarEleccionPersonaAsync(
                ListaElegirPersona.SelectedItem as ItemPersonaEscuela);
        }


        private async void ListaElegirPersona_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            await AplicarEleccionPersonaAsync(
                ListaElegirPersona.SelectedItem as ItemPersonaEscuela);
        }


        private async void BtnElegirNadie_Click(object sender, RoutedEventArgs e)
        {
            await AplicarEleccionPersonaAsync(
                null);
        }


        private void BtnCancelarElegirPersona_Click(object sender, RoutedEventArgs e)
        {
            OverlayElegirPersona.Visibility =
                Visibility.Collapsed;
        }


        private async Task AplicarEleccionPersonaAsync(
            ItemPersonaEscuela elegido)
        {
            try
            {
                string semana =
                    _escuelaDetalle?.SemanaTexto.ToLower() ?? "semana sin fecha";

                if (_puntoTallerPendiente != null)
                {
                    EscuelaTallerPunto punto =
                        _puntoTallerPendiente;

                    string nombre =
                        elegido?.NombreSolo ?? "";

                    int personaId =
                        elegido?.Persona?.Id ?? 0;

                    string descripcion;

                    if (_campoPendiente == "Asignado")
                    {
                        punto.AsignadoId =
                            personaId;

                        punto.AsignadoNombre =
                            nombre;

                        descripcion =
                            personaId == 0
                                ? $"Dejó sin asignar el punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {semana}"
                                : $"Asignó a {nombre} al punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {semana}";
                    }
                    else
                    {
                        punto.AcompananteId =
                            personaId;

                        punto.AcompananteNombre =
                            nombre;

                        descripcion =
                            personaId == 0
                                ? $"Quitó el acompañante del punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {semana}"
                                : $"Puso a {nombre} como acompañante del punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {semana}";
                    }

                    await Task.Run(
                        () => DatabaseHelper.GuardarTallerPunto(
                            punto,
                            descripcion));

                    await RefrescarTalleresAbiertosAsync();
                }
                else if (_diaHorarioPendiente != null)
                {
                    EscuelaHorarioDia dia =
                        _diaHorarioPendiente;

                    string nombre =
                        elegido?.NombreSolo ?? "";

                    int personaId =
                        elegido?.Persona?.Id ?? 0;

                    string etiqueta =
                        _campoPendiente switch
                        {
                            "OracionInicial" => "oración inicial",
                            "OracionIntermedia" => "oración de la mañana",
                            _ => "oración final"
                        };

                    if (_campoPendiente == "OracionInicial")
                    {
                        dia.OracionInicialId = personaId;
                        dia.OracionInicialNombre = nombre;
                    }
                    else if (_campoPendiente == "OracionIntermedia")
                    {
                        dia.OracionIntermediaId = personaId;
                        dia.OracionIntermediaNombre = nombre;
                    }
                    else
                    {
                        dia.OracionFinalId = personaId;
                        dia.OracionFinalNombre = nombre;
                    }

                    string descripcion =
                        personaId == 0
                            ? $"Dejó sin asignar la {etiqueta} del {dia.Dia.ToLower()} en la escuela de la {semana}"
                            : $"Asignó a {nombre} para la {etiqueta} del {dia.Dia.ToLower()} en la escuela de la {semana}";

                    await Task.Run(
                        () => DatabaseHelper.GuardarHorarioDia(
                            dia,
                            descripcion));

                    await RefrescarHorarioAbiertoAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo guardar la elección:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _puntoTallerPendiente =
                    null;

                _diaHorarioPendiente =
                    null;

                OverlayElegirPersona.Visibility =
                    Visibility.Collapsed;
            }
        }


        // =========================================================
        // CLASES: HORARIO
        // =========================================================

        private async void BtnHorario_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            SubtituloHorario.Text =
                "Horario de la "
                + _escuelaDetalle.SemanaTexto.ToLower()
                + " · las horas de clase son fijas; asigna las oraciones";

            await RefrescarHorarioAbiertoAsync();

            OverlayHorario.Visibility =
                Visibility.Visible;
        }


        private async Task RefrescarHorarioAbiertoAsync()
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                var dias =
                    await Task.Run(
                        () => DatabaseHelper.ObtenerHorarioEscuela(id));

                ConstruirPanelHorario(
                    dias);
            }
            catch
            {
            }
        }


        private void ConstruirPanelHorario(
            List<EscuelaHorarioDia> dias)
        {
            try
            {
                PanelHorario.Children.Clear();

                foreach (EscuelaHorarioDia dia in dias)
                {
                    var tarjeta =
                        new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x20)),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(14, 12, 14, 12),
                            Margin = new Thickness(0, 0, 0, 10)
                        };

                    var contenido =
                        new StackPanel();

                    contenido.Children.Add(
                        new TextBlock
                        {
                            Text = dia.Dia.ToUpperInvariant(),
                            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0xA3, 0xE3)),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 14,
                            Margin = new Thickness(0, 0, 0, 8)
                        });

                    contenido.Children.Add(
                        FilaCancionYOracion(
                            DatabaseHelper.CancionInicialDe(dia.Dia),
                            "Oración inicial",
                            dia.OracionInicialTexto,
                            dia,
                            "OracionInicial"));

                    var lecciones =
                        DatabaseHelper.LeccionesDe(
                            dia.Dia);

                    for (int i = 0; i < lecciones.Count; i++)
                    {
                        contenido.Children.Add(
                            new TextBlock
                            {
                                Text = $"{lecciones[i].Nombre}  ({lecciones[i].Horario})",
                                Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD5, 0xDD)),
                                FontSize = 12.5,
                                Margin = new Thickness(16, 2, 0, 2)
                            });

                        if (i == 1)
                        {
                            contenido.Children.Add(
                                new TextBlock
                                {
                                    Text = "Pausa 15 minutos",
                                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xA3, 0xAE)),
                                    FontSize = 11,
                                    FontStyle = FontStyles.Italic,
                                    Margin = new Thickness(16, 2, 0, 2)
                                });
                        }

                        if (i == 2)
                        {
                            contenido.Children.Add(
                                FilaCancionYOracion(
                                    "",
                                    "Oración de la mañana",
                                    dia.OracionIntermediaTexto,
                                    dia,
                                    "OracionIntermedia"));

                            contenido.Children.Add(
                                new TextBlock
                                {
                                    Text = "Pausa 60 minutos",
                                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xA3, 0xAE)),
                                    FontSize = 11,
                                    FontStyle = FontStyles.Italic,
                                    Margin = new Thickness(16, 2, 0, 2)
                                });
                        }

                        if (i == 3)
                        {
                            contenido.Children.Add(
                                new TextBlock
                                {
                                    Text = dia.Dia == "Sábado"
                                        ? "Pausa 10 minutos"
                                        : "Pausa 15 minutos",
                                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xA3, 0xAE)),
                                    FontSize = 11,
                                    FontStyle = FontStyles.Italic,
                                    Margin = new Thickness(16, 2, 0, 2)
                                });
                        }
                    }

                    contenido.Children.Add(
                        FilaCancionYOracion(
                            DatabaseHelper.CancionFinalDe(dia.Dia),
                            "Oración final",
                            dia.OracionFinalTexto,
                            dia,
                            "OracionFinal"));

                    tarjeta.Child =
                        contenido;

                    PanelHorario.Children.Add(
                        tarjeta);
                }
            }
            catch
            {
            }
        }


        private UIElement FilaCancionYOracion(
            string cancion,
            string etiquetaOracion,
            string nombreOracion,
            EscuelaHorarioDia dia,
            string campo)
        {
            var panel =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3)
                };

            panel.Children.Add(
                new TextBlock
                {
                    Text = cancion ?? "",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5,
                    Width = 110,
                    VerticalAlignment = VerticalAlignment.Center
                });

            var boton =
                new Wpf.Ui.Controls.Button
                {
                    Content = $"{etiquetaOracion}: {nombreOracion}",
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 12,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = dia,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

            boton.Click +=
                BtnElegirOracion_Click;

            System.Windows.Automation.AutomationProperties.SetName(
                boton,
                $"{etiquetaOracion} del {dia.Dia}");

            panel.Children.Add(
                boton);

            return panel;
        }


        private void BtnElegirOracion_Click(object sender, RoutedEventArgs e)
        {
            var boton =
                sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (boton?.Tag is not EscuelaHorarioDia dia)
            {
                return;
            }

            string etiqueta =
                boton.Content as string ?? "Oración";

            if (etiqueta.StartsWith("Oración inicial"))
            {
                _campoPendiente = "OracionInicial";
            }
            else if (etiqueta.StartsWith("Oración de la mañana"))
            {
                _campoPendiente = "OracionIntermedia";
            }
            else
            {
                _campoPendiente = "OracionFinal";
            }

            _diaHorarioPendiente =
                dia;

            AbrirElegirPersona(
                $"{etiqueta.Split(':')[0]} del {dia.Dia}");
        }


        private void BtnCerrarHorario_Click(object sender, RoutedEventArgs e)
        {
            OverlayHorario.Visibility =
                Visibility.Collapsed;
        }


        // =========================================================
        // PDF DE TALLERES, HORARIO Y LIMPIEZA
        // =========================================================

        private static string RutaPdfEscuela(
            string nombreArchivo)
        {
            string carpeta =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos",
                    "PDFs");

            if (!Directory.Exists(carpeta))
            {
                Directory.CreateDirectory(carpeta);
            }

            return Path.Combine(
                carpeta,
                nombreArchivo);
        }


        private static void AbrirPdfGenerado(
            string ruta)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(ruta)
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }


        private async void BtnPdfTalleres_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                var resultados =
                    await Task.Run(() => (
                        DatabaseHelper.ObtenerEscuelaClase(id),
                        DatabaseHelper.ObtenerTalleresEscuela(id)));

                if (resultados.Item1 == null)
                {
                    return;
                }

                string ruta =
                    RutaPdfEscuela(
                        $"Talleres_{resultados.Item1.SemanaInicio}.pdf");

                await Task.Run(
                    () => GenerarPdfTalleres(
                        resultados.Item1,
                        resultados.Item2,
                        ruta));

                DatabaseHelper.AnotarActividad(
                    "Escuelas",
                    "Generó el PDF de los talleres de la escuela de la "
                    + resultados.Item1.SemanaTexto.ToLower());

                AbrirPdfGenerado(
                    ruta);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo generar el PDF:\n\n{ex.Message}", "PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static Dictionary<int, string> _congregacionesCache;


        private static string CongregacionDe(
            int personaId)
        {
            try
            {
                if (_congregacionesCache == null)
                {
                    _congregacionesCache =
                        new Dictionary<int, string>();

                    foreach (Persona p in
                             DatabaseHelper.ObtenerPersonas())
                    {
                        _congregacionesCache[p.Id] =
                            string.IsNullOrWhiteSpace(p.CongregacionNombre)
                                ? p.CongregacionCodigo
                                : p.CongregacionNombre;
                    }
                }

                if (_congregacionesCache.TryGetValue(
                        personaId,
                        out string congregacion)
                    && !string.IsNullOrWhiteSpace(congregacion))
                {
                    return congregacion;
                }
            }
            catch
            {
            }

            return "sin congregación";
        }


        private static void GenerarPdfTalleres(
            EscuelaClase escuela,
            List<EscuelaTallerPunto> puntos,
            string ruta)
        {
            using (var pdf = new PdfDocument(new PdfWriter(ruta)))
            {
                using var documento =
                    new Document(
                        pdf,
                        iText.Kernel.Geom.PageSize.A4.Rotate());

                documento.SetMargins(28, 28, 28, 28);

                documento.Add(
                    new Paragraph("TALLERES DE LA ESCUELA")
                        .SetFontSize(16)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(
                            escuela.SemanaTexto
                            + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                                ? ""
                                : " · " + escuela.Congregacion)
                            + (string.IsNullOrWhiteSpace(escuela.InstructorNombre)
                                ? ""
                                : " · Instructor: " + escuela.InstructorNombre))
                        .SetFontSize(10.5f)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(" ")
                        .SetFontSize(8));

                var tabla =
                    new Table(
                            iText.Layout.Properties.UnitValue.CreatePercentArray(
                                new[] { 7f, 5f, 40f, 18f, 18f, 8f }))
                        .UseAllAvailableWidth();

                tabla.AddHeaderCell("Taller");
                tabla.AddHeaderCell("Min");
                tabla.AddHeaderCell("Tema");
                tabla.AddHeaderCell("Asignado");
                tabla.AddHeaderCell("Acompañante");
                tabla.AddHeaderCell("Ensayo");

                foreach (EscuelaTallerPunto punto in puntos)
                {
                    tabla.AddCell($"{punto.TallerTexto} · {punto.Numero}");
                    tabla.AddCell(punto.Minutos);
                    tabla.AddCell(punto.Tema);
                    tabla.AddCell(
                        string.IsNullOrWhiteSpace(punto.AsignadoNombre)
                            ? "—"
                            : punto.AsignadoNombre + " (" + CongregacionDe(punto.AsignadoId) + ")");
                    tabla.AddCell(
                        string.IsNullOrWhiteSpace(punto.AcompananteNombre)
                            ? "—"
                            : punto.AcompananteNombre + " (" + CongregacionDe(punto.AcompananteId) + ")");
                    tabla.AddCell(
                        string.IsNullOrWhiteSpace(punto.HoraEnsayo)
                            ? "—"
                            : punto.HoraEnsayo);
                }

                documento.Add(
                    tabla);
            }
        }


        private async void BtnPdfHorario_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                var resultados =
                    await Task.Run(() => (
                        DatabaseHelper.ObtenerEscuelaClase(id),
                        DatabaseHelper.ObtenerHorarioEscuela(id)));

                if (resultados.Item1 == null)
                {
                    return;
                }

                string ruta =
                    RutaPdfEscuela(
                        $"Horario_{resultados.Item1.SemanaInicio}.pdf");

                await Task.Run(
                    () => GenerarPdfHorario(
                        resultados.Item1,
                        resultados.Item2,
                        ruta));

                DatabaseHelper.AnotarActividad(
                    "Escuelas",
                    "Generó el PDF del horario de la escuela de la "
                    + resultados.Item1.SemanaTexto.ToLower());

                AbrirPdfGenerado(
                    ruta);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo generar el PDF:\n\n{ex.Message}", "PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static void GenerarPdfHorario(
            EscuelaClase escuela,
            List<EscuelaHorarioDia> dias,
            string ruta)
        {
            using (var pdf = new PdfDocument(new PdfWriter(ruta)))
            {
                using var documento =
                    new Document(
                        pdf,
                        iText.Kernel.Geom.PageSize.A4.Rotate());

                documento.SetMargins(24, 24, 24, 24);

                documento.Add(
                    new Paragraph("HORARIO DE LA ESCUELA")
                        .SetFontSize(16)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(
                            escuela.SemanaTexto
                            + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                                ? ""
                                : " · " + escuela.Congregacion))
                        .SetFontSize(10.5f)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(" ")
                        .SetFontSize(8));

                var tabla =
                    new Table(
                            iText.Layout.Properties.UnitValue.CreatePercentArray(
                                new[] { 1f, 1f, 1f, 1f, 1f, 1f }))
                        .UseAllAvailableWidth();

                var fondoDia =
                    new iText.Kernel.Colors.DeviceRgb(0xD9, 0xE2, 0xF3);

                var fondoOracion =
                    new iText.Kernel.Colors.DeviceRgb(0xE2, 0xEF, 0xDA);

                ITextCell Celda(
                    string linea1,
                    string linea2,
                    bool esOracion)
                {
                    var celda =
                        new ITextCell()
                            .SetPadding(4);

                    if (!string.IsNullOrWhiteSpace(linea1))
                    {
                        celda.Add(
                            new Paragraph(linea1)
                                .SetFontSize(9.5f)
                                .SetMargin(0)
                                .SetTextAlignment(ITextAlign.CENTER));
                    }

                    if (!string.IsNullOrWhiteSpace(linea2))
                    {
                        celda.Add(
                            new Paragraph(linea2)
                                .SetFontSize(8.5f)
                                .SetMargin(0)
                                .SetTextAlignment(ITextAlign.CENTER));
                    }

                    if (esOracion)
                    {
                        celda.SetBackgroundColor(
                            fondoOracion);
                    }

                    return celda;
                }

                ITextCell CeldaPausa(
                    string texto,
                    int columnas)
                {
                    return new ITextCell(1, columnas)
                        .Add(new Paragraph(texto)
                            .SetFontSize(9)
                            .SetTextAlignment(ITextAlign.CENTER))
                        .SetBackgroundColor(
                            fondoDia)
                        .SetPadding(3);
                }

                // Encabezado con los días.
                foreach (EscuelaHorarioDia dia in dias)
                {
                    tabla.AddHeaderCell(
                        new ITextCell()
                            .Add(new Paragraph(dia.Dia.ToUpperInvariant())
                                .SetFontSize(11)
                                .SetTextAlignment(ITextAlign.CENTER))
                            .SetTextAlignment(ITextAlign.CENTER)
                            .SetBackgroundColor(
                                fondoDia));
                }

                // Canción + oración inicial.
                foreach (EscuelaHorarioDia dia in dias)
                {
                    tabla.AddCell(
                        Celda(
                            DatabaseHelper.CancionInicialDe(dia.Dia),
                            "Oración: "
                            + (string.IsNullOrWhiteSpace(dia.OracionInicialNombre)
                                ? "—"
                                : dia.OracionInicialNombre),
                            false));
                }

                // Lecciones, oraciones y pausas (rejilla común).
                for (int i = 0; i < 6; i++)
                {
                    if (i == 2)
                    {
                        tabla.AddCell(
                            CeldaPausa(
                                "Pausa 15 minutos",
                                6));
                    }

                    if (i == 3)
                    {
                        foreach (EscuelaHorarioDia dia in dias)
                        {
                            tabla.AddCell(
                                Celda(
                                    "Oración",
                                    string.IsNullOrWhiteSpace(dia.OracionIntermediaNombre)
                                        ? "—"
                                        : dia.OracionIntermediaNombre,
                                    true));
                        }

                        tabla.AddCell(
                            CeldaPausa(
                                "Pausa 60 minutos",
                                6));
                    }

                    if (i == 4)
                    {
                        tabla.AddCell(
                            CeldaPausa(
                                "Pausa 15 minutos",
                                5));

                        tabla.AddCell(
                            CeldaPausa(
                                "Pausa 10 minutos",
                                1));
                    }

                    foreach (EscuelaHorarioDia dia in dias)
                    {
                        var lecciones =
                            DatabaseHelper.LeccionesDe(
                                dia.Dia);

                        string linea1 =
                            "";

                        string linea2 =
                            "";

                        if (i < lecciones.Count)
                        {
                            linea1 =
                                lecciones[i].Nombre;

                            linea2 =
                                "(" + lecciones[i].Horario + ")";
                        }

                        tabla.AddCell(
                            Celda(
                                linea1,
                                linea2,
                                false));
                    }
                }

                // Canción + oración final.
                foreach (EscuelaHorarioDia dia in dias)
                {
                    tabla.AddCell(
                        Celda(
                            DatabaseHelper.CancionFinalDe(dia.Dia),
                            "Oración: "
                            + (string.IsNullOrWhiteSpace(dia.OracionFinalNombre)
                                ? "—"
                                : dia.OracionFinalNombre),
                            false));
                }

                documento.Add(
                    tabla);
            }
        }


        private async void BtnPdfLimpieza_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_escuelaDetalle == null)
                {
                    return;
                }

                string id =
                    _escuelaDetalle.Id;

                var resultados =
                    await Task.Run(() => (
                        DatabaseHelper.ObtenerEscuelaClase(id),
                        DatabaseHelper.ObtenerAlumnosEscuela(id)));

                if (resultados.Item1 == null)
                {
                    return;
                }

                string ruta =
                    RutaPdfEscuela(
                        $"Limpieza_{resultados.Item1.SemanaInicio}.pdf");

                await Task.Run(
                    () => GenerarPdfLimpieza(
                        resultados.Item1,
                        resultados.Item2,
                        ruta));

                DatabaseHelper.AnotarActividad(
                    "Escuelas",
                    "Generó el PDF del programa de limpieza de la escuela de la "
                    + resultados.Item1.SemanaTexto.ToLower());

                AbrirPdfGenerado(
                    ruta);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo generar el PDF:\n\n{ex.Message}", "PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static void GenerarPdfLimpieza(
            EscuelaClase escuela,
            List<EscuelaAlumno> alumnos,
            string ruta)
        {
            using (var pdf = new PdfDocument(new PdfWriter(ruta)))
            {
                using var documento =
                    new Document(
                        pdf,
                        iText.Kernel.Geom.PageSize.A4);

                documento.SetMargins(36, 36, 36, 36);

                documento.Add(
                    new Paragraph("PROGRAMA DE LIMPIEZA")
                        .SetFontSize(16)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(
                            escuela.SemanaTexto
                            + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                                ? ""
                                : " · " + escuela.Congregacion))
                        .SetFontSize(10.5f)
                        .SetTextAlignment(ITextAlign.CENTER));

                documento.Add(
                    new Paragraph(" ")
                        .SetFontSize(8));

                var tabla =
                    new Table(
                            iText.Layout.Properties.UnitValue.CreatePercentArray(
                                new[] { 25f, 75f }))
                        .UseAllAvailableWidth();

                tabla.AddHeaderCell("Día");
                tabla.AddHeaderCell("Quién limpia");

                foreach (string dia in
                         new[]
                         {
                             "Lunes",
                             "Martes",
                             "Miércoles",
                             "Jueves",
                             "Viernes",
                             "Sábado"
                         })
                {
                    var nombres =
                        alumnos
                            .Where(
                                a => string.Equals(
                                    a.LimpiezaDia,
                                    dia,
                                    StringComparison.OrdinalIgnoreCase))
                            .Select(
                                a => a.Nombre)
                            .ToList();

                    tabla.AddCell(dia);
                    tabla.AddCell(
                        nombres.Count == 0
                            ? "— nadie todavía —"
                            : string.Join(
                                ", ",
                                nombres));
                }

                documento.Add(
                    tabla);
            }
        }


        private void Butaca_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Wpf.Ui.Controls.Button ?? sender as Button;

            if (boton?.Tag is AsientoEscuela asiento)
            {
                _asientoElegido = asiento;
                LblAsientoElegido.Text = $"Butaca {asiento.Numero} elegida";

                // Si está ocupada, preseleccionar a su ocupante.
                var items = CmbAlumnoAsiento.ItemsSource as List<ItemAlumnoEscuela>;

                if (items != null)
                {
                    var ocupante = items.FirstOrDefault(i => i.Alumno.Asiento == asiento.Numero);

                    if (ocupante != null)
                    {
                        CmbAlumnoAsiento.SelectedItem = ocupante;
                    }
                }
            }
        }


        private async void BtnAsignarAsiento_Click(object sender, RoutedEventArgs e)
        {
            if (_asientoElegido == null)
            {
                MessageBox.Show("Primero toca la butaca donde quieras sentar a la persona.", "Asientos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CmbAlumnoAsiento.SelectedItem is not ItemAlumnoEscuela item)
            {
                return;
            }

            await Task.Run(() => DatabaseHelper.AsignarAsientoEscuela(item.Alumno.Id, _asientoElegido.Numero));

            await RefrescarAsientosAsync();
        }


        private async void BtnVaciarAsiento_Click(object sender, RoutedEventArgs e)
        {
            if (_asientoElegido == null)
            {
                return;
            }

            var ocupante = _alumnosAsientos.FirstOrDefault(a => a.Asiento == _asientoElegido.Numero);

            if (ocupante == null)
            {
                return;
            }

            await Task.Run(() => DatabaseHelper.AsignarAsientoEscuela(ocupante.Id, 0));

            await RefrescarAsientosAsync();
        }


        private void BtnCerrarAsientos_Click(object sender, RoutedEventArgs e)
        {
            OverlayAsientos.Visibility = Visibility.Collapsed;
            _asientoElegido = null;
        }


        // =========================================================
        // CLASES: PROGRAMA DE LIMPIEZA
        // =========================================================

        private async void BtnLimpieza_Click(object sender, RoutedEventArgs e)
        {
            if (_escuelaDetalle == null)
            {
                return;
            }

            try
            {
                string id = _escuelaDetalle.Id;

                _alumnosLimpieza = await Task.Run(() => DatabaseHelper.ObtenerAlumnosEscuela(id));

                SubtituloLimpieza.Text =
                    _escuelaDetalle.SemanaTexto
                    + " · cada estudiante limpia el día que le asignes";

                _recargandoLimpieza = true;

                ListaLimpiezaAlumnos.ItemsSource = _alumnosLimpieza
                    .Select(a => new LineaLimpieza
                    {
                        Alumno = a,
                        Nombre = a.Nombre,
                        DiaSeleccionado = string.IsNullOrWhiteSpace(a.LimpiezaDia)
                            ? "Sin limpieza"
                            : a.LimpiezaDia
                    })
                    .ToList();

                _recargandoLimpieza = false;

                RefrescarResumenLimpieza();

                OverlayLimpieza.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _recargandoLimpieza = false;

                MessageBox.Show($"No se pudo abrir el programa de limpieza: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void RefrescarResumenLimpieza()
        {
            var resumen = new List<LineaResumenLimpieza>();

            foreach (string dia in new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" })
            {
                var nombres = _alumnosLimpieza
                    .Where(a => string.Equals(a.LimpiezaDia, dia, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.Nombre)
                    .ToList();

                resumen.Add(new LineaResumenLimpieza
                {
                    Dia = dia,
                    Nombres = nombres.Count == 0
                        ? "— nadie todavía —"
                        : string.Join(", ", nombres)
                });
            }

            ResumenLimpieza.ItemsSource = resumen;
        }


        private void ComboLimpieza_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_recargandoLimpieza)
                {
                    return;
                }

                if (sender is ComboBox combo
                    && combo.Tag is LineaLimpieza linea
                    && combo.SelectedItem is string dia)
                {
                    string diaReal = dia == "Sin limpieza" ? "" : dia;

                    if (string.Equals(linea.Alumno.LimpiezaDia, diaReal, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    linea.Alumno.LimpiezaDia = diaReal;

                    DatabaseHelper.AsignarLimpiezaEscuela(linea.Alumno.Id, diaReal);

                    RefrescarResumenLimpieza();
                }
            }
            catch
            {
            }
        }


        private void BtnCerrarLimpieza_Click(object sender, RoutedEventArgs e)
        {
            OverlayLimpieza.Visibility = Visibility.Collapsed;
        }


        // =========================================================
        // MODELO DE DATOS DE LA VISTA
        // =========================================================
        public class EstudianteEscuela
        {
            public int Id { get; set; }
            public string NombreComun { get; set; }
            public string Congregacion { get; set; }
            public string Email { get; set; }
            public string Telefono { get; set; }
            public string Edad { get; set; }
            public string FechaInicio { get; set; }

            public string FechaUltimaEscuela { get; set; } // <--- NUEVO CAMPO

            // Datos para la UI dinámica
            public bool IsDescartado { get; set; }
            public string TextoAccion => IsDescartado ? "Incluir" : "Descartar";
        }


        public class ItemPersonaEscuela
        {
            public Persona Persona { get; set; }
            public string NombreSolo { get; set; }
            public string Visual { get; set; }
        }


        public class ItemAlumnoEscuela
        {
            public EscuelaAlumno Alumno { get; set; }
            public string Visual { get; set; }
        }


        public class AlumnoPosibleEscuela
        {
            public Persona Persona { get; set; }
            public string Nombre { get; set; }
            public string Detalle { get; set; }
            public bool Seleccionado { get; set; }
        }


        public class AsientoEscuela
        {
            public int Numero { get; set; }
            public string Ocupante { get; set; }
            public string NumeroTexto => "Butaca " + Numero;
            public string NombreAccesible => "Butaca " + Numero + ": " + Ocupante;
            public Brush Fondo { get; set; }
            public Brush Borde { get; set; }
        }


        public class LineaLimpieza
        {
            public EscuelaAlumno Alumno { get; set; }
            public string Nombre { get; set; }
            public string DiaSeleccionado { get; set; }
        }


        public class LineaResumenLimpieza
        {
            public string Dia { get; set; }
            public string Nombres { get; set; }
        }
    }
}
