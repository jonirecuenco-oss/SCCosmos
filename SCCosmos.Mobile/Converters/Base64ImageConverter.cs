using System.Globalization;

namespace SCCosmos.Mobile.Converters;

public class Base64ImageConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not string base64 ||
            string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            byte[] bytes =
                System.Convert.FromBase64String(base64);

            return ImageSource.FromStream(
                () => new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
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