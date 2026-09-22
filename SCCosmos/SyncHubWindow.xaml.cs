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
                // 1. Buscamos a TODOS los que tengan el mismo nombre en esa congregación
                var posiblesMatches = personasDB.Where(p =>
                    (string.Equals(p.Nombre, hubItem.Nombre, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p.NombreJWHub, hubItem.Nombre, StringComparison.OrdinalIgnoreCase)) &&
                    p.CongregacionCodigo == hubItem.CongregacionCodigo
                ).ToList();

                Persona matchDB = null;

                if (posiblesMatches.Count > 0)
                {
                    // 2. FILTRO ANTI-HOMÓNIMOS EN LA INTERFAZ
                    matchDB = posiblesMatches.FirstOrDefault(p =>
                    {
                        Func<string, string, bool> SonIncompatibles = (dbDate, hubDate) =>
                        {
                            if (string.IsNullOrWhiteSpace(dbDate) || string.IsNullOrWhiteSpace(hubDate)) return false;

                            bool dbAprox = dbDate.StartsWith("01/01/");
                            bool hubAprox = hubDate.StartsWith("01/01/");

                            if (!dbAprox && !hubAprox) return dbDate.Trim() != hubDate.Trim();

                            int yearDB = 0, yearHub = 0;
                            if (dbDate.Length >= 4) int.TryParse(dbDate.Substring(dbDate.Length - 4), out yearDB);
                            if (hubDate.Length >= 4) int.TryParse(hubDate.Substring(hubDate.Length - 4), out yearHub);

                            if (yearDB > 0 && yearHub > 0 && Math.Abs(yearDB - yearHub) > 2) return true;
                            return false;
                        };

                        if (SonIncompatibles(p.FechaNacimiento, hubItem.FechaNacimiento)) return false;
                        if (SonIncompatibles(p.FechaBautismo, hubItem.FechaBautismo)) return false;

                        return true;
                    });
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
                    // Si no hubo Match entra aquí como NUEVO.
                    _nuevos.Add(new SyncItemHub { PersonaDB = null, PersonaHub = hubItem, NombreVisual = hubItem.Nombre, Descripcion = "Aparece en JW Hub pero no está en la Base de Datos." });
                }
            }

            foreach (var dbItem in personasDB)
            {
                // Solo se consideran posibles bajas los registros que alguna vez se hayan
                // confirmado como procedentes de JW Hub. Los creados únicamente por S-21
                // no deben aparecer aquí.
                if (!procesadosDB.Contains(dbItem.Id) && dbItem.TieneOrigenJWHub)
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
            // NombreJWHub sí pertenece a Hub y puede actualizarse.
            if (!string.IsNullOrEmpty(hub.NombreJWHub) && db.NombreJWHub != hub.NombreJWHub) return true;

            // Nombre completo y nombre común ya rellenos se consideran consolidados.
            // Hub únicamente los aporta cuando el campo de BBDD está vacío.
            if (string.IsNullOrWhiteSpace(db.Nombre) && !string.IsNullOrWhiteSpace(hub.Nombre)) return true;
            if (string.IsNullOrWhiteSpace(db.NombreComun) && !string.IsNullOrWhiteSpace(hub.NombreComun)) return true;

            // Grupo y Sexo se ignoran completamente en la sincronización con JW Hub.

            string hubEsp = hub.Esperanza ?? "";
            string dbEsp = db.Esperanza ?? "";
            if (hubEsp != "" && dbEsp != hubEsp) return true;

            // DETECCIÓN DE PÉRDIDAS (Nombramientos y Servicios)
            // Al quitar el "IsNullOrEmpty", obligamos a que detecte si el Hub viene vacío y la BBDD no.
            string hubNomb = hub.Nombramiento ?? "";
            string dbNomb = db.Nombramiento ?? "";
            if (hubNomb != dbNomb) return true;

            string hubServ = hub.Servicio ?? "";
            string dbServ = db.Servicio ?? "";
            if (hubServ != dbServ) return true;

            // PRIVILEGIOS: cualquier diferencia debe llegar al formulario de resolución.
            // Antes solo se comprobaba un subconjunto y podían pasar cambios sin marcarse.
            var hubPrivs = (hub.Privilegios ?? "")
                .Replace("\r", "")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var dbPrivs = (db.Privilegios ?? "")
                .Replace("\r", "")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (!hubPrivs.SequenceEqual(dbPrivs, StringComparer.Ordinal)) return true;

            // Nacimiento y bautismo importados desde S-21/JSON son definitivos.
            // Si no hay origen S21, seguimos ignorando fechas aproximadas 01/01/YYYY.
            if (!db.TieneOrigenS21)
            {
                if (!string.IsNullOrEmpty(hub.FechaNacimiento) && db.FechaNacimiento != hub.FechaNacimiento && !hub.FechaNacimiento.StartsWith("01/01/")) return true;
                if (!string.IsNullOrEmpty(hub.FechaBautismo) && db.FechaBautismo != hub.FechaBautismo && !hub.FechaBautismo.StartsWith("01/01/")) return true;
            }

            if (!string.IsNullOrEmpty(hub.FechaInicioPrec) && db.FechaInicioPrec != hub.FechaInicioPrec && !hub.FechaInicioPrec.StartsWith("01/01/")) return true;

            if (!string.IsNullOrEmpty(hub.FechaUltimaEscuela) && db.FechaUltimaEscuela != hub.FechaUltimaEscuela) return true;

            if (!string.IsNullOrEmpty(hub.Email) && db.Email != hub.Email) return true;
            if (!string.IsNullOrEmpty(hub.EmailJW) && db.EmailJW != hub.EmailJW) return true;
            if (!string.IsNullOrEmpty(hub.Telefono) && db.Telefono != hub.Telefono) return true;
            if (!string.IsNullOrEmpty(hub.Direccion) && db.Direccion != hub.Direccion) return true;
            if (!string.IsNullOrEmpty(hub.Foto) && db.Foto != hub.Foto) return true;

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
                    ventana.PersonaActualizada.OrigenRegistro = DatabaseHelper.CombinarOrigenes(ventana.PersonaActualizada.OrigenRegistro, "JWHub");
                    DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    DatabaseHelper.MarcarOrigenPersona(ventana.PersonaActualizada.Id, "JWHub");
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
                item.PersonaHub.OrigenRegistro = DatabaseHelper.CombinarOrigenes(item.PersonaHub.OrigenRegistro, "JWHub");
                int nuevoId = DatabaseHelper.InsertarPersonaNueva(item.PersonaHub);
                item.PersonaHub.Id = nuevoId;

                var ventana = new FormularioPersonaWindow(item.PersonaHub);
                ventana.Owner = Window.GetWindow(this);
                ventana.ConfigurarOrigenSincronizacion("JWHub");

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
                if (MessageBox.Show($"¿Estás seguro de que deseas dar de baja a {item.NombreVisual}?\n\nSe marcará su fecha de baja para ocultarlo de las listas actuales, pero conservará todos sus datos e historial intactos en la base de datos.", "Confirmar Baja", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    // 1. Soft Delete real: Solo marcamos la fecha de eliminación oficial
                    item.PersonaDB.FechaEliminacion = DateTime.Now.ToString("dd/MM/yyyy");

                    // 2. Guardamos los cambios sin borrar sus privilegios ni congregación
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
                ventana.ConfigurarOrigenSincronizacion("JWHub");
                if (ventana.ShowDialog() == true)
                {
                    ventana.PersonaActualizada.OrigenRegistro = DatabaseHelper.CombinarOrigenes(ventana.PersonaActualizada.OrigenRegistro, "JWHub");
                    DatabaseHelper.ActualizarPersona(ventana.PersonaActualizada);
                    DatabaseHelper.MarcarOrigenPersona(ventana.PersonaActualizada.Id, "JWHub");
                }
            }
        }

        // ==========================================
        // FUNCION: DESCARTAR ITEM
        // ==========================================
        private void BtnDescartar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItemHub item)
            {
                // Buscamos en qué lista está y lo borramos
                if (_conflictos.Contains(item)) _conflictos.Remove(item);
                else if (_nuevos.Contains(item)) _nuevos.Remove(item);
                else if (_bajas.Contains(item)) _bajas.Remove(item);
                else if (_iguales.Contains(item)) _iguales.Remove(item);

                ActualizarListasUI();
            }
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            // Los registros que sí hemos visto en JW Hub quedan vinculados a ese origen.
            // Esto permite que una sincronización futura solo proponga como baja a personas
            // que realmente habían sido confirmadas previamente en JW Hub.
            foreach (var item in _iguales)
            {
                if (item.PersonaDB != null) DatabaseHelper.MarcarOrigenPersona(item.PersonaDB.Id, "JWHub");
            }

            foreach (var item in _conflictos)
            {
                if (item.PersonaDB != null) DatabaseHelper.MarcarOrigenPersona(item.PersonaDB.Id, "JWHub");
            }

            // Iteramos sobre todos los "Nuevos" que aún no se hayan añadido manualmente a la BD.
            foreach (var itemNuevo in _nuevos)
            {
                itemNuevo.PersonaHub.OrigenRegistro = DatabaseHelper.CombinarOrigenes(itemNuevo.PersonaHub.OrigenRegistro, "JWHub");
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