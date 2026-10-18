using System.Globalization;

namespace SCCosmos.Mobile.Converters;

/// <summary>
/// Devuelve el icono del menú lateral a partir del título del módulo.
/// Se hace con un convertidor (y no con fuentes de iconos) para no
/// depender de recursos externos en el APK.
/// </summary>
public class IconoMenuConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        string titulo =
            (value as string ?? "")
                .ToLowerInvariant();

        if (titulo.Contains("inicio"))
        {
            return "\U0001F3E0";   // 🏠
        }

        if (titulo.Contains("calendario"))
        {
            return "\U0001F4C5";   // 📅
        }

        if (titulo.Contains("personas"))
        {
            return "\U0001F465";   // 👥
        }

        if (titulo.Contains("correspondencia"))
        {
            return "\u2709";       // ✉
        }

        if (titulo.Contains("circuito")
            &&
            !titulo.Contains("archivo"))
        {
            return "\U0001F5FA";   // 🗺
        }

        if (titulo.Contains("archivo"))
        {
            return "\U0001F5C4";   // 🗄
        }

        if (titulo.Contains("asambleas"))
        {
            return "\U0001F3DB";   // 🏛
        }

        if (titulo.Contains("rendimientos")
            || titulo.Contains("donaciones"))
        {
            return "\U0001F4BC";   // 💰
        }

        if (titulo.Contains("escuelas"))
        {
            return "\U0001F393";   // 🎓
        }

        if (titulo.Contains("rsc"))
        {
            return "\U0001F91D";   // 🤝
        }

        if (titulo.Contains("tareas"))
        {
            return "\u2705";       // check
        }

        if (titulo.Contains("actividad"))
        {
            return "\U0001F4DC";   // pergamino
        }

        if (titulo.Contains("configuraci"))
        {
            return "\u2699";       // ⚙
        }

        return "\u25CF";           // ●
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
