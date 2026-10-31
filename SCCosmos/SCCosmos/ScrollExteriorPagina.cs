using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCCosmos
{
    /// <summary>
    /// WPF-UI envuelve cada página en un ScrollViewer propio que le da una
    /// altura infinita, y así ninguna tabla de la página puede virtualizarse.
    /// Las páginas con tablas que se desplazan solas (Calendario, Visitas) lo
    /// desactivan mientras están abiertas. Solo la página que lo desactivó
    /// puede restaurarlo: al navegar, los eventos Loaded y Unloaded de dos
    /// páginas pueden llegar cruzados.
    /// </summary>
    public static class ScrollExteriorPagina
    {
        private static Page? _propietaria;

        private static ScrollViewer? _scroll;

        public static void Desactivar(
            Page pagina)
        {
            DependencyObject? padre =
                VisualTreeHelper.GetParent(pagina);

            while (padre != null
                   && padre is not ScrollViewer)
            {
                padre = VisualTreeHelper.GetParent(padre);
            }

            if (padre is not ScrollViewer scroll)
            {
                return;
            }

            _propietaria = pagina;
            _scroll = scroll;

            scroll.VerticalScrollBarVisibility =
                ScrollBarVisibility.Disabled;
        }

        public static void Restaurar(
            Page pagina)
        {
            if (!ReferenceEquals(_propietaria, pagina)
                || _scroll == null)
            {
                return;
            }

            _scroll.VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto;

            _propietaria = null;
        }
    }
}
