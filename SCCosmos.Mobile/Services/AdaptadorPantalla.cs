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

            AjustarColumnas(
                coleccion,
                columnas);
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

        // Solo se tocan listas verticales sencillas.
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

        if (!DisposicionesOriginales.TryGetValue(
                coleccion,
                out object? guardado))
        {
            DisposicionesOriginales.Add(
                coleccion,
                coleccion.ItemsLayout!);

            guardado = coleccion.ItemsLayout;
        }

        if (columnas <= 1)
        {
            coleccion.ItemsLayout =
                (ItemsLayout?)guardado
                ?? new LinearItemsLayout(
                    ItemsLayoutOrientation.Vertical);

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
