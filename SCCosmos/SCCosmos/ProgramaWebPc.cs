using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// El formulario web del programa de la visita, servido desde este PC:
    /// el servidor solo escucha en el propio equipo y Tailscale Funnel lo
    /// publica con la dirección https del PC. Lo que envían los ancianos se
    /// guarda en Configuracion (llega al móvil con la sincronización) y se
    /// pasa al programa desde la pestaña Programa de la visita.
    /// </summary>
    public static class ProgramaWebPc
    {
        public const int Puerto = 47320;

        private const string ClaveDireccion = ProgramaWeb.ClaveDireccion;

        private static ServidorProgramaWeb _servidor;

        /// <summary>Ha llegado un envío (código de congregación y mes). Llega desde otro hilo.</summary>
        public static event Action<string, string> EnvioRecibido;

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
                _servidor = new ServidorProgramaWeb(Puerto, DatosVisita, GuardarEnvio, LeerPagina(), CarpetaParaCodigo);
                _servidor.ArchivosCambiados += codigo =>
                {
                    if (DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita)
                    {
                        ArchivosRecibidos?.Invoke(visita.Congregacion, visita.Mes);
                    }
                };
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
        /// en el Archivo del circuito, «1 - 2 - S-303 y S-2 › {n} - {congregación} ›
        /// Archivos de los ancianos › {Mes año}». Sin Archivo del circuito, en los
        /// datos de SCCosmos. No la crea: se crea al llegar el primer archivo.
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

            string s303 = Path.Combine(
                DatabaseHelper.ObtenerConfiguracion("RutaArchivoCircuito", "").Trim(),
                "1 - 2 - S-303 y S-2");

            if (Path.IsPathRooted(s303) && Directory.Exists(s303))
            {
                // «10 - Navalmoral de la Mata»: la de la congregación, con su número delante.
                string carpetaCongregacion =
                    Directory.EnumerateDirectories(s303)
                        .FirstOrDefault(d => MismoNombre(Regex.Replace(Path.GetFileName(d), @"^\d+\s*-\s*", ""), nombre))
                    ?? Path.Combine(s303, nombre);

                return Path.Combine(carpetaCongregacion, "Archivos de los ancianos", mesTexto);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SCCosmos",
                "Archivos de los ancianos",
                nombre,
                mesTexto);
        }


        private static bool MismoNombre(string a, string b) =>
            string.Compare(a.Trim(), b.Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;


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
                ["diasComida"] = new JsonArray(DiasComida().Select(d => (JsonNode)d).ToArray()),
                ["archivos"] = true,
                ["extensiones"] = new JsonArray(ArchivosProgramaWeb.Extensiones.Select(x => (JsonNode)x).ToArray()),
                ["envio"] = envio,
                ["enviadoEn"] = enviadoEn
            };

            // ToJsonString escapa «<», así que puede ir dentro de <script>.
            return datos.ToJsonString();
        }


        /// <summary>El medio día libre de Configuración (sin elegir, el viernes por la mañana).</summary>
        public static string MedioDiaLibre() =>
            ProgramaWeb.MedioDiaLibre(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveMedioDiaLibre, ""));


        /// <summary>Los días que coméis con la congregación (Configuración; sin elegir, todos).</summary>
        public static string[] DiasComida() =>
            ProgramaWeb.DiasComida(DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveDiasComida, ""));


        private static bool GuardarEnvio(string codigo, string json)
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

            var guardado = new JsonObject
            {
                ["recibido"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["datos"] = datos
            };

            DatabaseHelper.GuardarConfiguracion(
                ProgramaWeb.ClaveRecibido(visita.Congregacion, visita.Mes),
                guardado.ToJsonString());

            EnvioRecibido?.Invoke(visita.Congregacion, visita.Mes);

            return true;
        }


        /// <summary>Lo último que enviaron los ancianos para esa visita (null si nada).</summary>
        public static (DateTime Cuando, string Json)? Recibido(string congregacion, string mes)
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

                return guardado?["datos"] is JsonObject datos
                    ? (cuando, datos.ToJsonString())
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


        /// <summary>«https://equipo.red.ts.net»: la dirección de Tailscale de este PC (Funnel la publica).</summary>
        public static string DireccionPublica()
        {
            string guardada = DatabaseHelper.ObtenerConfiguracion(ClaveDireccion, "");

            try
            {
                string tailscale = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Tailscale",
                    "tailscale.exe");

                if (File.Exists(tailscale))
                {
                    var inicio = new ProcessStartInfo(tailscale, "status --json")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using Process proceso = Process.Start(inicio);
                    string salida = proceso?.StandardOutput.ReadToEnd() ?? "";
                    proceso?.WaitForExit(5000);

                    string dns = JsonNode.Parse(salida)?["Self"]?["DNSName"]?.GetValue<string>()?.TrimEnd('.') ?? "";

                    if (dns.Length > 0)
                    {
                        string direccion = "https://" + dns;

                        if (direccion != guardada)
                        {
                            DatabaseHelper.GuardarConfiguracion(ClaveDireccion, direccion);
                        }

                        return direccion;
                    }
                }
            }
            catch
            {
            }

            return guardada.Length > 0 ? guardada : "http://localhost:" + Puerto;
        }
    }
}
