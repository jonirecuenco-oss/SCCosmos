using System;

namespace SCCosmos
{
    /// <summary>
    /// Cálculo de la ocurrencia de una tarea cuyo aviso ya ha vencido
    /// (con repeticiones y rangos).
    /// </summary>
    public static class TareasAviso
    {
        public static DateTime? OcurrenciaAvisoVencida(
            Tarea tarea,
            DateTime ahora)
        {
            if (string.IsNullOrWhiteSpace(tarea?.FechaInicio))
            {
                return null;
            }

            if (!DateTime.TryParse(
                    tarea.FechaInicio,
                    out DateTime inicio))
            {
                return null;
            }

            if (tarea.AvisoMinutos < 0)
            {
                return null;
            }

            DateTime? finRango =
                null;

            if (!string.IsNullOrWhiteSpace(tarea.FechaFin)
                && DateTime.TryParse(
                    tarea.FechaFin,
                    out DateTime fin))
            {
                finRango =
                    fin.Date;
            }

            int antelacion =
                Math.Max(
                    0,
                    tarea.AvisoMinutos);

            DateTime? mejor =
                null;

            DateTime fecha =
                inicio.Date;

            for (int i = 0; i < 4000; i++)
            {
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

                DateTime cita =
                    fecha.Date + hora;

                DateTime aviso =
                    cita.AddMinutes(-antelacion);

                if (aviso > ahora)
                {
                    break;
                }

                if (finRango.HasValue
                    && fecha.Date > finRango.Value)
                {
                    break;
                }

                mejor =
                    cita;

                switch ((tarea.Repeticion ?? "Ninguna").Trim())
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
                        return mejor;
                }
            }

            return mejor;
        }
    }
}
