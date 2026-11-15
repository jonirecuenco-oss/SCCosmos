using System;
using System.Collections.Generic;

namespace SCCosmos
{
    /// <summary>
    /// Una tarea en el Calendario (botón «Tareas»): «☐ dom 15 · 17:00 ·
    /// Llamar a Pedro», el mismo texto que en el móvil.
    /// </summary>
    public sealed class TareaCalendarioFila
    {
        public TareaCalendarioFila(Tarea tarea, List<DateTime> dias)
        {
            Tarea = tarea;
            Texto = SCCosmos.Sincro.FechasTarea.Texto(tarea.Completada, dias, tarea.TodoElDia ? "" : tarea.Hora, tarea.Titulo);
        }

        public Tarea Tarea { get; }

        public string Texto { get; }

        // Las hechas, atenuadas (como en Tareas).
        public double Opacidad => Tarea.Completada ? 0.5 : 1;
    }
}
