using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SCCosmos
{
    // =========================================================
    // AVISOS DEL SISTEMA EN WINDOWS
    //
    // Equivalente a las notificaciones de Android: globos de la
    // bandeja del sistema para los avisos de tareas (a su hora y,
    // si la tarea tiene aviso previo, también con esa antelación)
    // y para las alertas nuevas. El registro de avisos ya
    // mostrados es local de este equipo.
    // =========================================================

    public static class AvisosWindows
    {
        private static System.Windows.Forms.NotifyIcon? _icono;

        private static HashSet<string>? _avisadas;

        private static string _firmaAlertas = "";

        private static bool _estadoCargado;

        private static bool _ultimoFueTarea;

        private static readonly object _candado =
            new();


        private sealed class Estado
        {
            public List<string> Avisadas
            { get; set; } = new();

            public string FirmaAlertas
            { get; set; } = "";
        }


        private static string Ruta =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SCCosmos",
                "avisos_pc.json");


        private static void CargarEstado()
        {
            if (_estadoCargado)
            {
                return;
            }

            _estadoCargado =
                true;

            try
            {
                if (File.Exists(Ruta))
                {
                    var estado =
                        JsonSerializer.Deserialize<Estado>(
                            File.ReadAllText(Ruta));

                    if (estado != null)
                    {
                        _avisadas =
                            new HashSet<string>(
                                estado.Avisadas
                                ?? new List<string>(),
                                StringComparer.Ordinal);

                        _firmaAlertas =
                            estado.FirmaAlertas ?? "";

                        return;
                    }
                }
            }
            catch
            {
            }

            _avisadas ??=
                new HashSet<string>(
                    StringComparer.Ordinal);
        }


        private static void GuardarEstado()
        {
            try
            {
                var estado =
                    new Estado
                    {
                        Avisadas =
                            (_avisadas
                             ?? new HashSet<string>())
                            .ToList(),

                        FirmaAlertas =
                            _firmaAlertas
                    };

                File.WriteAllText(
                    Ruta,
                    JsonSerializer.Serialize(
                        estado,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
            }
            catch
            {
            }
        }


        /// <summary>
        /// Procesa los avisos pendientes de tareas y los títulos de las
        /// alertas actuales. Se llama desde la comprobación de cada minuto
        /// (y al activar la ventana).
        /// </summary>
        public static void Procesar(
            List<string> titulosAlertas)
        {
            try
            {
                lock (_candado)
                {
                    CargarEstado();

                    AvisarTareas();

                    AvisarAlertas(
                        titulosAlertas ?? new List<string>());
                }
            }
            catch
            {
            }
        }


        private static void AsegurarIcono()
        {
            if (_icono != null)
            {
                return;
            }

            try
            {
                System.Drawing.Icon? iconoAplicacion =
                    null;

                try
                {
                    iconoAplicacion =
                        System.Drawing.Icon
                            .ExtractAssociatedIcon(
                                Environment.ProcessPath
                                ?? "");
                }
                catch
                {
                }

                _icono =
                    new System.Windows.Forms.NotifyIcon
                    {
                        Icon =
                            iconoAplicacion
                            ?? System.Drawing.SystemIcons
                                .Application,
                        Text = "SCCosmos",
                        Visible = true
                    };

                _icono.BalloonTipClicked += (s, e) =>
                {
                    try
                    {
                        var ventana =
                            System.Windows.Application
                                .Current?.MainWindow;

                        if (ventana == null)
                        {
                            return;
                        }

                        if (ventana.WindowState
                            == System.Windows.WindowState
                                .Minimized)
                        {
                            ventana.WindowState =
                                System.Windows.WindowState
                                    .Normal;
                        }

                        ventana.Activate();

                        if (_ultimoFueTarea
                            && ventana is MainWindow principal)
                        {
                            principal.MenuPrincipal.Navigate(
                                typeof(TareasPage));
                        }
                    }
                    catch
                    {
                    }
                };
            }
            catch
            {
            }
        }


        private static void Mostrar(
            string titulo,
            string texto,
            bool esTarea)
        {
            AsegurarIcono();

            if (_icono == null)
            {
                return;
            }

            try
            {
                _ultimoFueTarea =
                    esTarea;

                _icono.BalloonTipTitle =
                    titulo;

                _icono.BalloonTipText =
                    texto;

                _icono.BalloonTipIcon =
                    esTarea
                        ? System.Windows.Forms.ToolTipIcon
                            .Warning
                        : System.Windows.Forms.ToolTipIcon
                            .Info;

                _icono.ShowBalloonTip(10000);
            }
            catch
            {
            }
        }


        /// <summary>
        /// Avisos de tareas: a la hora de la cita y, con aviso previo,
        /// también con la antelación configurada. Una sola vez por
        /// ocurrencia (se miran las últimas 24 horas).
        /// </summary>
        private static void AvisarTareas()
        {
            DateTime ahora =
                DateTime.Now;

            DateTime limite =
                ahora.AddHours(-24);

            var nuevas =
                new List<(string Clave, string Titulo, string Texto)>();

            foreach (Tarea tarea in DatabaseHelper.ObtenerTareas())
            {
                if (tarea.Completada
                    || string.IsNullOrWhiteSpace(
                        tarea.FechaInicio)
                    || !DateTime.TryParse(
                        tarea.FechaInicio,
                        out DateTime inicio))
                {
                    continue;
                }

                DateTime? finRango =
                    null;

                if (!string.IsNullOrWhiteSpace(
                        tarea.FechaFin)
                    && DateTime.TryParse(
                        tarea.FechaFin,
                        out DateTime fin))
                {
                    finRango =
                        fin.Date;
                }

                TimeSpan hora =
                    TimeSpan.Zero;

                if (!tarea.TodoElDia
                    && TimeSpan.TryParse(
                        (tarea.Hora ?? "").Trim(),
                        out TimeSpan horaParseada))
                {
                    hora =
                        horaParseada;
                }

                int antelacion =
                    tarea.AvisoMinutos > 0
                        ? tarea.AvisoMinutos
                        : 0;

                string titulo =
                    string.IsNullOrWhiteSpace(
                        tarea.Titulo)
                        ? "Tarea"
                        : tarea.Titulo.Trim();

                DateTime fecha =
                    inicio.Date;

                for (int i = 0; i < 4000; i++)
                {
                    if (finRango.HasValue
                        && fecha.Date > finRango.Value)
                    {
                        break;
                    }

                    DateTime cita =
                        fecha.Date + hora;

                    if (cita > ahora.AddDays(2))
                    {
                        break;
                    }

                    string claveHora =
                        $"{tarea.Id}|{cita:yyyyMMddHHmm}|hora";

                    if (cita >= limite
                        && cita <= ahora
                        && !_avisadas!.Contains(claveHora))
                    {
                        string cuando =
                            cita.Date == ahora.Date
                                ? "Hoy"
                                : cita.Date
                                  == ahora.Date
                                      .AddDays(-1)
                                    ? "Ayer"
                                    : cita.ToString(
                                        "dd MMM");

                        nuevas.Add((
                            claveHora,
                            "⏰ " + titulo,
                            $"{cuando} a las {cita:HH:mm}"));
                    }

                    if (antelacion > 0)
                    {
                        DateTime avisoPrevio =
                            cita.AddMinutes(
                                -antelacion);

                        string clavePrevio =
                            $"{tarea.Id}|{cita:yyyyMMddHHmm}|previo";

                        if (avisoPrevio >= limite
                            && avisoPrevio <= ahora
                            && !_avisadas!.Contains(
                                clavePrevio))
                        {
                            nuevas.Add((
                                clavePrevio,
                                "⏰ " + titulo,
                                $"En {antelacion} min ({cita:HH:mm})"));
                        }
                    }

                    switch ((tarea.Repeticion
                             ?? "Ninguna").Trim())
                    {
                        case "Diaria":
                            fecha =
                                fecha.AddDays(1);
                            break;

                        case "Semanal":
                            fecha =
                                fecha.AddDays(7);
                            break;

                        case "Mensual":
                            fecha =
                                fecha.AddMonths(1);
                            break;

                        case "Anual":
                            fecha =
                                fecha.AddYears(1);
                            break;

                        default:
                            i = 4000;
                            break;
                    }
                }
            }

            if (nuevas.Count == 0)
            {
                return;
            }

            foreach (var aviso in nuevas)
            {
                Mostrar(
                    aviso.Titulo,
                    aviso.Texto,
                    true);

                _avisadas!.Add(
                    aviso.Clave);
            }

            GuardarEstado();
        }


        /// <summary>
        /// Aviso de alertas nuevas (S-302, documentación, asambleas,
        /// JW Hub…): un solo globo cuando cambia el conjunto.
        /// </summary>
        private static void AvisarAlertas(
            List<string> titulos)
        {
            var relevantes =
                titulos
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();

            string firma =
                string.Join(
                    "\u0001",
                    relevantes
                        .OrderBy(
                            t => t,
                            StringComparer.Ordinal));

            if (firma.Length == 0
                || string.Equals(
                    firma,
                    _firmaAlertas,
                    StringComparison.Ordinal))
            {
                return;
            }

            _firmaAlertas =
                firma;

            string texto =
                relevantes.Count == 1
                    ? relevantes[0]
                    : $"{relevantes.Count} alertas: "
                      + string.Join(
                          ", ",
                          relevantes.Take(3))
                      + (relevantes.Count > 3
                          ? "…"
                          : "");

            Mostrar(
                "🔔 SCCosmos",
                texto,
                false);

            GuardarEstado();
        }


        /// <summary>Oculta el icono de la bandeja al cerrar la app.</summary>
        public static void Cerrar()
        {
            try
            {
                if (_icono != null)
                {
                    _icono.Visible =
                        false;

                    _icono.Dispose();

                    _icono =
                        null;
                }
            }
            catch
            {
            }
        }
    }
}
