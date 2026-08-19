using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.ComponentModel;

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

            // Filtramos las personas que ya tienen una vinculación confirmada con JW Hub
            var dbVinculadas = _personasDb.Where(p => !string.IsNullOrWhiteSpace(p.NombreJWHub)).ToList();

            foreach (var prec in personasExtraidas)
            {
                var fila = new FilaEmparejamiento
                {
                    PersonaExtraida = prec,
                    CargosLabel = $"{prec.Nombramiento} {prec.Servicio}".Trim(),
                    Actualizar = true // Por defecto marcamos que se procese
                };

                // Preparar el buscador inteligente para sugerencias difusas (por similitud)
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

                var listaOpciones = new List<OpcionEmparejamiento> { opcionNueva };
                listaOpciones.AddRange(opcionesOrdenadas);
                fila.ListaOpciones = listaOpciones;

                // ========================================================
                // LÓGICA DE DESEMPATE DE TOCAYOS (Mismo nombre JW Hub)
                // ========================================================
                var candidatosExactos = dbVinculadas.Where(p => p.NombreJWHub == prec.NombreCompleto).ToList();
                Persona personaExistente = null;

                if (candidatosExactos.Count == 1)
                {
                    personaExistente = candidatosExactos[0]; // Solo hay uno, es él seguro.
                }
                else if (candidatosExactos.Count > 1)
                {
                    // Hay tocayos. Desempatamos buscando que coincida también su congregación, correo o teléfono
                    personaExistente = candidatosExactos.FirstOrDefault(p =>
                        (!string.IsNullOrEmpty(prec.Email) && p.Email == prec.Email) ||
                        (!string.IsNullOrEmpty(prec.Telefono) && p.Telefono == prec.Telefono) ||
                        (!string.IsNullOrEmpty(prec.Congregacion) && p.CongregacionCodigo == prec.Congregacion)
                    );

                    // Si incluso así no sabemos quién es (no tienen email/tlf), coge el primero como rescate
                    if (personaExistente == null) personaExistente = candidatosExactos[0];
                }

                if (personaExistente != null)
                {
                    // Si ya la teníamos, seleccionamos esa persona en el desplegable
                    var opcionEncontrada = listaOpciones.FirstOrDefault(o => o.PersonaDb != null && o.PersonaDb.Id == personaExistente.Id);

                    if (opcionEncontrada != null)
                    {
                        opcionEncontrada.NombreMostrar = $"★ [Vínculo Exacto] {personaExistente.Nombre}";
                        fila.OpcionSeleccionada = opcionEncontrada;
                    }
                    else
                    {
                        fila.OpcionSeleccionada = opcionNueva;
                    }
                }
                else
                {
                    // ES NUEVA (o no vinculada previamente). Sugerimos crear nueva.
                    fila.OpcionSeleccionada = opcionNueva;
                }

                Filas.Add(fila);
            }

            TablaVinculacion.ItemsSource = Filas;
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            int creados = 0, actualizados = 0;

            foreach (var fila in Filas)
            {
                // Si el usuario desmarcó la casilla, ignoramos a esta persona por completo
                if (!fila.Actualizar) continue;

                if (fila.OpcionSeleccionada.PersonaDb == null)
                {
                    // 1. CREAR COMO NUEVA PERSONA
                    string nombreFinal = fila.PersonaExtraida.NombreCompleto;

                    var nuevaPersona = new Persona
                    {
                        Nombre = nombreFinal,
                        NombreComun = fila.PersonaExtraida.NombreCompleto,
                        Servicio = fila.PersonaExtraida.Servicio,
                        Nombramiento = fila.PersonaExtraida.Nombramiento,
                        Telefono = fila.PersonaExtraida.Telefono,
                        Email = fila.PersonaExtraida.Email,
                        EmailJW = fila.PersonaExtraida.EmailJW,
                        Direccion = fila.PersonaExtraida.Direccion,
                        FechaInicioPrec = fila.PersonaExtraida.FechaInicio,
                        FechaUltimaEscuela = fila.PersonaExtraida.FechaUltimaEscuela,
                        NombreJWHub = fila.PersonaExtraida.NombreCompleto,
                        FechaNacimiento = fila.PersonaExtraida.FechaNacimiento,
                        FechaBautismo = fila.PersonaExtraida.FechaBautismo, // Añadido
                        Foto = fila.PersonaExtraida.Foto, // AÑADIDO (La foto ahora sí se transfiere)
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
                    // 2. VINCULAR/ACTUALIZAR A PERSONA EXISTENTE (Ya sea automático o seleccionado a mano)
                    DatabaseHelper.ActualizarDatosPersonaImportada(fila.OpcionSeleccionada.PersonaDb.Id, fila.PersonaExtraida);
                    actualizados++;
                }
            }

            MessageBox.Show($"Proceso finalizado.\n\nNuevos creados: {creados}\nActualizados/Vinculados: {actualizados}", "Resumen", MessageBoxButton.OK, MessageBoxImage.Information);
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public class FilaEmparejamiento : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public PersonaExtraida PersonaExtraida { get; set; }
        public List<OpcionEmparejamiento> ListaOpciones { get; set; }
        public OpcionEmparejamiento OpcionSeleccionada { get; set; }
        public string CargosLabel { get; set; }

        private bool _actualizar;
        public bool Actualizar
        {
            get { return _actualizar; }
            set { _actualizar = value; OnPropertyChanged(nameof(Actualizar)); }
        }
    }

    public class OpcionEmparejamiento
    {
        public Persona PersonaDb { get; set; }
        public string NombreMostrar { get; set; }
    }
}