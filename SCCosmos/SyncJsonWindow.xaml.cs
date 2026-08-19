using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class SyncJsonWindow : Window
    {
        private string _congreCodigo;
        private string _mesReferencia; // Añadido para el control temporal (Viaje en el tiempo)
        private List<PersonaJson> _listaJson;
        private List<TerritorioJSON> _listaTerritorios;

        private List<SyncItem> _conflictos = new List<SyncItem>();
        private List<SyncItem> _nuevos = new List<SyncItem>();
        private List<SyncItem> _bajas = new List<SyncItem>();
        private List<SyncItem> _iguales = new List<SyncItem>();

        // Ahora pide el mes de referencia, igual que SyncHubWindow
        public SyncJsonWindow(string congreCodigo, List<PersonaJson> listaPersonas, List<TerritorioJSON> listaTerritorios, string mesReferencia = null)
        {
            InitializeComponent();
            _congreCodigo = congreCodigo;
            _listaJson = listaPersonas;
            _listaTerritorios = listaTerritorios;
            _mesReferencia = mesReferencia ?? DateTime.Now.ToString("yyyy-MM");

            AnalizarDatos();
        }

        private void AnalizarDatos()
        {
            // 1. Obtenemos todos los publicadores ACTIVOS de la BD (Solo las Raíces actuales, no los históricos)
            var personasDB = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo)
                .Where(p => string.IsNullOrEmpty(p.FechaEliminacion) || ParsearFechaSegura(p.FechaEliminacion) > DateTime.Now)
                .ToList();

            var procesadosDB = new HashSet<int>();

            foreach (var jsonItem in _listaJson)
            {
                // Convierte el JSON en un objeto Persona "falso" para usarlo luego en la fusión
                Persona personaDesdeJson = ConvertirJsonAPersona(jsonItem);

                // Buscamos coincidencia (Primero por JW Hub, luego por Nombre Completo)
                var matchDB = personasDB.FirstOrDefault(p =>
                    (!string.IsNullOrEmpty(p.NombreJWHub) && p.NombreJWHub == personaDesdeJson.NombreJWHub) ||
                    p.Nombre == personaDesdeJson.Nombre);

                if (matchDB != null)
                {
                    procesadosDB.Add(matchDB.Id);

                    // Comprobamos si hay algún dato distinto (Conflicto) o si son idénticos
                    if (HayConflicto(matchDB, personaDesdeJson))
                    {
                        _conflictos.Add(new SyncItem { PersonaDB = matchDB, PersonaFakeJSON = personaDesdeJson, NombreVisual = matchDB.NombreComun, Descripcion = "Algunos datos del JSON no coinciden con la BD." });
                    }
                    else
                    {
                        _iguales.Add(new SyncItem { PersonaDB = matchDB, PersonaFakeJSON = personaDesdeJson, NombreVisual = matchDB.NombreComun, Descripcion = "Ficha idéntica. Se actualizará la actividad en segundo plano." });
                    }
                }
                else
                {
                    // No está en la BD -> Es NUEVO
                    _nuevos.Add(new SyncItem { PersonaDB = null, PersonaFakeJSON = personaDesdeJson, NombreVisual = personaDesdeJson.NombreJWHub, Descripcion = "Aparece en el JSON pero no está en el programa." });
                }
            }

            // Los que están en la BD pero no han sido procesados por el JSON -> BAJAS
            foreach (var dbItem in personasDB)
            {
                if (!procesadosDB.Contains(dbItem.Id))
                {
                    _bajas.Add(new SyncItem { PersonaDB = dbItem, PersonaFakeJSON = null, NombreVisual = dbItem.NombreComun, Descripcion = "No aparece en el JSON. ¿Se ha mudado o borrado?" });
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

        // ========================================================
        // MAGIA DE CONVERSIÓN Y COMPARACIÓN
        // ========================================================

        private Persona ConvertirJsonAPersona(PersonaJson json)
        {
            return new Persona
            {
                CongregacionCodigo = _congreCodigo,
                Nombre = json.Nombre,        // Nombre completo oficial
                NombreJWHub = json.Nombre,   // El JW Hub será el oficial del JSON
                NombreComun = json.Nombre,   // Por defecto igual, por si es un nuevo publicador
                Sexo = json.Sexo,
                Esperanza = json.Esperanza,
                Nombramiento = json.Nombramiento,
                Servicio = json.Servicio,
                FechaNacimiento = json.FechaNacimiento,
                FechaBautismo = json.FechaBautismo,
                Grupo = json.Grupo             // AÑADIDO: ¡Ahora también importamos el grupo!
            };
        }

        private bool HayConflicto(Persona db, Persona json)
        {
            // Textos (Ignoramos mayúsculas y minúsculas por si acaso)
            if (!string.IsNullOrEmpty(json.NombreJWHub) && !string.Equals(db.NombreJWHub, json.NombreJWHub, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(json.Sexo) && !string.Equals(db.Sexo, json.Sexo, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(json.Esperanza) && !string.Equals(db.Esperanza, json.Esperanza, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(json.Nombramiento) && !string.Equals(db.Nombramiento, json.Nombramiento, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(json.Servicio) && !string.Equals(db.Servicio, json.Servicio, StringComparison.OrdinalIgnoreCase)) return true;

            // Fechas
            if (!string.IsNullOrEmpty(json.FechaNacimiento) && db.FechaNacimiento != json.FechaNacimiento) return true;
            if (!string.IsNullOrEmpty(json.FechaBautismo) && db.FechaBautismo != json.FechaBautismo) return true;

            // Grupo (Solo salta conflicto si el JSON dice que pertenece a un grupo > 0 y en la BD no coincide)
            if (json.Grupo > 0 && db.Grupo != json.Grupo) return true;

            return false;
        }

        // ========================================================
        // BOTONES DE ACCIÓN (ABREN EL FORMULARIO MÁGICO)
        // ========================================================

        private void BtnResolverConflicto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItem item)
            {
                // Le mandamos al Formulario nuestra PersonaDB actual, pero le decimos que el "candidato" a fusionar es la PersonaJSON
                var listaFusionJson = new List<Persona> { item.PersonaFakeJSON };
                var ventana = new FormularioPersonaWindow(item.PersonaDB, listaFusionJson);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    // Llama al motor histórico para clonar la persona si es necesario en lugar de sobrescribirla
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada, _mesReferencia);

                    // Si se resuelve, lo quitamos de conflictos y lo pasamos a iguales
                    _conflictos.Remove(item);
                    item.Descripcion = "Resuelto manualmente.";
                    _iguales.Add(item);
                    ActualizarListasUI();
                }
            }
        }

        private void BtnAnadirNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItem item)
            {
                // Como es nuevo, le pasamos la PersonaJson vacía para que rellene los datos, no fusionamos con nadie
                int nuevoId = DatabaseHelper.InsertarPersonaNueva(item.PersonaFakeJSON);
                item.PersonaFakeJSON.Id = nuevoId;

                var ventana = new FormularioPersonaWindow(item.PersonaFakeJSON);
                ventana.Owner = Window.GetWindow(this);

                if (ventana.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada, _mesReferencia);
                    _nuevos.Remove(item);
                    item.Descripcion = "Añadido correctamente.";
                    _iguales.Add(item);
                    ActualizarListasUI();
                }
                else DatabaseHelper.EliminarPersona(nuevoId); // Rollback si cancela
            }
        }

        private void BtnDarBaja_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItem item)
            {
                if (MessageBox.Show($"¿Estás seguro de que deseas dar de baja a {item.NombreVisual}?\nNo se borrará su historial, pero dejará de aparecer en las listas actuales.", "Confirmar Baja", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    // SOFT DELETE: Le ponemos la fecha de hoy como eliminación
                    DatabaseHelper.MarcarPersonaEliminada(item.PersonaDB.Id);

                    _bajas.Remove(item);
                    ActualizarListasUI();
                }
            }
        }

        private void BtnRevisarIgual_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItem item)
            {
                var ventana = new FormularioPersonaWindow(item.PersonaDB);
                ventana.Owner = Window.GetWindow(this);
                if (ventana.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada, _mesReferencia);
                }
            }
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            if (_conflictos.Count > 0 || _nuevos.Count > 0)
            {
                if (MessageBox.Show("Aún tienes Conflictos o Personas Nuevas sin revisar. Si continúas, se ignorarán y no se añadirán a tu base de datos.\n\n¿Deseas finalizar de todos modos?", "Atención", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    return;
                }
            }

            // AHORA USA EL CLONADOR MASIVO PARA GUARDAR EL JSON
            DatabaseHelper.GuardarVersionHistoricaJSON(_congreCodigo, _mesReferencia, _listaJson);

            // GUARDA LOS TERRITORIOS SI EXISTEN
            if (_listaTerritorios != null) DatabaseHelper.GuardarTerritoriosImportados(_congreCodigo, _listaTerritorios);

            MessageBox.Show("Sincronización finalizada correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private DateTime? ParsearFechaSegura(string fechaStr)
        {
            if (string.IsNullOrWhiteSpace(fechaStr)) return null;
            if (DateTime.TryParse(fechaStr, out DateTime dt)) return dt;
            return null;
        }

        public class SyncItem
        {
            public Persona PersonaDB { get; set; }
            public Persona PersonaFakeJSON { get; set; }
            public string NombreVisual { get; set; }
            public string Descripcion { get; set; }
        }
    }
}