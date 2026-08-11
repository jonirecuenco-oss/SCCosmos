using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SCCosmos
{
    public partial class EmparejarPersonasWindow : Window
    {
        public List<FilaEmparejamiento> Filas { get; set; }
        private List<Persona> _personasDb;

        public EmparejarPersonasWindow(List<PersonaExtraida> personasExtraidas)
        {
            InitializeComponent();
            _personasDb = DatabaseHelper.ObtenerPersonas();
            Filas = new List<FilaEmparejamiento>();

            var opcionNueva = new OpcionEmparejamiento { PersonaDb = null, NombreMostrar = "-- CREAR COMO NUEVA PERSONA --" };

            foreach (var prec in personasExtraidas)
            {
                string nombreFlipped = prec.NombreCompleto;
                if (nombreFlipped.Contains(","))
                {
                    var partes = nombreFlipped.Split(',');
                    nombreFlipped = partes[1].Trim() + " " + partes[0].Trim();
                }

                var opcionesOrdenadas = _personasDb
                    .Select(p => new { Persona = p, Similitud = DatabaseHelper.CalcularSimilitud(nombreFlipped, p.Nombre) })
                    .OrderByDescending(x => x.Similitud)
                    .Select(x => new OpcionEmparejamiento
                    {
                        PersonaDb = x.Persona,
                        NombreMostrar = x.Similitud > 40 ? $"[Coincidencia {x.Similitud}%] {x.Persona.Nombre}" : x.Persona.Nombre
                    }).ToList();

                var listaOpciones = new List<OpcionEmparejamiento>();
                listaOpciones.Add(opcionNueva);
                listaOpciones.AddRange(opcionesOrdenadas);

                Filas.Add(new FilaEmparejamiento
                {
                    PersonaExtraida = prec,
                    ListaOpciones = listaOpciones,
                    OpcionSeleccionada = opcionNueva,
                    CargosLabel = $"{prec.Nombramiento} {prec.Servicio}".Trim()
                });
            }
            TablaVinculacion.ItemsSource = Filas;
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            int creados = 0, actualizados = 0;
            foreach (var fila in Filas)
            {
                if (fila.OpcionSeleccionada.PersonaDb == null)
                {
                    // Guardamos el nombre TAL CUAL viene de la web
                    string nombreFinal = fila.PersonaExtraida.NombreCompleto;

                    var nuevaPersona = new Persona
                    {
                        Nombre = nombreFinal,
                        Servicio = fila.PersonaExtraida.Servicio,
                        Nombramiento = fila.PersonaExtraida.Nombramiento,
                        Telefono = fila.PersonaExtraida.Telefono,
                        Email = fila.PersonaExtraida.Email,
                        FechaInicioPrec = fila.PersonaExtraida.FechaInicio,
                        FechaUltimaEscuela = fila.PersonaExtraida.FechaUltimaEscuela,
                        NombreJWHub = fila.PersonaExtraida.NombreCompleto,
                        FechaCreacion = DateTime.Today.ToString("dd/MM/yyyy"),
                        FechaEliminacion = "",
                        Solicitudes = fila.PersonaExtraida.Solicitudes,
                        Privilegios = fila.PersonaExtraida.Privilegios
                    };
                    DatabaseHelper.InsertarPersonaImportada(nuevaPersona, fila.PersonaExtraida.Congregacion);
                    creados++;
                }
                else
                {
                    DatabaseHelper.ActualizarDatosPersonaImportada(fila.OpcionSeleccionada.PersonaDb.Id, fila.PersonaExtraida);
                    actualizados++;
                }
            }

            MessageBox.Show($"Proceso finalizado.\n\nNuevos creados: {creados}\nVinculados: {actualizados}", "Resumen", MessageBoxButton.OK, MessageBoxImage.Information);
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }

    public class FilaEmparejamiento
    {
        public PersonaExtraida PersonaExtraida { get; set; }
        public List<OpcionEmparejamiento> ListaOpciones { get; set; }
        public OpcionEmparejamiento OpcionSeleccionada { get; set; }
        public string CargosLabel { get; set; }
    }

    public class OpcionEmparejamiento
    {
        public Persona PersonaDb { get; set; }
        public string NombreMostrar { get; set; }
    }
}