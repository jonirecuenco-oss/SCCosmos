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
        private List<SyncItemHub> _traslados = new List<SyncItemHub>();
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

            DetectarPosiblesTraslados();
            ActualizarListasUI();
        }

        private void DetectarPosiblesTraslados()
        {
            _traslados.Clear();

            var nuevosProcesados = new HashSet<SyncItemHub>();
            var bajasProcesadas = new HashSet<SyncItemHub>();

            foreach (var nuevo in _nuevos.ToList())
            {
                if (nuevo.PersonaHub == null) continue;

                var candidatos = _bajas
                    .Where(b =>
                        b.PersonaDB != null
                        && !string.Equals(
                            b.PersonaDB.CongregacionCodigo,
                            nuevo.PersonaHub.CongregacionCodigo,
                            StringComparison.OrdinalIgnoreCase)
                        && EsMismaPersonaParaTraslado(b.PersonaDB, nuevo.PersonaHub))
                    .ToList();

                // Por seguridad, solo proponemos traslado automático cuando hay
                // una única ficha anterior compatible. Si hay homónimos, se deja
                // como alta/baja normal para que el usuario decida manualmente.
                if (candidatos.Count != 1) continue;

                var baja = candidatos[0];
                if (bajasProcesadas.Contains(baja)) continue;

                string nombre = ObtenerNombrePreferido(baja.PersonaDB);
                string origen = baja.PersonaDB.CongregacionNombre;
                if (string.IsNullOrWhiteSpace(origen)) origen = baja.PersonaDB.CongregacionCodigo;
                string destino = nuevo.PersonaHub.CongregacionCodigo ?? "";

                _traslados.Add(new SyncItemHub
                {
                    PersonaDB = baja.PersonaDB,
                    PersonaHub = nuevo.PersonaHub,
                    NombreVisual = nombre,
                    Descripcion = $"Posible traslado: {origen} → {destino}. Se conservarán ambas fichas enlazadas."
                });

                nuevosProcesados.Add(nuevo);
                bajasProcesadas.Add(baja);
            }

            foreach (var item in nuevosProcesados) _nuevos.Remove(item);
            foreach (var item in bajasProcesadas) _bajas.Remove(item);
        }

        private static bool EsMismaPersonaParaTraslado(Persona anterior, Persona nueva)
        {
            if (anterior == null || nueva == null) return false;

            bool mismoEmailJw =
                !string.IsNullOrWhiteSpace(anterior.EmailJW)
                && !string.IsNullOrWhiteSpace(nueva.EmailJW)
                && string.Equals(anterior.EmailJW.Trim(), nueva.EmailJW.Trim(), StringComparison.OrdinalIgnoreCase);

            bool mismoEmail =
                !string.IsNullOrWhiteSpace(anterior.Email)
                && !string.IsNullOrWhiteSpace(nueva.Email)
                && string.Equals(anterior.Email.Trim(), nueva.Email.Trim(), StringComparison.OrdinalIgnoreCase);

            string[] nombresAnterior =
            {
                anterior.NombreJWHub ?? "",
                anterior.Nombre ?? ""
            };

            string[] nombresNueva =
            {
                nueva.NombreJWHub ?? "",
                nueva.Nombre ?? ""
            };

            bool mismoNombre = nombresAnterior
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Any(a => nombresNueva.Any(n =>
                    !string.IsNullOrWhiteSpace(n)
                    && string.Equals(a.Trim(), n.Trim(), StringComparison.OrdinalIgnoreCase)));

            // Hace falta una señal fuerte de identidad. El nombre de Hub suele ser
            // estable durante el traslado; los correos son todavía más concluyentes.
            if (!mismoEmailJw && !mismoEmail && !mismoNombre) return false;

            if (FechasIncompatiblesTraslado(anterior.FechaNacimiento, nueva.FechaNacimiento)) return false;
            if (FechasIncompatiblesTraslado(anterior.FechaBautismo, nueva.FechaBautismo)) return false;

            return true;
        }

        private static bool FechasIncompatiblesTraslado(string anterior, string nueva)
        {
            if (string.IsNullOrWhiteSpace(anterior) || string.IsNullOrWhiteSpace(nueva)) return false;

            int anioAnterior = ExtraerAnio(anterior);
            int anioNueva = ExtraerAnio(nueva);

            if (anioAnterior > 0 && anioNueva > 0 && Math.Abs(anioAnterior - anioNueva) > 2)
                return true;

            bool aproxAnterior = anterior.Trim().StartsWith("01/01/", StringComparison.Ordinal);
            bool aproxNueva = nueva.Trim().StartsWith("01/01/", StringComparison.Ordinal);

            // Si ambas son fechas definitivas, una discrepancia completa sí sirve
            // para descartar homónimos. Si alguna es aproximada, solo miramos el año.
            if (!aproxAnterior && !aproxNueva)
            {
                return !string.Equals(anterior.Trim(), nueva.Trim(), StringComparison.Ordinal);
            }

            return false;
        }

        private static int ExtraerAnio(string fecha)
        {
            if (string.IsNullOrWhiteSpace(fecha)) return 0;
            for (int i = fecha.Length - 4; i >= 0; i--)
            {
                if (i + 4 <= fecha.Length
                    && int.TryParse(fecha.Substring(i, 4), out int anio)
                    && anio >= 1900 && anio <= 2100)
                    return anio;
            }
            return 0;
        }

        private static string ObtenerNombrePreferido(Persona p)
        {
            if (p == null) return "";
            if (!string.IsNullOrWhiteSpace(p.NombreComun)) return p.NombreComun.Trim();
            if (!string.IsNullOrWhiteSpace(p.NombreJWHub)) return p.NombreJWHub.Trim();
            return p.Nombre?.Trim() ?? "";
        }

        private void ActualizarListasUI()
        {
            GridConflictos.ItemsSource = null; GridConflictos.ItemsSource = _conflictos;
            GridNuevos.ItemsSource = null; GridNuevos.ItemsSource = _nuevos;
            GridBajas.ItemsSource = null; GridBajas.ItemsSource = _bajas;
            GridTraslados.ItemsSource = null; GridTraslados.ItemsSource = _traslados;
            GridIguales.ItemsSource = null; GridIguales.ItemsSource = _iguales;

            TxtNumConflictos.Text = _conflictos.Count.ToString();
            TxtNumNuevos.Text = _nuevos.Count.ToString();
            TxtNumBajas.Text = _bajas.Count.ToString();
            TxtNumTraslados.Text = _traslados.Count.ToString();
            TxtNumIguales.Text = _iguales.Count.ToString();
        }

        /// <summary>
        /// Compara dos textos ignorando espacios sobrantes (al principio, al
        /// final y repetidos por dentro), para no marcar como conflicto
        /// cambios irreales.
        /// </summary>
        private static bool MismoTexto(string a, string b)
        {
            return string.Equals(
                NormalizarTexto(a),
                NormalizarTexto(b),
                StringComparison.Ordinal);
        }

        private static bool MismoTextoSinMayusculas(string a, string b)
        {
            return string.Equals(
                NormalizarTexto(a),
                NormalizarTexto(b),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            var resultado = new System.Text.StringBuilder(texto.Length);
            bool huecoPendiente = false;

            foreach (char caracter in texto.Trim())
            {
                if (char.IsWhiteSpace(caracter)) { huecoPendiente = true; continue; }
                if (huecoPendiente && resultado.Length > 0) resultado.Append(' ');
                huecoPendiente = false;
                resultado.Append(caracter);
            }

            return resultado.ToString();
        }

        private bool HayConflicto(Persona db, Persona hub)
        {
            // NombreJWHub sí pertenece a Hub y puede actualizarse.
            if (!string.IsNullOrEmpty(hub.NombreJWHub) && !MismoTexto(db.NombreJWHub, hub.NombreJWHub)) return true;

            // Nombre completo y nombre común ya rellenos se consideran consolidados.
            // Hub únicamente los aporta cuando el campo de BBDD está vacío.
            if (string.IsNullOrWhiteSpace(db.Nombre) && !string.IsNullOrWhiteSpace(hub.Nombre)) return true;
            if (string.IsNullOrWhiteSpace(db.NombreComun) && !string.IsNullOrWhiteSpace(hub.NombreComun)) return true;

            // Grupo se ignora en JW Hub, pero Sexo se obtiene ahora desde
            // la pestaña Información personal y sí debe sincronizarse.
            if (!string.IsNullOrWhiteSpace(hub.Sexo)
                && !string.Equals(db.Sexo ?? "", hub.Sexo, StringComparison.OrdinalIgnoreCase))
                return true;

            string hubEsp = hub.Esperanza ?? "";
            string dbEsp = db.Esperanza ?? "";
            if (hubEsp != "" && !MismoTextoSinMayusculas(dbEsp, hubEsp)) return true;

            // DETECCIÓN DE PÉRDIDAS (Nombramientos y Servicios)
            // Al quitar el "IsNullOrEmpty", obligamos a que detecte si el Hub viene vacío y la BBDD no.
            string hubNomb = hub.Nombramiento ?? "";
            string dbNomb = db.Nombramiento ?? "";
            if (!MismoTexto(hubNomb, dbNomb)) return true;

            string hubServ = hub.Servicio ?? "";
            string dbServ = db.Servicio ?? "";
            if (!MismoTexto(hubServ, dbServ)) return true;

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
                if (!string.IsNullOrEmpty(hub.FechaNacimiento) && !MismoTexto(db.FechaNacimiento, hub.FechaNacimiento) && !hub.FechaNacimiento.StartsWith("01/01/")) return true;
                if (!string.IsNullOrEmpty(hub.FechaBautismo) && !MismoTexto(db.FechaBautismo, hub.FechaBautismo) && !hub.FechaBautismo.StartsWith("01/01/")) return true;
            }

            if (!string.IsNullOrEmpty(hub.FechaInicioPrec) && !MismoTexto(db.FechaInicioPrec, hub.FechaInicioPrec) && !hub.FechaInicioPrec.StartsWith("01/01/")) return true;

            if (!string.IsNullOrEmpty(hub.FechaUltimaEscuela) && !MismoTexto(db.FechaUltimaEscuela, hub.FechaUltimaEscuela)) return true;

            if (!string.IsNullOrEmpty(hub.Email) && !MismoTexto(db.Email, hub.Email)) return true;
            if (!string.IsNullOrEmpty(hub.EmailJW) && !MismoTexto(db.EmailJW, hub.EmailJW)) return true;
            if (!string.IsNullOrEmpty(hub.Telefono) && !MismoTexto(db.Telefono, hub.Telefono)) return true;
            if (!string.IsNullOrEmpty(hub.Direccion) && !MismoTexto(db.Direccion, hub.Direccion)) return true;
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

        private void BtnConfirmarTraslado_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SyncItemHub item) return;
            if (item.PersonaDB == null || item.PersonaHub == null) return;

            string nombre = ObtenerNombrePreferido(item.PersonaDB);
            string origen = !string.IsNullOrWhiteSpace(item.PersonaDB.CongregacionNombre)
                ? item.PersonaDB.CongregacionNombre
                : item.PersonaDB.CongregacionCodigo;
            string destino = item.PersonaHub.CongregacionCodigo ?? "";

            var respuesta = MessageBox.Show(
                $"¿Confirmas el traslado de {nombre}?\n\n" +
                $"{origen}  →  {destino}\n\n" +
                "La ficha anterior se dará de baja y se conservará para las visitas históricas. " +
                "Se creará una ficha nueva enlazada en la congregación de destino.\n\n" +
                "La actividad mensual NO se copiará a la ficha nueva.",
                "Confirmar traslado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (respuesta != MessageBoxResult.Yes) return;

            try
            {
                int nuevoId = DatabaseHelper.RegistrarTraslado(
                    item.PersonaDB,
                    item.PersonaHub,
                    DateTime.Today);

                item.PersonaHub.Id = nuevoId;
                item.PersonaDB.FechaEliminacion = DateTime.Today.ToString("dd/MM/yyyy");
                item.PersonaDB.TrasladoSiguienteId = nuevoId;
                item.PersonaDB.FechaTraslado = DateTime.Today.ToString("dd/MM/yyyy");
                item.PersonaHub.TrasladoAnteriorId = item.PersonaDB.Id;
                item.PersonaHub.FechaTraslado = DateTime.Today.ToString("dd/MM/yyyy");

                Persona nuevaGuardada = DatabaseHelper.ObtenerPersonaPorId(nuevoId) ?? item.PersonaHub;
                nuevaGuardada.TrasladoAnteriorId = item.PersonaDB.Id;
                nuevaGuardada.FechaTraslado = DateTime.Today.ToString("dd/MM/yyyy");

                _traslados.Remove(item);
                _iguales.Add(new SyncItemHub
                {
                    PersonaDB = nuevaGuardada,
                    PersonaHub = item.PersonaHub,
                    NombreVisual = ObtenerNombrePreferido(nuevaGuardada),
                    Descripcion = $"Traslado registrado: {origen} → {destino}."
                });

                ActualizarListasUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo registrar el traslado.\n\n" + ex.Message,
                    "Traslado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnNoEsTraslado_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not SyncItemHub item) return;
            if (item.PersonaDB == null || item.PersonaHub == null) return;

            _traslados.Remove(item);

            _nuevos.Add(new SyncItemHub
            {
                PersonaDB = null,
                PersonaHub = item.PersonaHub,
                NombreVisual = ObtenerNombrePreferido(item.PersonaHub),
                Descripcion = "Aparece en JW Hub pero no está en la Base de Datos."
            });

            _bajas.Add(new SyncItemHub
            {
                PersonaDB = item.PersonaDB,
                PersonaHub = null,
                NombreVisual = ObtenerNombrePreferido(item.PersonaDB),
                Descripcion = "Ya no aparece en JW Hub. ¿Se ha dado de baja o mudado?"
            });

            ActualizarListasUI();
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
                else if (_traslados.Contains(item)) _traslados.Remove(item);
                else if (_iguales.Contains(item)) _iguales.Remove(item);

                ActualizarListasUI();
            }
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            if (_traslados.Count > 0)
            {
                MessageBox.Show(
                    $"Quedan {_traslados.Count} posibles traslados por revisar.\n\nConfirma cada traslado o pulsa 'No es traslado' antes de guardar.",
                    "Traslados pendientes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

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