using System.Globalization;

namespace SCCosmos.Mobile.Converters;

/// <summary>
/// Devuelve el icono del menú lateral a partir del título del módulo: los
/// mismos símbolos de Fluent que el menú de Windows, como SVG en
/// Resources/Images (menu_*.svg, sacados de la fuente de Wpf.Ui con
/// Herramientas\Capturas, modo Iconos). Antes eran emojis.
/// </summary>
public class IconoMenuConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        string titulo =
            (value as string ?? "")
                .ToLowerInvariant();

        // «Archivo del circuito» antes que «Circuito».
        string? icono =
            titulo.Contains("inicio") ? "menu_inicio"
            : titulo.Contains("calendario") ? "menu_calendario"
            : titulo.Contains("personas") ? "menu_personas"
            : titulo.Contains("correspondencia") ? "menu_correspondencia"
            : titulo.Contains("archivo") ? "menu_archivo"
            : titulo.Contains("circuito") ? "menu_circuito"
            : titulo.Contains("asambleas") ? "menu_asambleas"
            : titulo.Contains("rendimientos") || titulo.Contains("donaciones") ? "menu_rendimientos"
            : titulo.Contains("escuelas") ? "menu_escuelas"
            : titulo.Contains("rsc") ? "menu_rsc"
            : titulo.Contains("tareas") ? "menu_tareas"
            : titulo.Contains("actividad") ? "menu_actividad"
            : titulo.Contains("configuraci") ? "menu_configuracion"
            : null;

        return icono is null
            ? null
            : ImageSource.FromFile(icono + ".png");
    }
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
