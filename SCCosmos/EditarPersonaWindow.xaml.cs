using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class EditarPersonaWindow : Window
    {
        private int _personaId;
        private string _congreCodigo;
        private DateTime _fechaVisitaActual;
        private Persona _personaActual;

        public EditarPersonaWindow(int personaId, string congreCodigo, DateTime fechaVisitaActual)
        {
            InitializeComponent();
            _personaId = personaId;
            _congreCodigo = congreCodigo;
            _fechaVisitaActual = fechaVisitaActual;
            CargarDatos();
        }

        private void CargarDatos()
        {
            if (_personaId == 0)
            {
                // Preparamos persona vacía pero NO detenemos la ejecución
                _personaActual = new Persona { CongregacionCodigo = _congreCodigo };
                CmbFusion.Visibility = Visibility.Collapsed;
            }
            else
            {
                _personaActual = DatabaseHelper.ObtenerPersonaPorId(_personaId);
                if (_personaActual == null) { this.Close(); return; }
            }

            // Llenamos el formulario principal
            TxtNombre.Text = _personaActual.Nombre;
            TxtGrupo.Text = _personaActual.Grupo.ToString();

            if (DateTime.TryParseExact(_personaActual.FechaNacimiento, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime fn) || DateTime.TryParse(_personaActual.FechaNacimiento, out fn))
                DpNacimiento.SelectedDate = fn;

            if (DateTime.TryParseExact(_personaActual.FechaBautismo, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime fb) || DateTime.TryParse(_personaActual.FechaBautismo, out fb))
                DpBautismo.SelectedDate = fb;

            if (!string.IsNullOrWhiteSpace(_personaActual.Sexo))
            {
                if (_personaActual.Sexo.Equals("Hombre", StringComparison.OrdinalIgnoreCase)) CmbSexo.SelectedIndex = 0;
                else if (_personaActual.Sexo.Equals("Mujer", StringComparison.OrdinalIgnoreCase)) CmbSexo.SelectedIndex = 1;
            }

            CmbNombramiento.Text = string.IsNullOrEmpty(_personaActual.Nombramiento) ? "Ninguno" : _personaActual.Nombramiento;
            CmbServicio.Text = string.IsNullOrEmpty(_personaActual.Servicio) ? "Ninguno" : _personaActual.Servicio;

            string[] nombresMesesJson = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            string mesBuscado = nombresMesesJson[_fechaVisitaActual.Month - 1];
            string anioServicioBuscado = (_fechaVisitaActual.Month >= 9) ? (_fechaVisitaActual.Year + 1).ToString() : _fechaVisitaActual.Year.ToString();

            var personasBD = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo);

            // CARGA JW HUB
            var todosJW = personasBD.Where(p => !string.IsNullOrWhiteSpace(p.NombreJWHub)).Select(p => p.NombreJWHub).Distinct().ToList();
            var listaNombresJW = new List<string>();

            foreach (var jwName in todosJW)
            {
                bool usadoPorOtroReal = personasBD.Any(p => p.Id != _personaId && p.NombreJWHub == jwName && (p.Grupo > 0 || p.Nombre != p.NombreJWHub));
                if (!usadoPorOtroReal) listaNombresJW.Add(jwName);
            }

            if (!string.IsNullOrWhiteSpace(_personaActual.NombreJWHub) && !listaNombresJW.Contains(_personaActual.NombreJWHub))
                listaNombresJW.Add(_personaActual.NombreJWHub);

            var opcionesOrdenadas = listaNombresJW.Select(nombreJW => new { Original = nombreJW, Similitud = DatabaseHelper.CalcularSimilitud(_personaActual.Nombre ?? "", nombreJW) })
                .OrderByDescending(x => x.Similitud).Select(x => x.Original).ToList();

            opcionesOrdenadas.Insert(0, "-- Sin vincular --");
            CmbNombreJW.ItemsSource = opcionesOrdenadas;

            if (!string.IsNullOrWhiteSpace(_personaActual.NombreJWHub) && opcionesOrdenadas.Contains(_personaActual.NombreJWHub)) CmbNombreJW.SelectedItem = _personaActual.NombreJWHub;
            else CmbNombreJW.SelectedIndex = 0;

            // CARGA FUSIÓN (Si es nuevo, esto corre pero la UI está oculta)
            var otrasPersonas = personasBD.Where(p => p.Id != _personaId).ToList();
            var personasAntiguas = new List<Persona>();
            foreach (var p in otrasPersonas)
            {
                var actividad = DatabaseHelper.ObtenerActividadPersona(p.Id);
                if (!actividad.Any(a => a.Mes.ToLower() == mesBuscado && a.AnioServicio == anioServicioBuscado))
                    personasAntiguas.Add(p);
            }
            CmbFusion.ItemsSource = personasAntiguas.OrderBy(p => (p.Nombre ?? "").Replace("(", "").Replace(")", "").Trim()).ToList();
            CmbFusion.DisplayMemberPath = "Nombre";
            CmbFusion.SelectedValuePath = "Id";

            // CARGA ACTIVIDAD (Si el ID es 0 devolverá vacío y rellenará la tabla de huecos limpios)
            var historialActividad = DatabaseHelper.ObtenerActividadPersona(_personaId);
            var listaActividadUI = new List<ActividadFilaEdit>();
            DateTime iterador = DateTime.Today.AddMonths(1);

            for (int i = 0; i < 24; i++)
            {
                DateTime iter = iterador.AddMonths(-i);
                string mBuscado = nombresMesesJson[iter.Month - 1];
                string aServ = (iter.Month >= 9) ? (iter.Year + 1).ToString() : iter.Year.ToString();

                var reg = historialActividad.FirstOrDefault(x => x.Mes.ToLower() == mBuscado && x.AnioServicio == aServ);

                listaActividadUI.Add(new ActividadFilaEdit
                {
                    Mes = mBuscado,
                    AnioServicio = aServ,
                    NombreMesVisual = char.ToUpper(mBuscado[0]) + mBuscado.Substring(1) + " " + iter.Year.ToString(),
                    Horas = reg?.Horas ?? "",
                    Participacion = reg?.Participacion ?? false,
                    Cursos = reg?.CursosBiblicos ?? "",
                    PrecursorAuxiliar = reg?.PrecursorAuxiliar ?? false,
                    Notas = reg?.Notas ?? ""
                });
            }

            GridActividad.ItemsSource = listaActividadUI;
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            string nuevoNombre = TxtNombre.Text.Trim();
            if (string.IsNullOrWhiteSpace(nuevoNombre)) { MessageBox.Show("El nombre no puede estar vacío.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            int grupo = 0; int.TryParse(TxtGrupo.Text, out grupo);
            string sexoFinal = CmbSexo.SelectedItem is ComboBoxItem itemSexo ? itemSexo.Content.ToString() : "";
            string nombramientoFinal = CmbNombramiento.Text == "Ninguno" ? "" : CmbNombramiento.Text;
            string servicioFinal = CmbServicio.Text == "Ninguno" ? "" : CmbServicio.Text;
            string nombreEditadaOriginal = _personaActual.Nombre;
            string fechaNacimientoFinal = DpNacimiento.SelectedDate.HasValue ? DpNacimiento.SelectedDate.Value.ToString("dd/MM/yyyy") : "";
            string fechaBautismoFinal = DpBautismo.SelectedDate.HasValue ? DpBautismo.SelectedDate.Value.ToString("dd/MM/yyyy") : "";
            string jwFinal = (CmbNombreJW.SelectedItem != null && CmbNombreJW.SelectedItem.ToString() != "-- Sin vincular --") ? CmbNombreJW.SelectedItem.ToString() : "";

            int idPersonaGuardada = _personaId;

            // ==========================================
            // LÓGICA DE NUEVA PERSONA
            // ==========================================
            if (_personaId == 0)
            {
                _personaActual.Nombre = nuevoNombre;
                _personaActual.Grupo = grupo;
                _personaActual.FechaNacimiento = fechaNacimientoFinal;
                _personaActual.FechaBautismo = fechaBautismoFinal;
                _personaActual.Sexo = sexoFinal;
                _personaActual.NombreJWHub = jwFinal;
                _personaActual.Nombramiento = nombramientoFinal;
                _personaActual.Servicio = servicioFinal;
                _personaActual.FechaCreacion = DateTime.Today.ToString("dd/MM/yyyy");

                idPersonaGuardada = DatabaseHelper.InsertarPersonaNueva(_personaActual);
            }
            else if (CmbFusion.SelectedItem != null && CmbFusion.Visibility == Visibility.Visible)
            {
                Persona personaAntigua = CmbFusion.SelectedItem as Persona;
                int idSobreviviente = personaAntigua.Id;
                int idABorrar = _personaId;
                idPersonaGuardada = idSobreviviente;

                DatabaseHelper.FusionarPersonas(idSobreviviente, idABorrar, nuevoNombre, grupo, _congreCodigo, nombreEditadaOriginal, personaAntigua.Nombre);

                personaAntigua.Nombre = nuevoNombre; personaAntigua.Grupo = grupo; personaAntigua.FechaNacimiento = fechaNacimientoFinal;
                personaAntigua.FechaBautismo = fechaBautismoFinal; personaAntigua.Sexo = sexoFinal; personaAntigua.NombreJWHub = jwFinal;

                // Aplicamos los campos a la cuenta antigua (que ahora es la principal)
                personaAntigua.Nombramiento = nombramientoFinal;
                personaAntigua.Servicio = servicioFinal;

                DatabaseHelper.ActualizarPersona(personaAntigua);

                MessageBox.Show("Las cuentas se han fusionado correctamente.", "Fusión Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _personaActual.Nombre = nuevoNombre; _personaActual.Grupo = grupo; _personaActual.FechaNacimiento = fechaNacimientoFinal;
                _personaActual.FechaBautismo = fechaBautismoFinal; _personaActual.Sexo = sexoFinal; _personaActual.NombreJWHub = jwFinal;

                // Aplicamos los campos a la cuenta normal
                _personaActual.Nombramiento = nombramientoFinal;
                _personaActual.Servicio = servicioFinal;

                DatabaseHelper.ActualizarPersona(_personaActual);

                if (nuevoNombre != nombreEditadaOriginal) DatabaseHelper.ActualizarNombreEnTerritorios(_congreCodigo, nombreEditadaOriginal, nuevoNombre);
            }

            // GUARDAR LOS CAMBIOS DE LA PESTAÑA ACTIVIDAD
            if (GridActividad.ItemsSource is List<ActividadFilaEdit> listaAct)
            {
                foreach (var act in listaAct)
                {
                    DatabaseHelper.GuardarActividadMensualEditada(idPersonaGuardada, act.Mes, act.AnioServicio, act.Horas, act.Participacion, act.Cursos, act.PrecursorAuxiliar, act.Notas);
                }
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Clase auxiliar para manejar la interfaz del DataGrid
        public class ActividadFilaEdit
        {
            public string Mes { get; set; }
            public string AnioServicio { get; set; }
            public string NombreMesVisual { get; set; }
            public string Horas { get; set; }
            public bool Participacion { get; set; }
            public string Cursos { get; set; }
            public bool PrecursorAuxiliar { get; set; }
            public string Notas { get; set; }
        }
    }
}