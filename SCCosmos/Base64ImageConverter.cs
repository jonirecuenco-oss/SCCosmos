using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SCCosmos
{
    public class Base64ImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string base64 = value as string;
            if (string.IsNullOrEmpty(base64)) return null;

            try
            {
                // Limpiamos la cabecera que devuelve JW Hub ("data:image/jpeg;base64,...")
                if (base64.Contains(",")) base64 = base64.Split(',')[1];

                byte[] imageBytes = System.Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(imageBytes))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    return image;
                }
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}