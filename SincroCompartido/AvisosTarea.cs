#nullable enable

using System;
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


    /// <summary>La última opción de los desplegables: cualquier antelación.</summary>
    public const string TextoPersonalizado = "Personalizado…";

    /// <summary>Lo más que se puede adelantar un aviso personalizado: un año.</summary>
    public const int MaxMinutos = 365 * 1440;

    /// <summary>Las unidades del aviso personalizado, de menor a mayor.</summary>
    public static readonly IReadOnlyList<(string Plural, string Singular, int Minutos)> Unidades =
        new[]
        {
            ("minutos", "minuto", 1),
            ("horas", "hora", 60),
            ("días", "día", 1440),
            ("semanas", "semana", 10080)
        };


    public static string[] TextosUnidades =>
        Unidades.Select(u => u.Plural).ToArray();


    /// <summary>Si está en la lista; si no (y hay aviso), es personalizado.</summary>
    public static bool EsDeLaLista(int minutos) =>
        Opciones.Any(o => o.Minutos == minutos);


    /// <summary>En la unidad más grande que lo divide: 4320 → (3, días); 90 → (90, minutos).</summary>
    public static (int Cantidad, int Unidad) Descomponer(int minutos)
    {
        for (int i = Unidades.Count - 1; i > 0; i--)
        {
            if (minutos > 0 && minutos % Unidades[i].Minutos == 0)
            {
                return (minutos / Unidades[i].Minutos, i);
            }
        }

        return (Math.Max(minutos, 1), 0);
    }


    /// <summary>Los minutos de «3 días»; -1 si no vale (de 1 minuto a 1 año).</summary>
    public static int MinutosDe(int cantidad, int unidad)
    {
        if (cantidad < 1 || unidad < 0 || unidad >= Unidades.Count)
        {
            return -1;
        }

        long minutos = (long)cantidad * Unidades[unidad].Minutos;

        return minutos <= MaxMinutos ? (int)minutos : -1;
    }


    /// <summary>«1 semana antes»; uno que no esté en la lista, en semanas, días, horas o minutos.</summary>
    public static string Texto(int minutos)
    {
        foreach (var opcion in Opciones)
        {
            if (opcion.Minutos == minutos)
            {
                return opcion.Texto;
            }
        }

        // Sin aviso es -1, pero cualquier negativo se trata igual al avisar.
        if (minutos < 0)
        {
            return Opciones[0].Texto;
        }

        var (cantidad, unidad) = Descomponer(minutos);

        return $"{cantidad} {(cantidad == 1 ? Unidades[unidad].Singular : Unidades[unidad].Plural)} antes";
    }
}
