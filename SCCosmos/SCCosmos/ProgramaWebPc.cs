using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// El formulario web del programa de la visita, servido desde este PC:
    /// el servidor solo escucha en el propio equipo y Tailscale Funnel lo
    /// publica con la dirección https del PC. Lo que van rellenando los
    /// ancianos se guarda en Configuracion (llega al móvil con la
    /// sincronización); al enviarlo completo, se pasa solo al programa de la
    /// visita, salvo que ese programa tenga cambios hechos a mano.
    /// </summary>
    public static class ProgramaWebPc
    {
        public const int Puerto = 47320;

        private const string ClaveDireccion = ProgramaWeb.ClaveDireccion;

        private static ServidorProgramaWeb _servidor;

        /// <summary>Ha llegado algo del formulario, borrador o envío (código de congregación y mes). Llega desde otro hilo.</summary>
        public static event Action<string, string> EnvioRecibido;

        /// <summary>Lo enviado ya está en el programa de la visita (congregación y mes). Llega desde otro hilo.</summary>
        public static event Action<string, string> ProgramaPasado;

        /// <summary>Han subido o quitado archivos de una visita (congregación y mes). Llega desde otro hilo.</summary>
        public static event Action<string, string> ArchivosRecibidos;

        /// <summary>El enlace vale hasta estos días después del domingo de la visita.</summary>
        public const int DiasTrasLaVisita = 15;

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");


        public static void Iniciar()
        {
            if (_servidor is not null)
            {
                return;
            }

            try
            {
                _servidor = new ServidorProgramaWeb(Puerto, DatosVisita, Recibir, LeerPagina(), CarpetaParaCodigo);
                _servidor.ArchivosCambiados += codigo =>
                {
                    if (DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita)
                    {
                        ArchivosRecibidos?.Invoke(visita.Congregacion, visita.Mes);
                    }
                };
                _servidor.Redireccion = DestinoRedireccion;
                _servidor.Estado = EstadoJson;
                _servidor.Lugares = SugerenciasAsync;
                _servidor.Lugar = LugarGoogleAsync;
                _servidor.Iniciar();
            }
            catch (Exception ex)
            {
                _servidor = null;
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.Iniciar");
            }
        }


        public static string LeerPagina()
        {
            using Stream recurso =
                typeof(ProgramaWebPc).Assembly.GetManifestResourceStream("programa_web.html")
                ?? throw new InvalidOperationException("Falta la página del formulario (programa_web.html).");

            using var lector = new StreamReader(recurso);

            return lector.ReadToEnd();
        }


        private static string DatosVisita(string codigo) =>
            DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita && !Caducado(visita.Congregacion, visita.Mes)
                ? DatosVisitaJson(codigo, visita.Congregacion, visita.Mes)
                : null;


        // =========================================================
        // BUSCADOR DE DIRECCIONES DEL FORMULARIO: Google Maps si hay su
        // clave (LugaresGoogle); si no, o si falla, TomTom (LugaresTomTom);
        // sin ninguno, la página busca con OpenStreetMap. Lo pide la página
        // a este PC, así las claves no salen. Topes diarios para no salir
        // nunca de lo gratuito (Google: 10.000 al mes de cada tipo; TomTom:
        // 2.500 al día, que también usa el móvil para los tiempos de viaje).
        // =========================================================

        public const int MaxSugerenciasGoogleDia = 300;

        public const int MaxLugaresGoogleDia = 200;

        public const int MaxSugerenciasTomTomDia = 1000;

        // Las pruebas ponen uno de mentira.
        public static HttpClient HttpLugares { get; set; } = new() { Timeout = TimeSpan.FromSeconds(8) };

        private static readonly object CandadoConsultas = new();

        private static DateTime _diaConsultas = DateTime.MinValue;

        private static readonly Dictionary<string, int> _consultasHoy = new();


        /// <summary>La clave de Google Maps de Configuración («»: sin Google).</summary>
        public static string ClaveGoogle() =>
            DatabaseHelper.ObtenerConfiguracion(LugaresGoogle.ClaveApi, "").Trim();


        /// <summary>La clave de TomTom de Configuración (la misma del móvil; «»: sin TomTom).</summary>
        public static string ClaveTomTom() =>
            DatabaseHelper.ObtenerConfiguracion(LugaresTomTom.ClaveApi, "").Trim();


        /// <summary>Con qué busca el formulario: «google», «tomtom» o «» (OpenStreetMap, desde la página).</summary>
        public static string Buscador() =>
            ClaveGoogle().Length > 0 ? "google"
            : ClaveTomTom().Length > 0 ? "tomtom"
            : "";


        // Cuenta la consulta («google», «google-lugar», «tomtom») si aún quedan hoy.
        private static bool QuedanConsultas(string tipo, int maximo)
        {
            lock (CandadoConsultas)
            {
                if (_diaConsultas != DateTime.Today)
                {
                    _diaConsultas = DateTime.Today;
                    _consultasHoy.Clear();
                }

                int hechas = _consultasHoy.GetValueOrDefault(tipo);

                if (hechas >= maximo)
                {
                    return false;
                }

                _consultasHoy[tipo] = hechas + 1;

                return true;
            }
        }


        // Solo con el enlace de una visita que aún vale: nadie más gasta las consultas.
        private static (string Congregacion, string Mes)? VisitaVigente(string codigo) =>
            DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita && !Caducado(visita.Congregacion, visita.Mes)
                ? visita
                : null;


        /// <summary>
        /// Sugerencias para lo escrito en el formulario: «{"fuente","lugares"}» (las de
        /// Google traen «id» y piden las coordenadas al elegir; las de TomTom ya las
        /// traen). Primero Google; si no encuentra nada con calle, TomTom; si tampoco,
        /// «lugares» vacío para que la página pruebe con OpenStreetMap. null: sin
        /// claves o sin consultas, que la página siga ya solo con OpenStreetMap.
        /// </summary>
        public static async Task<string?> SugerenciasAsync(string codigo, string texto, string sesion, CancellationToken token)
        {
            texto = (texto ?? "").Trim();

            if (texto.Length < 3 || VisitaVigente(codigo) is not { } visita)
            {
                return null;
            }

            Congregacion? congregacion =
                DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == visita.Congregacion);

            (double, double)? centro =
                congregacion is { Latitud: not 0 } ? (congregacion.Latitud, congregacion.Longitud) : null;

            string claveGoogle = ClaveGoogle();
            bool contestaron = false;

            if (claveGoogle.Length > 0
                && LugaresGoogle.SesionValidaTexto(sesion)
                && QuedanConsultas("google", MaxSugerenciasGoogleDia)
                && await LugaresGoogle.SugerenciasAsync(HttpLugares, claveGoogle, texto, sesion, centro, token) is { } deGoogle)
            {
                if (HayLugares(deGoogle))
                {
                    return $"{{\"fuente\":\"Google Maps\",\"lugares\":{deGoogle}}}";
                }

                contestaron = true;
            }

            string claveTomTom = ClaveTomTom();

            if (claveTomTom.Length > 0
                && QuedanConsultas("tomtom", MaxSugerenciasTomTomDia)
                && await LugaresTomTom.SugerenciasAsync(HttpLugares, claveTomTom, texto, centro, token) is { } deTomTom)
            {
                if (HayLugares(deTomTom))
                {
                    return $"{{\"fuente\":\"TomTom\",\"lugares\":{deTomTom}}}";
                }

                contestaron = true;
            }

            return contestaron
                ? "{\"fuente\":\"\",\"lugares\":[]}"
                : null;
        }


        private static bool HayLugares(string lista) =>
            JsonNode.Parse(lista) is JsonArray { Count: > 0 };


        /// <summary>Las coordenadas de la dirección elegida entre las de Google (null si no se pueden dar).</summary>
        public static Task<string?> LugarGoogleAsync(string codigo, string id, string sesion, CancellationToken token)
        {
            string clave = ClaveGoogle();

            if (clave.Length == 0
                || !LugaresGoogle.IdLugarValido(id)
                || !LugaresGoogle.SesionValidaTexto(sesion)
                || VisitaVigente(codigo) is null
                || !QuedanConsultas("google-lugar", MaxLugaresGoogleDia))
            {
                return Task.FromResult<string?>(null);
            }

            return LugaresGoogle.LugarAsync(HttpLugares, clave, id, sesion, token);
        }


        /// <summary>Prueba una clave de Google (Configuración): cuántas sugerencias da para «Azuaga»; null si no funciona.</summary>
        public static async Task<int?> ProbarClaveGoogleAsync(string clave)
        {
            string? sugerencias = await LugaresGoogle.SugerenciasAsync(
                HttpLugares, clave.Trim(), "Azuaga", Guid.NewGuid().ToString("N"), null, CancellationToken.None);

            return sugerencias is null ? null : JsonNode.Parse(sugerencias)?.AsArray().Count;
        }


        /// <summary>Prueba una clave de TomTom: cuántas sugerencias da para «Azuaga»; null si no funciona.</summary>
        public static async Task<int?> ProbarClaveTomTomAsync(string clave)
        {
            string? sugerencias = await LugaresTomTom.SugerenciasAsync(
                HttpLugares, clave.Trim(), "Azuaga", null, CancellationToken.None);

            return sugerencias is null ? null : JsonNode.Parse(sugerencias)?.AsArray().Count;
        }


        /// <summary>Cuándo pulsaron «Enviar» por última vez («» si aún no), para el móvil.</summary>
        public static string? EstadoJson(string codigo) =>
            DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita && !Caducado(visita.Congregacion, visita.Mes)
                ? new JsonObject
                {
                    ["enviado"] = DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveEnviado(visita.Congregacion, visita.Mes), "")
                }.ToJsonString()
                : null;


        /// <summary>Cuántos archivos han subido los ancianos para esa visita (0 si ninguno).</summary>
        public static int ArchivosSubidos(string congregacion, string mes)
        {
            try
            {
                return ArchivosProgramaWeb.Subidos(CarpetaArchivos(congregacion, mes)).Count;
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.ArchivosSubidos");
                return 0;
            }
        }


        private static string CarpetaParaCodigo(string codigo) =>
            DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita && !Caducado(visita.Congregacion, visita.Mes)
                ? CarpetaArchivos(visita.Congregacion, visita.Mes)
                : null;


        /// <summary>Hasta cuándo vale el enlace: 15 días después del domingo de la visita (null si no está en el calendario).</summary>
        public static DateTime? Caducidad(string congregacion, string mes) =>
            DatabaseHelper.ObtenerVisitaProgramaWeb(congregacion, mes) is { } visita
                ? visita.Martes.AddDays(5 + DiasTrasLaVisita)
                : null;


        public static bool Caducado(string congregacion, string mes) =>
            Caducidad(congregacion, mes) is { } hasta && DateTime.Today > hasta;


        /// <summary>
        /// Dónde se guardan los archivos que suben los ancianos para esa visita:
        /// siempre en los datos de SCCosmos de este PC, «%LocalAppData%\SCCosmos ›
        /// Archivos de los ancianos › {congregación} › {Mes año}» (nunca en el
        /// Archivo del circuito). No la crea: se crea al llegar el primer archivo.
        /// </summary>
        public static string CarpetaArchivos(string congregacion, string mes)
        {
            string nombre =
                DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == congregacion)?.Nombre?.Trim() ?? "";

            nombre = string.Concat(
                (nombre.Length > 0 ? nombre : congregacion)
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

            string mesTexto =
                DateTime.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime primero)
                    ? primero.ToString("MMMM yyyy", Es)
                    : mes;

            mesTexto = char.ToUpper(mesTexto[0], Es) + mesTexto.Substring(1);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SCCosmos",
                "Archivos de los ancianos",
                nombre,
                mesTexto);
        }


        /// <summary>Lo que la página necesita de la visita (y lo ya enviado, para corregirlo).</summary>
        public static string DatosVisitaJson(string codigo, string congregacion, string mes)
        {
            var es = CultureInfo.GetCultureInfo("es-ES");
            var dias = new JsonObject();
            string fechas = "";
            bool conAlojamiento = true;

            if (DatabaseHelper.ObtenerVisitaProgramaWeb(congregacion, mes) is { } visita)
            {
                conAlojamiento = visita.ConAlojamiento;

                for (int i = 0; i < ProgramaWeb.Dias.Length; i++)
                {
                    dias[ProgramaWeb.Dias[i]] = visita.Martes.AddDays(i).ToString("dddd d", es);
                }

                DateTime domingo = visita.Martes.AddDays(5);

                fechas = visita.Martes.Month == domingo.Month
                    ? $"del {visita.Martes.Day} al {domingo.ToString("d 'de' MMMM 'de' yyyy", es)}"
                    : $"del {visita.Martes.ToString("d 'de' MMMM", es)} al {domingo.ToString("d 'de' MMMM 'de' yyyy", es)}";
            }

            Congregacion datosCongregacion =
                DatabaseHelper.ObtenerCongregaciones().FirstOrDefault(c => c.Codigo == congregacion);

            JsonNode envio = null;
            long enviadoEn = 0;

            if (Recibido(congregacion, mes) is { } recibido)
            {
                try
                {
                    envio = JsonNode.Parse(recibido.Json);
                    enviadoEn = new DateTimeOffset(recibido.Cuando).ToUnixTimeMilliseconds();
                }
                catch
                {
                }
            }

            string Nombre(string clave, string porDefecto)
            {
                string nombre = DatabaseHelper.ObtenerConfiguracion(clave, "").Trim();
                return nombre.Length > 0 ? nombre : porDefecto;
            }

            // Lo que ya se sabe de la congregación: el formulario lo trae puesto.
            string direccionSalon = datosCongregacion?.DireccionSR?.Trim() ?? "";
            string diaFinDeSemana = ProgramaWeb.Dia(datosCongregacion?.DiaFinSemana ?? "");

            var datos = new JsonObject
            {
                ["codigo"] = codigo,
                ["congregacion"] = datosCongregacion?.Nombre ?? "",
                ["fechas"] = fechas,
                ["dias"] = dias,
                ["alojamiento"] = conAlojamiento,
                ["sc"] = Nombre("NombreSC", "el superintendente"),
                ["esposa"] = Nombre("NombreEsposaSC", "su esposa"),
                ["centro"] = datosCongregacion is { Latitud: not 0 }
                    ? new JsonObject { ["lat"] = datosCongregacion.Latitud, ["lon"] = datosCongregacion.Longitud }
                    : null,
                ["salon"] = direccionSalon.Length > 0
                    ? new JsonObject { ["direccion"] = direccionSalon, ["lat"] = datosCongregacion.Latitud, ["lon"] = datosCongregacion.Longitud }
                    : null,
                ["horaEntreSemana"] = ProgramaWeb.Hora(datosCongregacion?.HoraEntreSemana ?? ""),
                ["diaFinDeSemana"] = diaFinDeSemana is "Sábado" or "Domingo" ? diaFinDeSemana : "",
                ["horaFinDeSemana"] = ProgramaWeb.Hora(datosCongregacion?.HoraFinSemana ?? ""),
                ["medioDiaLibre"] = MedioDiaLibre(),
                ["diasComida"] = new JsonArray(DiasComida(congregacion).Select(d => (JsonNode)d).ToArray()),
                // Para elegir el acompañante de cada visita de pastoreo.
                ["acompanantes"] = new JsonArray(Acompanantes(congregacion)
                    .Select(a => (JsonNode)new JsonObject { ["nombre"] = a.Nombre, ["cargo"] = a.Cargo })
                    .ToArray()),
                // Con clave de Google Maps o de TomTom, el buscador de direcciones la usa (a través de este PC).
                ["buscador"] = Buscador(),
                ["archivos"] = true,
                ["extensiones"] = new JsonArray(ArchivosProgramaWeb.Extensiones.Select(x => (JsonNode)x).ToArray()),
                ["envio"] = envio,
                ["enviadoEn"] = enviadoEn
            };

            // ToJsonString escapa «<», así que puede ir dentro de <script>.
            return datos.ToJsonString();
        }


        /// <summary>
        /// Los ancianos y siervos ministeriales de la congregación (sin las bajas),
        /// los primeros los ancianos: entre ellos se elige el acompañante de cada
        /// visita de pastoreo.
        /// </summary>
        public static List<(string Nombre, string Cargo)> Acompanantes(string congregacion)
        {
            try
            {
                return DatabaseHelper.ObtenerPersonas()
                    .Where(p => p.CongregacionCodigo == congregacion && string.IsNullOrWhiteSpace(p.FechaEliminacion))
                    .Select(p => (
                        Nombre: (p.Nombre ?? "").Trim(),
                        Cargo: (p.Nombramiento ?? "").Contains("Anciano", StringComparison.OrdinalIgnoreCase) ? "Anciano"
                            : (p.Nombramiento ?? "").Contains("Siervo ministerial", StringComparison.OrdinalIgnoreCase) ? "Siervo ministerial"
                            : ""))
                    .Where(a => a.Nombre.Length > 0 && a.Cargo.Length > 0)
                    .Distinct()
                    .OrderBy(a => a.Cargo == "Anciano" ? 0 : 1)
                    .ThenBy(a => a.Nombre, StringComparer.Create(Es, true))
                    .ToList();
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.Acompanantes");
                return new List<(string, string)>();
            }
        }


        /// <summary>
        /// La carpeta de la congregación en la última «Visita N» de Documentos
        /// («… › 1 - Congregaciones › Visita 3 › 2 - Castuera»), para proponerla al
        /// descargar los archivos de los ancianos; null si no se encuentra.
        /// </summary>
        public static string? CarpetaVisitaEnDocumentos(string nombreCongregacion)
        {
            string buscada = (nombreCongregacion ?? "").Trim();

            // «2 - Castuera» → «Castuera»; «4 - Cáceres - Norte» → «Cáceres - Norte».
            static string SinOrden(string carpeta)
            {
                int guion = carpeta.IndexOf(" - ", StringComparison.Ordinal);
                return guion > 0 && carpeta.Substring(0, guion).Trim().All(char.IsDigit)
                    ? carpeta.Substring(guion + 3).Trim()
                    : carpeta.Trim();
            }

            static int Numero(string carpeta) =>
                int.TryParse(Path.GetFileName(carpeta).Substring("Visita".Length).Trim(), out int n) ? n : 0;

            try
            {
                string documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                foreach (string raiz in Directory.EnumerateDirectories(documentos))
                {
                    string congregaciones = Path.Combine(raiz, "1 - Congregaciones");

                    if (buscada.Length == 0 || !Directory.Exists(congregaciones))
                    {
                        continue;
                    }

                    foreach (string visita in Directory.EnumerateDirectories(congregaciones, "Visita *").OrderByDescending(Numero))
                    {
                        string? encontrada = Directory.EnumerateDirectories(visita)
                            .FirstOrDefault(d => string.Equals(SinOrden(Path.GetFileName(d)), buscada, StringComparison.CurrentCultureIgnoreCase));

                        if (encontrada is not null)
                        {
                            return encontrada;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.CarpetaVisitaEnDocumentos");
            }

            return null;
        }


        /// <summary>El medio día libre de Configuración (sin elegir, el viernes por la mañana).</summary>
        public static string MedioDiaLibre() =>
            ProgramaWeb.MedioDiaLibre(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveMedioDiaLibre, ""));


        /// <summary>Los días que coméis con la congregación (Configuración; sin elegir, todos).</summary>
        public static string[] DiasComida() =>
            ProgramaWeb.DiasComida(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveDiasComida, ""));


        /// <summary>Los días que coméis con esa congregación: los elegidos en Circuito o, si no, los de Configuración.</summary>
        public static string[] DiasComida(string congregacion) =>
            ProgramaWeb.DiasComidaDe(congregacion, clave => DatabaseHelper.ObtenerConfiguracion(clave, ""));


        /// <summary>Si la congregación tiene sus propios días de comida (si no, valen los de Configuración).</summary>
        public static bool TieneDiasComidaPropios(string congregacion) =>
            !string.IsNullOrWhiteSpace(congregacion)
            && DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveDiasComidaCongregacion(congregacion.Trim()), "").Trim().Length > 0;


        /// <summary>Guarda los días de comida de la congregación (null: vuelve a los de Configuración).</summary>
        public static void GuardarDiasComida(string congregacion, IEnumerable<string>? dias) =>
            DatabaseHelper.GuardarConfiguracion(
                ProgramaWeb.ClaveDiasComidaCongregacion(congregacion.Trim()),
                dias is null ? "" : ProgramaWeb.TextoDiasComida(dias));


        /// <summary>
        /// Lo que llega del formulario. Mientras lo rellenan llega como borrador
        /// («borrador»: true) y se guarda, para seguir otro día o desde otro
        /// dispositivo. Al enviarlo completo, se pasa solo al programa de la visita.
        /// </summary>
        public static bool Recibir(string codigo, string json)
        {
            if (DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is not { } visita)
            {
                return false;
            }

            JsonObject datos;

            try
            {
                datos = JsonNode.Parse(json) as JsonObject;

                if (datos?["version"]?.GetValue<int>() != 1)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            // Sin la marca de borrador (como los envíos de antes), está completo.
            bool completo = !(datos["borrador"] is JsonValue marca && marca.TryGetValue(out bool esBorrador) && esBorrador);
            datos.Remove("borrador");

            var guardado = new JsonObject
            {
                ["recibido"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["completo"] = completo,
                ["datos"] = datos
            };

            DatabaseHelper.GuardarConfiguracion(
                ProgramaWeb.ClaveRecibido(visita.Congregacion, visita.Mes),
                guardado.ToJsonString());

            if (completo)
            {
                // Han pulsado «Enviar»: lo ve el móvil (sincronizado o preguntando) y avisa.
                DatabaseHelper.GuardarConfiguracion(
                    ProgramaWeb.ClaveEnviado(visita.Congregacion, visita.Mes),
                    ProgramaWeb.TextoEnviado(DateTime.Now, ArchivosSubidos(visita.Congregacion, visita.Mes)));

                PasarSolo(visita.Congregacion, visita.Mes, datos.ToJsonString());
            }

            EnvioRecibido?.Invoke(visita.Congregacion, visita.Mes);

            return true;
        }


        // Lo enviado pasa solo al programa si el del mes está vacío o tal como
        // quedó la última vez; si alguien lo ha cambiado a mano, no se pisa
        // (se pasa desde la pestaña Programa, con «Pasar al programa»).
        private static void PasarSolo(string congregacion, string mes, string json)
        {
            try
            {
                if (!ProgramaSinTocar(congregacion, mes))
                {
                    return;
                }

                List<EventoProgramaVisita> eventos = EventosRecibidos(congregacion, json);

                if (eventos.Count > 0)
                {
                    PasarAlPrograma(congregacion, mes, eventos);
                    ProgramaPasado?.Invoke(congregacion, mes);
                }
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.PasarSolo");
            }
        }


        /// <summary>Si el programa del mes está vacío o igual que cuando se pasó lo recibido por última vez.</summary>
        public static bool ProgramaSinTocar(string congregacion, string mes)
        {
            string actual = DatabaseHelper.HuellaProgramaMes(congregacion, mes);

            return actual.Length == 0
                || actual == DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveHuella(congregacion, mes), "");
        }


        /// <summary>
        /// Lo recibido, como actividades del programa: en el medio día libre no va
        /// nadie con vosotros, solo cuentan las comidas de los días que coméis con
        /// la congregación y, en reuniones y salidas sin lugar, el del salón (como con el PDF).
        /// </summary>
        public static List<EventoProgramaVisita> EventosRecibidos(string congregacion, string json)
        {
            var (eventos, _) = ProgramaWeb.AEventos(json, MedioDiaLibre(), DiasComida(congregacion));

            string direccionSalon =
                DatabaseHelper.ObtenerCongregaciones()
                    .FirstOrDefault(c => string.Equals(c.Codigo, congregacion, StringComparison.OrdinalIgnoreCase))
                    ?.DireccionSR?.Trim() ?? "";

            if (direccionSalon.Length > 0)
            {
                foreach (var evento in eventos.Where(ev =>
                             string.IsNullOrWhiteSpace(ev.Ubicacion)
                             && (ev.TipoActividad.StartsWith("Reunión", StringComparison.OrdinalIgnoreCase)
                                 || ev.TipoActividad == "Predicación")))
                {
                    evento.Ubicacion = direccionSalon;
                }
            }

            return eventos;
        }


        /// <summary>Sustituye el programa del mes por esas actividades y apunta cómo quedó.</summary>
        public static int PasarAlPrograma(string congregacion, string mes, List<EventoProgramaVisita> eventos)
        {
            int creados = DatabaseHelper.ReemplazarProgramaMesVisita(congregacion, mes, eventos);

            MarcarAplicado(congregacion, mes);

            DatabaseHelper.GuardarConfiguracion(
                ProgramaWeb.ClaveHuella(congregacion, mes),
                DatabaseHelper.HuellaProgramaMes(congregacion, mes));

            return creados;
        }


        /// <summary>
        /// Lo último que llegó del formulario para esa visita (null si nada) y si
        /// lo enviaron completo o es un borrador a medias.
        /// </summary>
        public static (DateTime Cuando, string Json, bool Completo)? Recibido(string congregacion, string mes)
        {
            string texto = DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveRecibido(congregacion, mes), "");

            if (texto.Length == 0)
            {
                return null;
            }

            try
            {
                JsonNode guardado = JsonNode.Parse(texto);

                DateTime cuando = DateTime.ParseExact(
                    guardado?["recibido"]?.GetValue<string>() ?? "",
                    "yyyy-MM-ddTHH:mm:ss",
                    CultureInfo.InvariantCulture);

                // Lo guardado antes de los borradores no trae «completo»: lo estaba.
                bool completo = guardado?["completo"] is not JsonValue marca
                    || !marca.TryGetValue(out bool esCompleto)
                    || esCompleto;

                return guardado?["datos"] is JsonObject datos
                    ? (cuando, datos.ToJsonString(), completo)
                    : null;
            }
            catch
            {
                return null;
            }
        }


        /// <summary>Cuándo se pasó lo recibido al programa (null si aún no).</summary>
        public static DateTime? Aplicado(string congregacion, string mes) =>
            DateTime.TryParseExact(
                DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveAplicado(congregacion, mes), ""),
                "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime cuando)
                ? cuando
                : null;


        public static void MarcarAplicado(string congregacion, string mes) =>
            DatabaseHelper.GuardarConfiguracion(
                ProgramaWeb.ClaveAplicado(congregacion, mes),
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));


        /// <summary>Si ya se ha creado el enlace de esa visita (sin crearlo).</summary>
        public static bool TieneEnlace(string congregacion, string mes) =>
            ProgramaWeb.CodigoValido(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveCodigo(congregacion, mes), ""));


        /// <summary>El enlace de la visita para los ancianos (con su código; se crea la primera vez).</summary>
        public static string Enlace(string congregacion, string mes)
        {
            string clave = ProgramaWeb.ClaveCodigo(congregacion, mes);
            string codigo = DatabaseHelper.ObtenerConfiguracion(clave, "");

            if (!ProgramaWeb.CodigoValido(codigo))
            {
                codigo = ProgramaWeb.NuevoCodigo();
                DatabaseHelper.GuardarConfiguracion(clave, codigo);
            }

            return ProgramaWeb.Enlace(DireccionPublica(), codigo);
        }


        /// <summary>El enlace de la visita de una semana del calendario (null si no se encuentra la congregación).</summary>
        public static string EnlaceDeSemana(string nombreCongregacion, DateTime fechaMartes)
        {
            string codigo = DatabaseHelper.ObtenerCodigoCongregacion(nombreCongregacion);

            return string.IsNullOrWhiteSpace(codigo)
                ? null
                : Enlace(codigo, fechaMartes.ToString("yyyy-MM"));
        }


        /// <summary>Copia el enlace y solo avisa de eso.</summary>
        public static void CopiarEnlace(string enlace)
        {
            System.Windows.Clipboard.SetText(enlace);
            System.Windows.MessageBox.Show("Enlace copiado en el portapapeles");
        }


        /// <summary>
        /// La dirección para los enlaces. Con un servidor elegido en Configuración,
        /// siempre la suya. Sin elegir, la de Tailscale de este PC (Funnel la publica),
        /// que se guarda para el móvil, como hasta ahora.
        /// </summary>
        public static string DireccionPublica()
        {
            string elegido = ServidorElegido();

            if (elegido.Length > 0)
            {
                return elegido;
            }

            string guardada = DatabaseHelper.ObtenerConfiguracion(ClaveDireccion, "");
            string propia = DireccionTailscale();

            if (propia.Length > 0)
            {
                if (propia != guardada)
                {
                    DatabaseHelper.GuardarConfiguracion(ClaveDireccion, propia);
                }

                return propia;
            }

            return guardada.Length > 0 ? guardada : "http://localhost:" + Puerto;
        }


        /// <summary>El servidor del formulario elegido en Configuración («» si no se ha elegido).</summary>
        public static string ServidorElegido() =>
            ProgramaWeb.NormalizarDireccion(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveServidor, ""));


        /// <summary>
        /// Elige el servidor del formulario para todos los dispositivos («» lo quita y
        /// vuelve a ser automático). Devuelve la dirección guardada.
        /// </summary>
        public static string ElegirServidor(string direccion)
        {
            string normalizada = ProgramaWeb.NormalizarDireccion(direccion);

            DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveServidor, normalizada);

            // Los equipos aún sin actualizar leen esta otra: que también lleve al servidor elegido.
            if (normalizada.Length > 0)
            {
                DatabaseHelper.GuardarConfiguracion(ClaveDireccion, normalizada);
            }

            return normalizada;
        }


        /// <summary>
        /// Los enlaces enviados antes de cambiar de servidor siguen funcionando:
        /// si este PC recibe la página por otra dirección que la del servidor
        /// elegido, la manda allí (mientras este PC esté encendido).
        /// </summary>
        public static string? DestinoRedireccion(string host)
        {
            try
            {
                return ProgramaWeb.DestinoRedireccion(host, ServidorElegido(), DireccionTailscaleReciente());
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.Redireccion");
                return null;
            }
        }


        private static string _direccionTailscale = "";
        private static DateTime _direccionTailscaleHasta = DateTime.MinValue;

        // La de Tailscale, sin preguntarle en cada visita a la página.
        private static string DireccionTailscaleReciente()
        {
            if (DateTime.UtcNow > _direccionTailscaleHasta)
            {
                _direccionTailscale = DireccionTailscale();
                _direccionTailscaleHasta = DateTime.UtcNow.AddMinutes(10);
            }

            return _direccionTailscale;
        }


        /// <summary>
        /// Los enlaces que aún valen (la visita está en el calendario y no ha
        /// caducado), con la dirección actual: congregación, mes y enlace.
        /// </summary>
        public static List<(string Congregacion, string Mes, string Enlace)> EnlacesActivos()
        {
            var activos = new List<(string, string, string)>();
            string? direccion = null;

            foreach (string visita in DatabaseHelper.VisitasConEnlaceProgramaWeb().OrderBy(v => v.Substring(v.LastIndexOf('_') + 1)))
            {
                // «{congregación}_{yyyy-MM}» (el código de congregación puede llevar «_»).
                int separador = visita.LastIndexOf('_');

                if (separador <= 0)
                {
                    continue;
                }

                string congregacion = visita.Substring(0, separador);
                string mes = visita.Substring(separador + 1);

                if (Caducidad(congregacion, mes) is not { } hasta || DateTime.Today > hasta)
                {
                    continue;
                }

                direccion ??= DireccionPublica();

                activos.Add((congregacion, mes, ProgramaWeb.Enlace(
                    direccion,
                    DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveCodigo(congregacion, mes), ""))));
            }

            return activos;
        }


        /// <summary>«https://equipo.red.ts.net»: la dirección de Tailscale de este PC («» si no tiene Tailscale).</summary>
        public static string DireccionTailscale()
        {
            try
            {
                string tailscale = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Tailscale",
                    "tailscale.exe");

                if (!File.Exists(tailscale))
                {
                    return "";
                }

                var inicio = new ProcessStartInfo(tailscale, "status --json")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process proceso = Process.Start(inicio);
                string salida = proceso?.StandardOutput.ReadToEnd() ?? "";
                proceso?.WaitForExit(5000);

                // Tailscale parado o sin sesión: no contesta con JSON.
                if (!salida.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    return "";
                }

                string dns = JsonNode.Parse(salida)?["Self"]?["DNSName"]?.GetValue<string>()?.TrimEnd('.') ?? "";

                return dns.Length > 0 ? "https://" + dns : "";
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "ProgramaWeb.Tailscale");
                return "";
            }
        }
    }
}
