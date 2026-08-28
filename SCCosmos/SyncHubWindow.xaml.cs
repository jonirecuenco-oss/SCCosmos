using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class SyncHubWindow : Window
    {
        private List<Persona> _personasHub;

        private List<SyncItemHub> _conflictos = new List<SyncItemHub>();
        private List<SyncItemHub> _nuevos = new List<SyncItemHub>();
        private List<SyncItemHub> _bajas = new List<SyncItemHub>();
        private List<SyncItemHub> _iguales = new List<SyncItemHub>();

        public SyncHubWindow(List<Persona> personasHubDescargadas)
        {
            InitializeComponent();
            _personasHub = personasHubDescargadas;
            AnalizarDatos();
        }

        private void AnalizarDatos()
        {
            var personasDB = DatabaseHelper.ObtenerPersonas()
                .Where(p => string.IsNullOrEmpty(p.FechaEliminacion) || ParsearFechaSegura(p.FechaEliminacion) > DateTime.Now)
                .ToList();

            var procesadosDB = new HashSet<int>();

            foreach (var hubItem in _personasHub)
            {
                // 1. Buscamos a TODOS los que tengan el mismo nombre exacto en esa congregación
                var posiblesMatches = personasDB.Where(p =>
                    p.Nombre == hubItem.Nombre &&
                    p.CongregacionCodigo == hubItem.CongregacionCodigo
                ).ToList();

                Persona matchDB = null;

                if (posiblesMatches.Count == 1)
                {
                    // CASO A: Solo hay uno. Es él con total seguridad (aunque le hayan corregido la fecha)
                    matchDB = posiblesMatches[0];
                }
                else if (posiblesMatches.Count > 1)
                {
                    // CASO B: Hay homónimos (2 o más con el mismo nombre). Desempatamos con la fecha.
                    matchDB = posiblesMatches.FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(p.FechaNacimiento) &&
                        !string.IsNullOrWhiteSpace(hubItem.FechaNacimiento) &&
                        p.FechaNacimiento.Trim() == hubItem.FechaNacimiento.Trim()
                    );

                    // Salvavidas por si ambos homónimos tienen la fecha en blanco en nuestra BD
                    if (matchDB == null) matchDB = posiblesMatches[0];
                }

                if (matchDB != null)
                {
                    procesadosDB.Add(matchDB.Id);

                    if (HayConflicto(matchDB, hubItem))
                    {
                        _conflictos.Add(new SyncItemHub { PersonaDB = matchDB, PersonaHub = hubItem, NombreVisual = matchDB.NombreComun, Descripcion = "Se han detectado cambios en uno o más campos." });
                    }
                    else
                    {
                        _iguales.Add(new SyncItemHub { PersonaDB = matchDB, PersonaHub = hubItem, NombreVisual = matchDB.NombreComun, Descripcion = "Coinciden todos los datos." });
                    }
                }
                else
                {
                    _nuevos.Add(new SyncItemHub { PersonaDB = null, PersonaHub = hubItem, NombreVisual = hubItem.Nombre, Descripcion = "Aparece en JW Hub pero no está en la Base de Datos." });
                }
            }

            foreach (var dbItem in personasDB)
            {
                if (!procesadosDB.Contains(dbItem.Id))
                {
                    _bajas.Add(new SyncItemHub { PersonaDB = dbItem, PersonaHub = null, NombreVisual = dbItem.NombreComun, Descripcion = "Ya no aparece en JW Hub. ¿Se ha dado de baja o mudado?" });
                }
            }

            ActualizarListasUI();
        }

        private void ActualizarListasUI()
        {
            GridConflictos.ItemsSource = null; GridConflictos.ItemsSource = _conflictos;
            GridNuevos.ItemsSource = null; GridNuevos.ItemsSource = _nuevos;
            GridBajas.ItemsSource = null; GridBajas.ItemsSource = _bajas;
            GridIguales.ItemsSource = null; GridIguales.ItemsSource = _iguales;

            TxtNumConflictos.Text = _conflictos.Count.ToString();
            TxtNumNuevos.Text = _nuevos.Count.ToString();
            TxtNumBajas.Text = _bajas.Count.ToString();
            TxtNumIguales.Text = _iguales.Count.ToString();
        }

        private bool HayConflicto(Persona db, Persona hub)
        {
            if (!string.IsNullOrEmpty(hub.NombreJWHub) && db.NombreJWHub != hub.NombreJWHub) return true;
            if (!string.IsNullOrEmpty(hub.NombreComun) && db.NombreComun != hub.NombreComun) return true;
            if (!string.IsNullOrEmpty(hub.Nombre) && db.Nombre != hub.Nombre) return true;
            if (!string.IsNullOrEmpty(hub.Sexo) && db.Sexo != hub.Sexo) return true;
            if (!string.IsNullOrEmpty(hub.Esperanza) && db.Esperanza != hub.Esperanza) return true;
            if (!string.IsNullOrEmpty(hub.Nombramiento) && db.Nombramiento != hub.Nombramiento) return true;
            if (!string.IsNullOrEmpty(hub.Servicio) && db.Servicio != hub.Servicio) return true;

            if (!string.IsNullOrEmpty(hub.FechaNacimiento) && db.FechaNacimiento != hub.FechaNacimiento) return true;
            if (!string.IsNullOrEmpty(hub.FechaBautismo) && db.FechaBautismo != hub.FechaBautismo) return true;
            if (!string.IsNullOrEmpty(hub.FechaInicioPrec) && db.FechaInicioPrec != hub.FechaInicioPrec) return true;
            if (!string.IsNullOrEmpty(hub.FechaUltimaEscuela) && db.FechaUltimaEscuela != hub.FechaUltimaEscuela) return true;

            if (!string.IsNullOrEmpty(hub.Email) && db.Email != hub.Email) return true;
            if (!string.IsNullOrEmpty(hub.EmailJW) && db.EmailJW != hub.EmailJW) return true;
            if (!string.IsNullOrEmpty(hub.Telefono) && db.Telefono != hub.Telefono) return true;
            if (!string.IsNullOrEmpty(hub.Direccion) && db.Direccion != hub.Direccion) return true;
            if (!string.IsNullOrEmpty(hub.Foto) && db.Foto != hub.Foto) return true;

            if (!string.IsNullOrEmpty(hub.Privilegios) && (string.IsNullOrEmpty(db.Privilegios) || !db.Privilegios.Contains(hub.Privilegios))) return true;

            string hubSols = hub.Solicitudes ?? "";
            string dbSols = db.Solicitudes ?? "";

            var listHub = hubSols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).OrderBy(s => s).ToList();
            var listDb = dbSols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).OrderBy(s => s).ToList();

            if (!listHub.SequenceEqual(listDb)) return true;

            return false;
        }

        private void BtnResolverConflicto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItemHub item)
            {

                var ventana = new FormularioPersonaWindow(item.PersonaDB);
                ventana.Owner = Window.GetWindow(this);

                ventana.CargarConflictosDesdeHub(item.PersonaHub);

                if (ventana.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    _conflictos.Remove(item);
                    item.Descripcion = "Resuelto manualmente.";
                    _iguales.Add(item);
                    ActualizarListasUI();
                }
            }
        }

        private void BtnAnadirNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItemHub item)
            {
                int nuevoId = DatabaseHelper.InsertarPersonaNueva(item.PersonaHub);
                item.PersonaHub.Id = nuevoId;

                var ventana = new FormularioPersonaWindow(item.PersonaHub);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    _nuevos.Remove(item);
                    item.Descripcion = "Añadido correctamente.";
                    _iguales.Add(item);
                    ActualizarListasUI();
                }
                else DatabaseHelper.EliminarPersona(nuevoId);
            }
        }

        private void BtnDarBaja_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItemHub item)
            {
                if (MessageBox.Show($"¿Estás seguro de que deseas dar de baja a {item.NombreVisual}?\nNo se borrará su historial, pero dejará de aparecer en las listas actuales.", "Confirmar Baja", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    item.PersonaDB.FechaEliminacion = DateTime.Now.ToString("dd/MM/yyyy");
                    DatabaseHelper.ActualizarPersona(item.PersonaDB);
                    _bajas.Remove(item);
                    ActualizarListasUI();
                }
            }
        }

        private void BtnRevisarIgual_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItemHub item)
            {
                var ventana = new FormularioPersonaWindow(item.PersonaDB);
                ventana.Owner = Window.GetWindow(this);
                if (ventana.ShowDialog() == true) DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
            }
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            // Iteramos sobre todos los "Nuevos" que aún no se hayan añadido manualmente a la BD
            foreach (var itemNuevo in _nuevos)
            {
                DatabaseHelper.InsertarPersonaNueva(itemNuevo.PersonaHub);
            }

            // Una vez añadidos todos, cerramos la ventana
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }

        private DateTime? ParsearFechaSegura(string fechaStr)
        {
            if (string.IsNullOrWhiteSpace(fechaStr)) return null;
            if (DateTime.TryParse(fechaStr, out DateTime dt)) return dt;
            return null;
        }

        public class SyncItemHub
        {
            public Persona PersonaDB { get; set; }
            public Persona PersonaHub { get; set; }
            public string NombreVisual { get; set; }
            public string Descripcion { get; set; }
        }
    }
}