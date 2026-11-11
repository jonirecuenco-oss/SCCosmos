using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Los recordatorios con hora de la página Avisos: quedar con el CCA (el día
/// y la hora elegidos de la semana antes de una visita) y el envío del S-303
/// (el domingo de la visita, salvo las de SCS). Una alarma por el siguiente;
/// al sonar se publica lo que toque y se programa el que sigue.
/// </summary>
public static class RecordatoriosVisitaService
{
    // Los ya avisados: «CCA|2026-11-17;S303|2026-11-17» (el martes de la visita).
    private const string ClaveAvisados =
        "Avisos_RecordatoriosAvisados";

    private static readonly CultureInfo Es =
        CultureInfo.GetCultureInfo("es-ES");


    public sealed record Recordatorio(
        DateTime Cuando,
        string Clave,
        string Titulo,
        string Texto);


    // Como la lista de alertas: son del SC (sin perfil todavía, también); la
    // esposa no los recibe.
    private static bool EsDelSc =>
        !PerfilAccesoService.Configurado
        || PerfilAccesoService.EsHombre;


    public static bool HayActivos()
    {
        AjustesAvisos ajustes =
            AjustesAvisos.Leer(clave => DatabaseHelper.ObtenerConfiguracion(clave, ""));

        return EsDelSc
               && (ajustes.CcaActivo || ajustes.S303Activo);
    }


    /// <summary>Los de las visitas desde la semana pasada hasta dentro de 11 semanas.</summary>
    public static List<Recordatorio> Calcular(DateTime ahora)
    {
        var lista = new List<Recordatorio>();

        if (!EsDelSc)
        {
            return lista;
        }

        AjustesAvisos ajustes =
            AjustesAvisos.Leer(clave => DatabaseHelper.ObtenerConfiguracion(clave, ""));

        if (!ajustes.CcaActivo && !ajustes.S303Activo)
        {
            return lista;
        }

        List<SemanaCalendario> semanas =
            DatabaseHelper.ObtenerSemanasEnRangoMobile(
                ahora.Date.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ahora.Date.AddDays(77).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        foreach (SemanaCalendario semana in semanas)
        {
            if (!string.Equals(semana.Actividad, "Visita", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(semana.Congregacion))
            {
                continue;
            }

            DateTime martes =
                semana.FechaMartes.Date;

            string dia =
                martes.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (ajustes.CcaActivo)
            {
                lista.Add(new Recordatorio(
                    AjustesAvisos.DiaCca(martes, ajustes.CcaDia).Add(ajustes.CcaHora),
                    "CCA|" + dia,
                    "Quedar con el CCA · " + semana.Congregacion,
                    $"La visita empieza el {martes.ToString("dddd d 'de' MMMM", Es)}: queda con el coordinador."));
            }

            // El S-303 no va en las visitas con el SCS (como en Windows).
            if (ajustes.S303Activo
                && !(semana.Tipo ?? "").Contains("SCS", StringComparison.OrdinalIgnoreCase))
            {
                lista.Add(new Recordatorio(
                    martes.AddDays(5).Add(ajustes.S303Hora),
                    "S303|" + dia,
                    "Envío del S-303 · " + semana.Congregacion,
                    "Hoy termina la visita: prepara y envía el S-303."));
            }
        }

        return lista
            .OrderBy(r => r.Cuando)
            .ToList();
    }


    /// <summary>
    /// Publica los que ya tocan (de las últimas 12 horas, por si el móvil
    /// estaba apagado) y aún no se han avisado. Devuelve cuándo toca el siguiente.
    /// </summary>
    public static DateTime? Verificar(
        DateTime ahora,
        Action<Recordatorio> publicar)
    {
        List<Recordatorio> lista =
            Calcular(ahora);

        HashSet<string> avisados =
            LeerAvisados();

        foreach (Recordatorio recordatorio in lista)
        {
            if (recordatorio.Cuando <= ahora
                && recordatorio.Cuando > ahora.AddHours(-12)
                && avisados.Add(recordatorio.Clave))
            {
                publicar(recordatorio);
            }
        }

        GuardarAvisados(avisados, ahora);

        return lista
            .Where(r => r.Cuando > ahora && !avisados.Contains(r.Clave))
            .Select(r => (DateTime?)r.Cuando)
            .FirstOrDefault();
    }


    private static HashSet<string> LeerAvisados() =>
        Preferences.Default.Get(ClaveAvisados, "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);


    // Se olvidan los de visitas de hace más de un mes.
    private static void GuardarAvisados(
        HashSet<string> avisados,
        DateTime ahora)
    {
        string limite =
            ahora.Date.AddDays(-31).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Preferences.Default.Set(
            ClaveAvisados,
            string.Join(
                ";",
                avisados.Where(a => string.CompareOrdinal(a[(a.IndexOf('|') + 1)..], limite) >= 0)));
    }
}
