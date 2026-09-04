using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class EscuelasPage : Page
    {
        private bool _viendoDescartados = false;

        public EscuelasPage()
        {
            InitializeComponent();
            this.Loaded += EscuelasPage_Loaded;
        }

        private async void EscuelasPage_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarEstudiantesAsync();
        }

        private async Task CargarEstudiantesAsync()
        {
            try
            {
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
    }
}