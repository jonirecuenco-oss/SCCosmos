using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos
{
    /// <summary>
    /// Mantiene Tailscale conectado mientras SCCosmos está abierto: con él se
    /// sincroniza fuera de casa y Funnel publica el formulario de los ancianos.
    /// Si está instalado pero desconectado, lo vuelve a conectar solo; lo que
    /// no se puede arreglar sin la persona (iniciar sesión, servicio parado)
    /// sale en la campana. Sin Tailscale instalado no hace nada.
    /// </summary>
    public static class TailscalePc
    {
        public enum Accion
        {
            Nada,
            Conectar,
            Avisar
        }

        private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

        private const string SinServicio =
            "El servicio de Tailscale no responde en este PC. Ábrelo desde el menú Inicio o reinicia el PC.";

        private const string SinSesion =
            "Tailscale necesita que vuelvas a iniciar sesión en este PC: ábrelo y pulsa «Log in». "
            + "En el PC que hace de servidor conviene desactivar la caducidad de su clave en la consola de Tailscale.";

        private static CancellationTokenSource _cancelacion;

        /// <summary>Lo que impide que esté conectado y no se arregla solo («» si nada).</summary>
        public static string Problema { get; private set; } = "";

        private static string Carpeta =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale");

        public static bool Instalado => File.Exists(Path.Combine(Carpeta, "tailscale.exe"));


        public static void Iniciar()
        {
            if (_cancelacion is not null || !Instalado)
            {
                return;
            }

            _cancelacion = new CancellationTokenSource();
            CancellationToken token = _cancelacion.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Sin estorbar al arranque.
                    await Task.Delay(TimeSpan.FromSeconds(15), token);

                    while (!token.IsCancellationRequested)
                    {
                        Comprobar();
                        await Task.Delay(Intervalo, token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }


        public static void Detener()
        {
            _cancelacion?.Cancel();
            _cancelacion = null;
        }


        /// <summary>Mira cómo está Tailscale y, si está desconectado, lo vuelve a conectar.</summary>
        public static void Comprobar()
        {
            try
            {
                var (accion, problema) = Interpretar(Ejecutar("status --json"));

                if (accion == Accion.Conectar)
                {
                    // «Disconnect» en su icono o «tailscale down»: lo mismo que pulsar «Connect».
                    Ejecutar("up");
                    Perf.Registrar("Tailscale.Reconectado", 0);

                    (accion, problema) = Interpretar(Ejecutar("status --json"));

                    if (accion == Accion.Conectar)
                    {
                        problema = "Tailscale está desconectado en este PC y no se ha podido conectar solo: ábrelo y pulsa «Connect».";
                    }
                }

                Problema = problema;
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "Tailscale.Comprobar");
            }
        }


        /// <summary>Qué hacer según lo que contesta «tailscale status --json».</summary>
        public static (Accion Accion, string Problema) Interpretar(string salida)
        {
            // Si el servicio no está en marcha, no contesta con JSON.
            if (string.IsNullOrWhiteSpace(salida) || !salida.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return (Accion.Avisar, SinServicio);
            }

            string estado;

            try
            {
                estado = JsonNode.Parse(salida)?["BackendState"]?.GetValue<string>() ?? "";
            }
            catch (System.Text.Json.JsonException)
            {
                return (Accion.Avisar, SinServicio);
            }

            return estado switch
            {
                "Stopped" => (Accion.Conectar, ""),
                "NeedsLogin" or "NeedsMachineAuth" => (Accion.Avisar, SinSesion),
                _ => (Accion.Nada, "")
            };
        }


        /// <summary>Abre la aplicación de Tailscale (su icono de la bandeja), para iniciar sesión o conectar.</summary>
        public static void AbrirAplicacion()
        {
            try
            {
                string aplicacion = Path.Combine(Carpeta, "tailscale-ipn.exe");

                if (File.Exists(aplicacion))
                {
                    Process.Start(new ProcessStartInfo(aplicacion) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                RegistroErrores.AnotarSilencioso(ex, "Tailscale.Abrir");
            }
        }


        // Lo que escribe «tailscale …» («» si no se pudo ejecutar o tardó demasiado).
        private static string Ejecutar(string argumentos)
        {
            var inicio = new ProcessStartInfo(Path.Combine(Carpeta, "tailscale.exe"), argumentos)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process proceso = Process.Start(inicio);

            if (proceso is null)
            {
                return "";
            }

            Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
            Task<string> errores = proceso.StandardError.ReadToEndAsync();

            if (!proceso.WaitForExit(20000))
            {
                try
                {
                    proceso.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // Ya había terminado.
                }

                return "";
            }

            _ = errores.Result;

            return salida.Result;
        }
    }
}
