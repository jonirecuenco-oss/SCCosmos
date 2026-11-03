using System;
using System.Collections.Generic;
using System.Globalization;
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

        // Por el nombre de la tarjeta: la ficha que le corresponde y las que el usuario ya ha decidido.
        private readonly Dictionary<string, int> _fichaPorTarjeta = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _fichasDecididas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _listaJson = listaPersonas ?? new List<PersonaJson>();
            _listaTerritorios = listaTerritorios;
            _mesReferencia = mesReferencia ?? DateTime.Now.ToString("yyyy-MM");

            // Normalizamos las fechas del S-21 una sola vez al entrar. De este modo
            // tanto la comparación como cualquier guardado posterior usan dd/MM/yyyy.
            foreach (var personaJson in _listaJson)
            {
                if (personaJson == null) continue;
                personaJson.FechaNacimiento = FormatearFechaS21(personaJson.FechaNacimiento);
                personaJson.FechaBautismo = FormatearFechaS21(personaJson.FechaBautismo);
            }

            AnalizarDatos();
        }

        // Fichas que ya existían antes de esta importación. Son las únicas
        // que se ofrecen para vincular: las que se van añadiendo desde el
        // JSON en esta misma sesión no deben aparecer como candidatas.
        private List<Persona> _personasExistentes = new List<Persona>();

        private void AnalizarDatos()
        {
            // 1. Obtenemos todos los publicadores ACTIVOS de la BD (Solo las Raíces actuales, no los históricos)
            var personasDB = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo)
                .Where(p => string.IsNullOrEmpty(p.FechaEliminacion) || ParsearFechaSegura(p.FechaEliminacion) > DateTime.Now)
                .ToList();

            _personasExistentes = personasDB.ToList();

            var procesadosDB = new HashSet<int>();

            // Una persona puede venir en varios años de servicio (una
            // tarjeta por año): la ficha se compara una sola vez y la
            // actividad de cada año se guarda al finalizar.
            var nombresProcesados = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var jsonItem in _listaJson)
            {
                if (jsonItem == null)
                {
                    continue;
                }

                string nombreJson = (jsonItem.Nombre ?? "").Trim();

                if (nombreJson.Length > 0
                    && !nombresProcesados.Add(nombreJson))
                {
                    continue;
                }

                // Convierte el JSON en un objeto Persona "falso" para usarlo luego en la fusión
                Persona personaDesdeJson = ConvertirJsonAPersona(jsonItem);

                // Buscamos coincidencia usando el nombre del S-21 contra los dos campos
                // que normalmente pueden contener el nombre oficial. No dependemos de
                // PersonaFakeJSON.NombreJWHub porque el S-21 no confirma que exista Hub.
                var matchDB = personasDB.FirstOrDefault(p =>
                    string.Equals(p.Nombre, jsonItem.Nombre, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(p.NombreJWHub) &&
                     string.Equals(p.NombreJWHub, jsonItem.Nombre, StringComparison.OrdinalIgnoreCase)))
                    // Mismas palabras en otro orden o sin acentos, si solo hay una ficha así y las fechas encajan.
                    ?? SCCosmos.Sincro.NombresPersona.BuscarUnica(
                        personasDB.Where(p => !procesadosDB.Contains(p.Id)),
                        jsonItem.Nombre,
                        jsonItem.FechaNacimiento,
                        jsonItem.FechaBautismo,
                        p => (p.Nombre, p.NombreJWHub, p.FechaNacimiento, p.FechaBautismo));

                if (matchDB != null)
                {
                    procesadosDB.Add(matchDB.Id);
                    _fichaPorTarjeta[nombreJson] = matchDB.Id;

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
                    _nuevos.Add(new SyncItem { PersonaDB = null, PersonaFakeJSON = personaDesdeJson, NombreVisual = personaDesdeJson.Nombre, Descripcion = "Aparece en el JSON pero no está en el programa." });
                }
            }

            // Los que están en la BD pero no han sido procesados por el JSON -> BAJAS
            foreach (var dbItem in personasDB)
            {
                if (!procesadosDB.Contains(dbItem.Id) && dbItem.TieneOrigenS21)
                {
                    _bajas.Add(new SyncItem { PersonaDB = dbItem, PersonaFakeJSON = null, NombreVisual = dbItem.NombreComun, Descripcion = "No aparece en el JSON S-21. ¿Se ha mudado o borrado?" });
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
                Nombre = json.Nombre,        // Nombre completo oficial del S-21
                NombreJWHub = "",            // El S-21 no confirma que exista un registro en JW Hub
                NombreComun = json.Nombre,   // Por defecto igual, por si es un nuevo publicador
                Sexo = json.Sexo,
                Esperanza = json.Esperanza,
                Nombramiento = json.Nombramiento,
                Servicio = json.Servicio,
                FechaNacimiento = FormatearFechaS21(json.FechaNacimiento),
                FechaBautismo = FormatearFechaS21(json.FechaBautismo),
                Grupo = json.Grupo,            // AÑADIDO: ¡Ahora también importamos el grupo!
                OrigenRegistro = "S21"
            };
        }

        private bool HayConflicto(Persona db, Persona json)
        {
            // NOMBRES: el S-21 es la fuente preferente SOLO para Nombre completo.
            // Nunca usamos el nombre del JSON para sustituir NombreJWHub y tampoco
            // intentamos reemplazar NombreComun, que se prefiere conservar desde JW Hub.
            if (!string.IsNullOrWhiteSpace(json.Nombre))
            {
                string nombreS21 = json.Nombre.Trim();
                if (!string.Equals(db.Nombre?.Trim(), nombreS21, StringComparison.Ordinal)
                    && !SCCosmos.Sincro.NombresPersona.Equivalentes(db.Nombre, nombreS21)) return true;
            }

            // Textos: toda diferencia, incluida solo mayúsculas/minúsculas, es
            // conflicto. Rellenar lo que la ficha no tiene, no: no hay nada que elegir.
            static bool Difiere(string enFicha, string enTarjeta) =>
                !string.IsNullOrEmpty(enTarjeta)
                && !string.IsNullOrWhiteSpace(enFicha)
                && !string.Equals(enFicha, enTarjeta, StringComparison.Ordinal);

            if (Difiere(db.Sexo, json.Sexo)) return true;
            if (Difiere(db.Esperanza, json.Esperanza)) return true;
            if (Difiere(db.Nombramiento, json.Nombramiento)) return true;
            if (Difiere(db.Servicio, json.Servicio)) return true;

            // Fechas
            // La de JW Hub es una aproximación (01/01/año): la exacta del S-21 la sustituye
            // sin preguntar, si el año encaja.
            if (SCCosmos.Sincro.NombresPersona.FechaEnConflicto(db.FechaNacimiento, json.FechaNacimiento)) return true;
            if (SCCosmos.Sincro.NombresPersona.FechaEnConflicto(db.FechaBautismo, json.FechaBautismo)) return true;

            // Grupo (Solo salta conflicto si el JSON dice que pertenece a un grupo > 0 y en la BD no coincide)
            if (json.Grupo > 0 && db.Grupo > 0 && db.Grupo != json.Grupo) return true;

            return false;
        }

        // ========================================================
        // BOTONES DE ACCIÓN (ABREN EL FORMULARIO MÁGICO)
        // ========================================================

        private void MarcarDecidida(SyncItem item, int personaId)
        {
            string nombre = (item.PersonaFakeJSON?.Nombre ?? "").Trim();
            _fichaPorTarjeta[nombre] = personaId;
            _fichasDecididas.Add(nombre);
        }

        private void BtnResolverConflicto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SyncItem item)
            {
                // Resolver un conflicto S-21 no es una fusión con un registro ficticio.
                // Abrimos la persona real y cargamos los valores del JSON como opciones
                // amarillas para escoger campo por campo.
                var ventana = new FormularioPersonaWindow(item.PersonaDB, _personasExistentes);
                ventana.Owner = Window.GetWindow(this);
                ventana.CargarConflictosDesdeJson(item.PersonaFakeJSON);

                if (ventana.ShowDialog() == true)
                {
                    // Llama al motor histórico para clonar la persona si es necesario en lugar de sobrescribirla
                    ventana.PersonaActualizada.OrigenRegistro = DatabaseHelper.CombinarOrigenes(ventana.PersonaActualizada.OrigenRegistro, "S21");
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada);
                    DatabaseHelper.MarcarOrigenPersona(ventana.PersonaActualizada.Id, "S21");
                    MarcarDecidida(item, ventana.PersonaActualizada.Id);

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

                var ventana = new FormularioPersonaWindow(item.PersonaFakeJSON, _personasExistentes);
                ventana.Owner = Window.GetWindow(this);
                ventana.ConfigurarOrigenSincronizacion("S21");

                if (ventana.ShowDialog() == true)
                {
                    ventana.PersonaActualizada.OrigenRegistro = DatabaseHelper.CombinarOrigenes(ventana.PersonaActualizada.OrigenRegistro, "S21");
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada);
                    DatabaseHelper.MarcarOrigenPersona(ventana.PersonaActualizada.Id, "S21");
                    MarcarDecidida(item, ventana.PersonaActualizada.Id);
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
                ventana.ConfigurarOrigenSincronizacion("S21");
                if (ventana.ShowDialog() == true)
                {
                    ventana.PersonaActualizada.OrigenRegistro = DatabaseHelper.CombinarOrigenes(ventana.PersonaActualizada.OrigenRegistro, "S21");
                    DatabaseHelper.ActualizarPersonaDesdeVisitas(ventana.PersonaActualizada);
                    DatabaseHelper.MarcarOrigenPersona(ventana.PersonaActualizada.Id, "S21");
                    MarcarDecidida(item, ventana.PersonaActualizada.Id);
                }
            }
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            if (_conflictos.Count > 0 || _nuevos.Count > 0)
            {
                if (MessageBox.Show("Aún tienes conflictos o personas nuevas sin revisar. Si continúas, las fichas con conflicto conservarán sus datos (su actividad sí se guarda) y las personas nuevas se añadirán tal como vienen en el S-21.\n\n¿Deseas finalizar de todos modos?", "Atención", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    return;
                }
            }

            // Finalizar no pisa lo que ya se ha decidido en esta ventana ni lo que se ha
            // dejado sin revisar: esas fichas conservan sus datos y se guarda su actividad.
            var fichasSinTocar = new HashSet<string>(_fichasDecididas, StringComparer.OrdinalIgnoreCase);
            foreach (var conflicto in _conflictos)
            {
                fichasSinTocar.Add((conflicto.PersonaFakeJSON?.Nombre ?? "").Trim());
            }

            // AHORA USA EL CLONADOR MASIVO PARA GUARDAR EL JSON
            DatabaseHelper.GuardarVersionHistoricaJSON(_congreCodigo, _mesReferencia, _listaJson, _fichaPorTarjeta, fichasSinTocar);

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

        private string FormatearFechaS21(string fecha)
        {
            if (string.IsNullOrWhiteSpace(fecha)) return fecha;

            string valor = fecha.Trim();

            // Formatos habituales que pueden llegar en los JSON S-21.
            string[] formatos =
            {
                "yyyy-MM-dd", "yyyy/MM/dd",
                "dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy",
                "dd-MM-yyyy", "d-M-yyyy", "dd-M-yyyy", "d-MM-yyyy",
                "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFF",
                "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFK"
            };

            if (DateTime.TryParseExact(valor, formatos, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime fechaExacta))
                return fechaExacta.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            // Último intento con cultura española, para no perder fechas válidas
            // que incluyan separadores o componentes no contemplados arriba.
            if (DateTime.TryParse(valor, new CultureInfo("es-ES"), DateTimeStyles.AllowWhiteSpaces, out DateTime fechaEs))
                return fechaEs.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            // Si no puede interpretarse, conservamos el dato original en vez de borrarlo.
            return valor;
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