#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SCCosmos.Sincro;

/// <summary>
/// Qué avisos están activos y cuándo (se editan en la página Avisos del
/// móvil). Van en Configuracion, que se sincroniza: la campana de Windows
/// usa los mismos plazos. Sin nada guardado, como estaba: las alertas
/// activas con sus plazos de siempre, el tiempo activo y los recordatorios
/// con hora (quedar con el CCA, el S-303 del domingo) apagados.
/// </summary>
public sealed class AjustesAvisos
{
    /// <summary>Las alertas de la lista (Inicio y campana): tipo, nombre, plazo de siempre y de qué.</summary>
    public static readonly IReadOnlyList<(string Tipo, string Nombre, int Dias, string Plazo)> Alertas =
        new[]
        {
            ("S302", "S-302 pendiente", 90, "días antes de la visita"),
            ("Documentacion", "Documentación pendiente", 42, "días antes de la visita"),
            ("S303", "S-303 pendiente", 14, "días después de la visita"),
            ("Recordatorio", "Recordatorio de asamblea", 60, "días antes de la asamblea"),
            ("RepBetel", "Mensaje al representante de Betel (AC-BR)", 30, "días antes de la asamblea"),
            ("JWHub", "Revisión de JW Hub pendiente", 0, "")
        };

    // «1|4|18:00»: activo, día de la semana (DayOfWeek) y hora.
    private const string ClaveCca = "Avisos_CCA";

    // «1|20:00»: activo y hora del domingo.
    private const string ClaveS303 = "Avisos_S303Domingo";

    // «1» o «0».
    private const string ClaveTiempo = "Avisos_Tiempo";

    // + tipo: «1|90», activa y días.
    private const string PrefijoAlerta = "Avisos_Alerta_";

    private readonly Dictionary<string, (bool Activa, int Dias)> _alertas =
        Alertas.ToDictionary(a => a.Tipo, a => (true, a.Dias));

    public bool CcaActivo { get; set; }

    public DayOfWeek CcaDia { get; set; } = DayOfWeek.Thursday;

    public TimeSpan CcaHora { get; set; } = new(18, 0, 0);

    public bool S303Activo { get; set; }

    public TimeSpan S303Hora { get; set; } = new(20, 0, 0);

    public bool TiempoActivo { get; set; } = true;


    public bool AlertaActiva(string tipo) =>
        !_alertas.TryGetValue(tipo, out var alerta) || alerta.Activa;


    public int DiasAlerta(string tipo) =>
        _alertas.TryGetValue(tipo, out var alerta) ? alerta.Dias : 0;


    public void PonerAlerta(string tipo, bool activa, int dias) =>
        _alertas[tipo] = (activa, Math.Clamp(dias, 0, 365));


    /// <summary>Con la lectura de Configuracion de cada app («» si no está).</summary>
    public static AjustesAvisos Leer(Func<string, string> leer)
    {
        var ajustes = new AjustesAvisos();

        string[] cca = (leer(ClaveCca) ?? "").Split('|');

        if (cca.Length == 3)
        {
            ajustes.CcaActivo = cca[0] == "1";

            if (int.TryParse(cca[1], out int dia) && dia is >= 0 and <= 6)
            {
                ajustes.CcaDia = (DayOfWeek)dia;
            }

            if (TimeSpan.TryParse(cca[2], CultureInfo.InvariantCulture, out TimeSpan hora))
            {
                ajustes.CcaHora = hora;
            }
        }

        string[] s303 = (leer(ClaveS303) ?? "").Split('|');

        if (s303.Length == 2)
        {
            ajustes.S303Activo = s303[0] == "1";

            if (TimeSpan.TryParse(s303[1], CultureInfo.InvariantCulture, out TimeSpan hora))
            {
                ajustes.S303Hora = hora;
            }
        }

        ajustes.TiempoActivo = leer(ClaveTiempo) != "0";

        foreach (var alerta in Alertas)
        {
            string[] valor = (leer(PrefijoAlerta + alerta.Tipo) ?? "").Split('|');

            if (valor.Length == 2 && int.TryParse(valor[1], out int dias))
            {
                ajustes.PonerAlerta(alerta.Tipo, valor[0] == "1", dias);
            }
        }

        return ajustes;
    }


    /// <summary>Las claves y valores para guardar en Configuracion.</summary>
    public IEnumerable<(string Clave, string Valor)> Valores()
    {
        static string Hora(TimeSpan hora) =>
            hora.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        yield return (ClaveCca, $"{(CcaActivo ? 1 : 0)}|{(int)CcaDia}|{Hora(CcaHora)}");
        yield return (ClaveS303, $"{(S303Activo ? 1 : 0)}|{Hora(S303Hora)}");
        yield return (ClaveTiempo, TiempoActivo ? "1" : "0");

        foreach (var alerta in Alertas)
        {
            var valor = _alertas[alerta.Tipo];

            yield return (PrefijoAlerta + alerta.Tipo, $"{(valor.Activa ? 1 : 0)}|{valor.Dias}");
        }
    }


    /// <summary>
    /// El día de avisar de quedar con el CCA: ese día de la semana dentro de
    /// los 7 anteriores al martes de la visita (el martes, el de la semana antes).
    /// </summary>
    public static DateTime DiaCca(DateTime martesVisita, DayOfWeek dia)
    {
        int atras = ((int)martesVisita.DayOfWeek - (int)dia + 7) % 7;

        return martesVisita.Date.AddDays(-(atras == 0 ? 7 : atras));
    }
}
