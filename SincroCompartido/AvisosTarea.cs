#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace SCCosmos.Sincro;

/// <summary>
/// Los avisos que se pueden poner a una tarea (minutos de antelación; -1,
/// sin aviso), los mismos en el móvil y en Windows: la tarea se sincroniza
/// y cada app tiene que saber enseñar el que puso la otra.
/// </summary>
public static class AvisosTarea
{
    public static readonly IReadOnlyList<(int Minutos, string Texto)> Opciones =
        new[]
        {
            (-1, "Sin aviso"),
            (0, "A la hora"),
            (10, "10 minutos antes"),
            (30, "30 minutos antes"),
            (60, "1 hora antes"),
            (120, "2 horas antes"),
            (1440, "1 día antes"),
            (2880, "2 días antes"),
            (10080, "1 semana antes"),
            (20160, "2 semanas antes"),
            (43200, "1 mes antes")
        };


    public static string[] Textos =>
        Opciones.Select(o => o.Texto).ToArray();


    public static int[] Minutos =>
        Opciones.Select(o => o.Minutos).ToArray();


    /// <summary>«1 semana antes»; uno que no esté en la lista, en días, horas o minutos.</summary>
    public static string Texto(int minutos)
    {
        foreach (var opcion in Opciones)
        {
            if (opcion.Minutos == minutos)
            {
                return opcion.Texto;
            }
        }

        return minutos % 1440 == 0 ? $"{minutos / 1440} días antes"
            : minutos % 60 == 0 ? $"{minutos / 60} horas antes"
            : $"{minutos} minutos antes";
    }
}
