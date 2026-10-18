using System.Globalization;

namespace SCCosmos.Mobile.Models;

/// <summary>
/// Una escuela (clase) de una semana completa, de lunes a sábado.
/// </summary>
public class EscuelaClaseMobile
{
    public string Id { get; set; } = "";
    public string SemanaInicio { get; set; } = "";
    public string Congregacion { get; set; } = "";
    public int InstructorId { get; set; }
    public string InstructorNombre { get; set; } = "";
    public int SustitutoId { get; set; }
    public string SustitutoNombre { get; set; } = "";
    public int Asientos { get; set; } = 12;
    public string Notas { get; set; } = "";
    public string FechaCreacion { get; set; } = "";
    public int Filas { get; set; }
    public int Columnas { get; set; }
    public string Pasillos { get; set; } = "";
    public int Alumnos { get; set; }

    public int FilasEfectivas =>
        Filas > 0
            ? Filas
            : 4;

    public int ColumnasEfectivas =>
        Columnas > 0
            ? Columnas
            : 3;

    public List<int> PasillosLista =>
        ParsearPasillos(
            Pasillos,
            ColumnasEfectivas);

    public string DisposicionTexto =>
        $"{FilasEfectivas} filas × {ColumnasEfectivas} columnas";


    /// <summary>
    /// Convierte "2,4" en la lista ordenada de pasillos válidos
    /// (después de las columnas 1..columnas-1).
    /// </summary>
    public static List<int> ParsearPasillos(
        string pasillos,
        int columnas)
    {
        var lista =
            new List<int>();

        if (string.IsNullOrWhiteSpace(
                pasillos))
        {
            return lista;
        }

        foreach (string parte in
                 pasillos.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(
                    parte.Trim(),
                    out int n)
                && n >= 1
                && n < columnas
                && !lista.Contains(
                    n))
            {
                lista.Add(
                    n);
            }
        }

        lista.Sort();

        return lista;
    }

    public string SemanaTexto =>
        FormatearSemana(
            SemanaInicio);

    public string InstructorTexto =>
        string.IsNullOrWhiteSpace(
            InstructorNombre)
            ? "Sin instructor"
            : InstructorNombre;

    public string SustitutoTexto =>
        string.IsNullOrWhiteSpace(
            SustitutoNombre)
            ? "Sin sustituto"
            : SustitutoNombre;

    public string Detalle =>
        (string.IsNullOrWhiteSpace(
             Congregacion)
             ? "Sin salón"
             : Congregacion)
        + " · "
        + InstructorTexto
        + " · "
        + Alumnos
        + (Alumnos == 1
            ? " estudiante"
            : " estudiantes");

    public string NotasTexto =>
        string.IsNullOrWhiteSpace(
            Notas)
            ? "Sin notas"
            : Notas;


    /// <summary>
    /// "Semana del 9 al 14 de noviembre" (lunes a sábado).
    /// </summary>
    public static string FormatearSemana(
        string semanaInicio)
    {
        if (!DateTime.TryParse(
                semanaInicio,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime inicio))
        {
            return string.IsNullOrWhiteSpace(
                       semanaInicio)
                   ? "Semana sin fecha"
                   : "Semana del " + semanaInicio;
        }

        DateTime fin =
            inicio.AddDays(5);

        string mesInicio =
            NombreMes(
                inicio.Month);

        string mesFin =
            NombreMes(
                fin.Month);

        if (inicio.Year != fin.Year)
        {
            return $"Semana del {inicio.Day} de {mesInicio} de {inicio.Year} al {fin.Day} de {mesFin} de {fin.Year}";
        }

        if (inicio.Month == fin.Month)
        {
            return $"Semana del {inicio.Day} al {fin.Day} de {mesInicio}";
        }

        return $"Semana del {inicio.Day} de {mesInicio} al {fin.Day} de {mesFin}";
    }


    private static string NombreMes(
        int mes) =>
        CultureInfo
            .GetCultureInfo("es-ES")
            .DateTimeFormat
            .GetMonthName(
                mes);
}


/// <summary>
/// Un estudiante apuntado a una escuela, con su asiento y su día de
/// limpieza.
/// </summary>
public class EscuelaAlumnoMobile
{
    public string Id { get; set; } = "";
    public string EscuelaId { get; set; } = "";
    public int PersonaId { get; set; }
    public string Nombre { get; set; } = "";
    public int Asiento { get; set; }
    public string LimpiezaDia { get; set; } = "";

    public string AsientoTexto =>
        Asiento > 0
            ? "Asiento " + Asiento
            : "Sin asiento";

    public string LimpiezaTexto =>
        string.IsNullOrWhiteSpace(
            LimpiezaDia)
            ? "Sin limpieza"
            : LimpiezaDia;

    public string Detalle =>
        AsientoTexto + " · " + LimpiezaTexto;
}
