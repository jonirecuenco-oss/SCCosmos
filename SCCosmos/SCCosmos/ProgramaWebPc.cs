using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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

        private const string ClaveDireccion = "ProgramaWeb_Direccion";

        private static ServidorProgramaWeb _servidor;

        /// <summary>Ha llegado un envío (código de congregación y mes). Llega desde otro hilo.</summary>
        public static event Action<string, string> EnvioRecibido;


        public static void Iniciar()
        {
            if (_servidor is not null)
            {
                return;
            }

            try
            {
                _servidor = new ServidorProgramaWeb(Puerto, DatosVisita, GuardarEnvio, LeerPagina());
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
            DatabaseHelper.BuscarVisitaProgramaWeb(codigo) is { } visita
                ? DatosVisitaJson(codigo, visita.Congregacion, visita.Mes)
                : null;


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
                ["envio"] = envio,
                ["enviadoEn"] = enviadoEn
            };

            // ToJsonString escapa «<», así que puede ir dentro de <script>.
            return datos.ToJsonString();
        }


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

            return DireccionPublica() + "/programa/" + codigo;
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
