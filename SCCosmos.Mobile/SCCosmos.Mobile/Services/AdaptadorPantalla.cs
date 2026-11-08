using System.Runtime.CompilerServices;

using Microsoft.Maui.Controls;

namespace SCCosmos.Mobile.Services;

// ============================================================================
// ADAPTACIÓN A LA PANTALLA (móvil, plegable abierto, tableta)
// ============================================================================
//
// Se aplica sola al cambiar de página y cuando cambia el tamaño de la
// ventana (girar el móvil, desplegar un plegable, cambiar de ventana en una
// tableta), a partir del ancho disponible en unidades independientes (dp):
//
//   · estrecho (< 600): una sola columna y los márgenes de siempre.
//   · medio (600-899, tableta pequeña o plegable abierto): márgenes algo
//     mayores, formularios centrados y listas a dos columnas.
//   · grande (>= 900, tableta/escritorio): lo anterior con listas a tres
//     columnas y el menú lateral fijo.

public static class AdaptadorPantalla
{
    public const double AnchoMedio = 600;

    public const double AnchoGrande = 900;

    /// <summary>
    /// Ancho mínimo (dp) de cada tarjeta de una lista. Si en el ancho disponible
    /// no caben tantas columnas, se usan menos (la visita, con sus seis cuadros de
    /// meses, no cabe en tres columnas con el móvil tumbado). 0 = sin mínimo.
    /// </summary>
    public static readonly BindableProperty AnchoMinimoTarjetaProperty =
        BindableProperty.CreateAttached(
            "AnchoMinimoTarjeta",
            typeof(double),
            typeof(AdaptadorPantalla),
            0d);

    public static double GetAnchoMinimoTarjeta(BindableObject vista) =>
        (double)vista.GetValue(AnchoMinimoTarjetaProperty);

    public static void SetAnchoMinimoTarjeta(BindableObject vista, double valor) =>
        vista.SetValue(AnchoMinimoTarjetaProperty, valor);

    private static readonly
        ConditionalWeakTable<Layout, object> PaddingsOriginales =
            new();

    private static readonly
        ConditionalWeakTable<Layout, object> AnchosOriginales =
            new();

    private static readonly
        ConditionalWeakTable<CollectionView, object> DisposicionesOriginales =
            new();


    public static bool EsModoAncho(
        double ancho)
    {
        return ancho >= AnchoMedio;
    }


    /// <summary>
    /// Ajusta la página al ancho indicado. Es idempotente: se puede llamar
    /// tantas veces como haga falta.
    /// </summary>
    public static void Aplicar(
        ContentPage? pagina,
        double ancho)
    {
        if (pagina?.Content is not VisualElement raiz
            ||
            ancho < 100)
        {
            return;
        }

        bool anchoActivo =
            EsModoAncho(ancho);

        // 1. Márgenes de la página.
        if (pagina.Content is Layout layout)
        {
            AjustarPadding(
                layout,
                anchoActivo);
        }

        // 2. Formularios y paneles: centrados con un ancho máximo legible.
        if (pagina.Content is ScrollView scroll
            && scroll.Content is Layout interior)
        {
            AjustarAnchoMaximo(
                interior,
                anchoActivo);
        }

        // 3. Listas: una, dos o tres columnas según el espacio.
        int columnas =
            !anchoActivo
                ? 1
                : (ancho >= AnchoGrande
                    ? 3
                    : 2);

        foreach (
            CollectionView coleccion
            in BuscarColecciones(raiz))
        {
            if (EsDeDetalle(
                    coleccion,
                    pagina))
            {
                continue;
            }

            int columnasLista = columnas;
            double minimo = GetAnchoMinimoTarjeta(coleccion);

            if (minimo > 0)
            {
                columnasLista = Math.Max(
                    1,
                    Math.Min(columnas, (int)(ancho / minimo)));
            }

            AjustarColumnas(
                coleccion,
                columnasLista);
        }
    }


    private static void AjustarPadding(
        Layout layout,
        bool ancho)
    {
        if (!PaddingsOriginales.TryGetValue(
                layout,
                out object? guardado))
        {
            PaddingsOriginales.Add(
                layout,
                layout.Padding);

            guardado = layout.Padding;
        }

        Thickness original =
            (Thickness)guardado!;

        // Se conservan los márgenes originales y se añade aire arriba/abajo
        // y a los lados cuando hay sitio de sobra.
        double extra =
            ancho
                ? Math.Min(
                    18,
                    Math.Max(0, original.Left))
                : 0;

        layout.Padding =
            new Thickness(
                original.Left + extra,
                original.Top,
                original.Right + extra,
                original.Bottom);
    }


    private static void AjustarAnchoMaximo(
        Layout layout,
        bool ancho)
    {
        if (!AnchosOriginales.TryGetValue(
                layout,
                out object? guardado))
        {
            AnchosOriginales.Add(
                layout,
                layout.MaximumWidthRequest);

            guardado = layout.MaximumWidthRequest;
        }

        double original =
            (double)guardado!;

        if (ancho)
        {
            layout.MaximumWidthRequest =
                original > 0
                    ? original
                    : 760;

            layout.HorizontalOptions =
                LayoutOptions.Center;
        }
        else
        {
            layout.MaximumWidthRequest =
                original > 0
                    ? original
                    : -1;

            layout.HorizontalOptions =
                LayoutOptions.Fill;
        }
    }


    private static void AjustarColumnas(
        CollectionView coleccion,
        int columnas)
    {
        if (coleccion.IsGrouped)
        {
            return;
        }

        // Si se adapta o no se decide con la disposición ORIGINAL. Antes se
        // miraba la actual: al girar a horizontal pasa a ser una cuadrícula y,
        // al volver a vertical, ya no parecía una lista normal y nunca se
        // restauraba (la lista se quedaba en dos columnas).
        if (!DisposicionesOriginales.TryGetValue(
                coleccion,
                out object? guardado))
        {
            if (coleccion.ItemsLayout is not null
                &&
                (coleccion.ItemsLayout
                     is not LinearItemsLayout lineal
                 ||
                 lineal.Orientation !=
                     ItemsLayoutOrientation.Vertical))
            {
                return;
            }

            DisposicionesOriginales.Add(
                coleccion,
                coleccion.ItemsLayout!);

            guardado = coleccion.ItemsLayout;
        }

        if (columnas <= 1)
        {
            if (!ReferenceEquals(coleccion.ItemsLayout, guardado))
            {
                coleccion.ItemsLayout =
                    (ItemsLayout?)guardado
                    ?? new LinearItemsLayout(
                        ItemsLayoutOrientation.Vertical);
            }

            return;
        }

        // Ya tiene esas columnas: no se rehace (cada cambio vuelve a pintar la lista).
        if (coleccion.ItemsLayout is GridItemsLayout cuadricula
            && cuadricula.Span == columnas)
        {
            return;
        }

        coleccion.ItemsLayout =
            new GridItemsLayout(
                columnas,
                ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 10,
                VerticalItemSpacing = 0
            };
    }


    /// <summary>
    /// No se reparten en columnas las listas que pertenecen a vistas de
    /// detalle (matriz de oradores, revisiones, etc.), porque sus filas ya
    /// llevan mucha información.
    /// </summary>
    private static bool EsDeDetalle(
        CollectionView coleccion,
        Page pagina)
    {
        Element? actual =
            coleccion.Parent;

        while (actual is not null
               &&
               !ReferenceEquals(
                   actual,
                   pagina))
        {
            string nombre =
                actual.GetType().Name;

            if (nombre.Contains("Matriz")
                ||
                nombre.Contains("Detalle")
                ||
                nombre.Contains("Review"))
            {
                return true;
            }

            actual = actual.Parent;
        }

        return false;
    }


    private static IEnumerable<CollectionView>
        BuscarColecciones(
            Element raiz)
    {
        foreach (
            Element hijo
            in raiz.LogicalChildren)
        {
            if (hijo is CollectionView coleccion)
            {
                yield return coleccion;
            }

            foreach (
                CollectionView interna
                in BuscarColecciones(hijo))
            {
                yield return interna;
            }
        }
    }
}
