using System.Globalization;

using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Motor de alertas para Android. Se basa en la semana de visita y el
/// estado de los envíos almacenados en la tabla Semanas, más la
/// revisión pendiente de JW Hub. No reproduce las alertas específicas
/// de escritorio (descargas, correo de Betel vía OWA).
/// </summary>
public static class AlertasMobileService
{
    private const string ClaveLeidas =
        "SCCosmos_AlertasLeidas";

    private static string Firma(
        AlertaMobile alerta)
    {
        return alerta.Tipo + "|" +
               alerta.Titulo + "|" +
               alerta.Descripcion;
    }

    private static HashSet<string> ObtenerLeidas()
    {
        try
        {
            string texto =
                Preferences.Default.Get(
                    ClaveLeidas,
                    "");

            if (string.IsNullOrWhiteSpace(
                    texto))
            {
                return new HashSet<string>(
                    StringComparer.Ordinal);
            }

            return new HashSet<string>(
                texto.Split(
                    '\u0001',
                    StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(
                StringComparer.Ordinal);
        }
    }

    public static void MarcarTodasLeidas(
        IEnumerable<AlertaMobile> alertas)
    {
        HashSet<string> leidas =
            ObtenerLeidas();

        foreach (AlertaMobile alerta in alertas)
        {
            leidas.Add(
                Firma(alerta));
        }

        Preferences.Default.Set(
            ClaveLeidas,
            string.Join(
                "\u0001",
                leidas));
    }

    public static List<AlertaMobile> Calcular(
        int semanasAtras = 6,
        int semanasAdelante = 8)
    {
        var alertas =
            new List<AlertaMobile>();

        DateTime hoy =
            DateTime.Today;

        DateTime martesActual =
            ObtenerMartesActual();

        for (int w = -semanasAtras;
             w <= semanasAdelante;
             w++)
        {
            DateTime fecha =
                martesActual.AddDays(w * 7);

            string iso =
                fecha.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            List<SemanaCalendario> semanas;

            try
            {
                semanas =
                    DatabaseHelper
                        .ObtenerTodasSemanasPorFecha(
                            iso);
            }
            catch
            {
                continue;
            }

            foreach (SemanaCalendario semana in semanas)
            {
                AnadirAlertasSemana(
                    alertas,
                    semana,
                    hoy);
            }
        }

#if ANDROID
        try
        {
            if (SCCosmos.Mobile.Platforms
                    .Android
                    .JwHubSyncReviewStore
                    .ExisteRevisionPendiente)
            {
                alertas.Insert(
                    0,
                    new AlertaMobile
                    {
                        Tipo = "JWHub",
                        Titulo =
                            "Revisión de JW Hub pendiente",
                        Descripcion =
                            "Hay una sintonización de JW Hub sin " +
                            "revisar. Ábrela desde Personas → " +
                            "Revisar Hub."
                    });
            }
        }
        catch
        {
            // Sin revisión persistida accesible.
        }
#endif

        HashSet<string> leidas =
            ObtenerLeidas();

        return alertas
            .Where(a => !leidas.Contains(Firma(a)))
            .ToList();
    }

    private static void AnadirAlertasSemana(
        List<AlertaMobile> alertas,
        SemanaCalendario semana,
        DateTime hoy)
    {
        bool esVisita =
            string.Equals(
                semana.Actividad,
                "Visita",
                StringComparison.OrdinalIgnoreCase)
            &&
            !string.IsNullOrWhiteSpace(
                semana.Congregacion);

        bool esAsamblea =
            string.Equals(
                semana.Actividad,
                "Asamblea de circuito",
                StringComparison.OrdinalIgnoreCase);

        DateTime finSemana =
            semana.FechaMartes.AddDays(5);

        bool futura =
            finSemana >= hoy;

        if (esVisita)
        {
            if (futura
                &&
                !semana.S302Enviado)
            {
                alertas.Add(
                    new AlertaMobile
                    {
                        Tipo = "S302",
                        Titulo =
                            $"S-302 pendiente · {semana.Congregacion}",
                        Descripcion =
                            $"Visita: {semana.RangoFechas}. " +
                            "Aún no se ha marcado el S-302 como enviado."
                    });
            }

            if (futura
                &&
                !semana.DocEnviada)
            {
                alertas.Add(
                    new AlertaMobile
                    {
                        Tipo = "Documentacion",
                        Titulo =
                            $"Documentación pendiente · {semana.Congregacion}",
                        Descripcion =
                            $"Visita: {semana.RangoFechas}. " +
                            "La documentación no se ha marcado como enviada."
                    });
            }

            if (!futura
                &&
                FaltaS303(semana))
            {
                alertas.Add(
                    new AlertaMobile
                    {
                        Tipo = "S303",
                        Titulo =
                            $"S-303 pendiente · {semana.Congregacion}",
                        Descripcion =
                            $"Visita: {semana.RangoFechas}. Ya " +
                            "terminó y no hay S-303 guardado."
                    });
            }
        }

        if (esAsamblea
            &&
            futura
            &&
            !semana.RecordatorioEnviado)
        {
            alertas.Add(
                new AlertaMobile
                {
                    Tipo = "Recordatorio",
                    Titulo =
                        "Recordatorio de asamblea pendiente",
                    Descripcion =
                        $"Asamblea: {semana.RangoFechas}" +
                        (string.IsNullOrWhiteSpace(
                             semana.SeccionAsamblea)
                            ? "."
                            : $" (sección {semana.SeccionAsamblea}).")
                });
        }
    }

    private static bool FaltaS303(
        SemanaCalendario semana)
    {
        try
        {
            string codigo =
                DatabaseHelper
                    .ObtenerCodigoCongregacion(
                        semana.Congregacion);

            if (string.IsNullOrWhiteSpace(
                    codigo))
            {
                return false;
            }

            string mes =
                $"{semana.FechaMartes.Year:D4}-" +
                $"{semana.FechaMartes.Month:D2}";

            return DatabaseHelper
                       .ObtenerS303VisitaMobile(
                           codigo,
                           mes)
                   is null;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime ObtenerMartesActual()
    {
        DateTime hoy =
            DateTime.Today;

        int diferencia =
            (int)hoy.DayOfWeek -
            (int)DayOfWeek.Tuesday;

        if (diferencia < 0)
        {
            diferencia += 7;
        }

        return hoy
            .AddDays(-diferencia)
            .Date;
    }
}
