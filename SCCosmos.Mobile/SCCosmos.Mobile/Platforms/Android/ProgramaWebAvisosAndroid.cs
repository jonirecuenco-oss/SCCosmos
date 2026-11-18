using System.Text.Json.Nodes;

using Android.App;
using Android.Content;
using Android.OS;

using AndroidX.Core.App;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Notificación cuando los ancianos pulsan «Enviar» en el formulario del
/// programa (página Avisos: «Programa enviado por los ancianos»). Con la app
/// abierta llega con la sincronización, al momento; con la app cerrada, la
/// revisión de cada media hora (TiempoAlarmReceiver) se lo pregunta al
/// servidor del formulario (…/programa/{código}/estado). Cada envío se avisa
/// una vez; si lo vuelven a enviar, se avisa otra vez.
/// </summary>
public static class ProgramaWebAvisosAndroid
{
    private const string Canal =
        "sccosmos_programa_web";

    // «{congregación}_{mes}\t{yyyy-MM-ddTHH:mm:ss}»: el último envío avisado de cada visita.
    private const string ClaveNotificados =
        "ProgramaWeb_EnviosNotificados";

    private const int IdAbrir =
        4813;

    // Lo enviado hace más tiempo no se avisa (por ejemplo, al instalar esta versión).
    private static readonly TimeSpan Antiguedad =
        TimeSpan.FromDays(3);

    private static readonly HttpClient Http =
        new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly SemaphoreSlim Turno =
        new(1, 1);


    /// <summary>
    /// Mira si hay envíos nuevos y los notifica. preguntarServidor: además de
    /// lo sincronizado, preguntarlo al servidor del formulario (con la app cerrada).
    /// </summary>
    public static async Task RevisarAsync(
        bool preguntarServidor,
        CancellationToken token = default)
    {
        // Lo sincronizado llega a menudo: si ya hay una revisión en marcha, basta con ella.
        try
        {
            if (!await Turno.WaitAsync(preguntarServidor ? Timeout.Infinite : 0, token))
            {
                return;
            }
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        try
        {
            await RevisarYaAsync(preguntarServidor, token);
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "ProgramaWeb.Avisos");
        }
        finally
        {
            Turno.Release();
        }
    }


    private static async Task RevisarYaAsync(
        bool preguntarServidor,
        CancellationToken token)
    {
        // Como los demás avisos: la esposa del SC no recibe notificaciones.
        if (!PerfilAccesoService.EsHombre
            || !AjustesAvisos.Leer(clave => DatabaseHelper.ObtenerConfiguracion(clave, "")).ProgramaWebActivo)
        {
            return;
        }

        // El enlace vale hasta 15 días después de la visita: basta con este mes, el anterior y los siguientes.
        string desde =
            DateTime.Today.AddMonths(-1).ToString("yyyy-MM");

        var enlaces =
            DatabaseHelper.EnlacesProgramaWeb()
                .Where(e => string.CompareOrdinal(e.Mes, desde) >= 0)
                .ToList();

        if (enlaces.Count == 0)
        {
            return;
        }

        string direccion =
            preguntarServidor
                ? ProgramaWeb.DireccionEnlaces(clave => DatabaseHelper.ObtenerConfiguracion(clave, ""))
                : "";

        // «localhost» sería este móvil, no el PC.
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out Uri? servidor) || servidor.IsLoopback)
        {
            direccion = "";
        }

        Dictionary<string, string> nombres =
            DatabaseHelper.ObtenerCongregaciones()
                .GroupBy(c => c.Codigo)
                .ToDictionary(g => g.Key, g => g.First().Nombre);

        Dictionary<string, string> avisados =
            LeerAvisados(desde);

        foreach (var enlace in enlaces)
        {
            (DateTime Cuando, int Archivos)? enviado =
                ProgramaWeb.LeerEnviado(enlace.Enviado);

            if (direccion.Length > 0
                && await PreguntarAsync(direccion, enlace.Codigo, token) is { } remoto
                && (enviado is null || remoto.Cuando > enviado.Value.Cuando))
            {
                enviado = remoto;
            }

            if (enviado is not { } envio)
            {
                continue;
            }

            string visita =
                enlace.Congregacion + "_" + enlace.Mes;

            string marca =
                ProgramaWeb.TextoEnviado(envio.Cuando, 0)[..19];

            bool otraVez =
                avisados.TryGetValue(visita, out string? anterior);

            if (otraVez && string.CompareOrdinal(anterior, marca) >= 0)
            {
                continue;
            }

            avisados[visita] = marca;
            GuardarAvisados(avisados);

            if (DateTime.Now - envio.Cuando > Antiguedad)
            {
                continue;
            }

            string nombre =
                nombres.TryGetValue(enlace.Congregacion, out string? n) && !string.IsNullOrWhiteSpace(n)
                    ? n.Trim()
                    : enlace.Congregacion;

            Notificar(
                visita,
                ProgramaWeb.AvisoEnviado(nombre, enlace.Mes, envio.Archivos, otraVez));
        }
    }


    // null si no contesta (apagado, sin Internet, enlace caducado…).
    private static async Task<(DateTime Cuando, int Archivos)?> PreguntarAsync(
        string direccion,
        string codigo,
        CancellationToken token)
    {
        try
        {
            using HttpResponseMessage respuesta =
                await Http.GetAsync(ProgramaWeb.Enlace(direccion, codigo) + "/estado", token);

            if (!respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            JsonNode? estado =
                JsonNode.Parse(await respuesta.Content.ReadAsStringAsync(token));

            return ProgramaWeb.LeerEnviado(estado?["enviado"]?.GetValue<string>());
        }
        catch
        {
            return null;
        }
    }


    private static void Notificar(
        string visita,
        (string Titulo, string Texto) aviso)
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                ((NotificationManager?)contexto.GetSystemService(Context.NotificationService))
                    ?.CreateNotificationChannel(
                        new NotificationChannel(
                            Canal,
                            "Programa de los ancianos",
                            NotificationImportance.High)
                        {
                            Description = "Cuando los ancianos envían el formulario del programa de la visita"
                        });
            }

            var notificacion =
                new NotificationCompat.Builder(contexto, Canal)
                    .SetSmallIcon(contexto.ApplicationInfo?.Icon ?? 0)
                    .SetContentTitle(aviso.Titulo)
                    .SetContentText(aviso.Texto)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(aviso.Texto))
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetAutoCancel(true);

            Intent? abrir =
                contexto.PackageManager?.GetLaunchIntentForPackage(contexto.PackageName ?? "");

            if (abrir is not null
                && PendingIntent.GetActivity(
                    contexto,
                    IdAbrir,
                    abrir,
                    PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent) is PendingIntent alPulsar)
            {
                notificacion.SetContentIntent(alPulsar);
            }

            // Una por visita: si lo vuelven a enviar, sustituye a la anterior.
            NotificationManagerCompat.From(contexto).Notify(
                IdDe(visita),
                notificacion.Build());
        }
        catch
        {
            // Sin permiso de notificaciones: no hay nada más que hacer.
        }
    }


    // Sin las visitas de meses que ya no se miran.
    private static Dictionary<string, string> LeerAvisados(string desde)
    {
        var avisados = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string linea in Preferences.Default.Get(ClaveNotificados, "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = linea.IndexOf('\t');

            if (tab > 0
                && string.CompareOrdinal(linea[(linea.LastIndexOf('_', tab) + 1)..tab], desde) >= 0)
            {
                avisados[linea[..tab]] = linea[(tab + 1)..];
            }
        }

        return avisados;
    }


    private static void GuardarAvisados(Dictionary<string, string> avisados) =>
        Preferences.Default.Set(
            ClaveNotificados,
            string.Join("\n", avisados.Select(a => a.Key + "\t" + a.Value)));


    // Id estable (GetHashCode cambia en cada arranque); aparte de los del tiempo (50000-89999).
    private static int IdDe(string clave)
    {
        uint hash = 2166136261;

        foreach (char c in clave)
        {
            hash = (hash ^ c) * 16777619;
        }

        return 91000 + (int)(hash % 8000);
    }
}
