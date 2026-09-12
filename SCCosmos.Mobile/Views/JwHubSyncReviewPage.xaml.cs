using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;


public partial class JwHubSyncReviewPage
    : ContentPage
{
#if ANDROID

    private ResultadoAnalisisJwHub?
        _analisis;


    private TipoAnalisisJwHub
        _categoriaActual =
            TipoAnalisisJwHub.Conflicto;

#endif


    public JwHubSyncReviewPage()
    {
        InitializeComponent();
    }


    protected override void OnAppearing()
    {
        base.OnAppearing();


        CargarAnalisis();
    }


    // =========================================================
    // CARGAR ANÁLISIS
    // =========================================================

    private void CargarAnalisis()
    {
#if ANDROID

        _analisis =
            JwHubSyncAnalysisService
                .UltimoAnalisis;


        if (_analisis is null)
        {
            TxtNumConflictos.Text = "0";
            TxtNumNuevos.Text = "0";
            TxtNumBajas.Text = "0";
            TxtNumIguales.Text = "0";

            ListaResultados.ItemsSource =
                null;

            LblTituloLista.Text =
                "Sin resultados";

            LblEstado.Text =
                "No hay una comparación disponible.";

            return;
        }


        TxtNumConflictos.Text =
            _analisis.TotalConflictos
                .ToString();

        TxtNumNuevos.Text =
            _analisis.TotalNuevos
                .ToString();

        TxtNumBajas.Text =
            _analisis.TotalBajas
                .ToString();

        TxtNumIguales.Text =
            _analisis.TotalIguales
                .ToString();


        int total =
            _analisis.TotalConflictos +
            _analisis.TotalNuevos +
            _analisis.TotalBajas +
            _analisis.TotalIguales;


        LblEstado.Text =
            $"{total} elementos";


        // Al volver de resolver un conflicto,
        // mantenemos la categoría que estaba abierta.
        MostrarCategoria(
            _categoriaActual);

#endif
    }


#if ANDROID

    // =========================================================
    // MOSTRAR CATEGORÍA
    // =========================================================

    private void MostrarCategoria(
        TipoAnalisisJwHub categoria)
    {
        if (_analisis is null)
        {
            ListaResultados.ItemsSource =
                null;

            return;
        }


        _categoriaActual =
            categoria;


        IReadOnlyList<JwHubSyncItemAnalisis>
            elementos;


        switch (categoria)
        {
            case TipoAnalisisJwHub.Conflicto:

                elementos =
                    _analisis.Conflictos;

                LblTituloLista.Text =
                    $"Conflictos ({elementos.Count})";

                break;


            case TipoAnalisisJwHub.Nuevo:

                elementos =
                    _analisis.Nuevos;

                LblTituloLista.Text =
                    $"Nuevos en JW Hub ({elementos.Count})";

                break;


            case TipoAnalisisJwHub.Baja:

                elementos =
                    _analisis.Bajas;

                LblTituloLista.Text =
                    $"Posibles bajas ({elementos.Count})";

                break;


            default:

                elementos =
                    _analisis.Iguales;

                LblTituloLista.Text =
                    $"Sin cambios ({elementos.Count})";

                break;
        }


        // Forzamos refresco del CollectionView.
        ListaResultados.ItemsSource =
            null;


        ListaResultados.ItemsSource =
            elementos;


        ActualizarBotonesCategoria();
    }


    private void ActualizarBotonesCategoria()
    {
        BtnConflictos.Opacity =
            _categoriaActual ==
            TipoAnalisisJwHub.Conflicto
                ? 1
                : 0.55;


        BtnNuevos.Opacity =
            _categoriaActual ==
            TipoAnalisisJwHub.Nuevo
                ? 1
                : 0.55;


        BtnBajas.Opacity =
            _categoriaActual ==
            TipoAnalisisJwHub.Baja
                ? 1
                : 0.55;


        BtnIguales.Opacity =
            _categoriaActual ==
            TipoAnalisisJwHub.Igual
                ? 1
                : 0.55;
    }

#endif


    // =========================================================
    // RESOLVER CONFLICTO
    // =========================================================

    private async void BtnResolverConflicto_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        if (sender is not Button boton)
        {
            return;
        }


        if (boton.CommandParameter
            is not JwHubSyncItemAnalisis item)
        {
            return;
        }


        if (item.Tipo !=
            TipoAnalisisJwHub.Conflicto)
        {
            return;
        }


        if (item.PersonaDB is null ||
            item.PersonaHub is null)
        {
            await DisplayAlert(
                "JW Hub",
                "No están disponibles los datos " +
                "necesarios para resolver este conflicto.",
                "Aceptar");

            return;
        }


        await Navigation.PushAsync(
            new JwHubSyncConflictPage(
                item));

#endif
    }

    private async void BtnAnadirNuevo_Clicked(
    object sender,
    EventArgs e)
    {
#if ANDROID

        if (sender is not Button boton)
        {
            return;
        }


        if (boton.CommandParameter
            is not JwHubSyncItemAnalisis item)
        {
            return;
        }


        if (item.Tipo !=
            TipoAnalisisJwHub.Nuevo)
        {
            return;
        }


        if (item.PersonaHub is null)
        {
            await DisplayAlert(
                "JW Hub",
                "No están disponibles los datos " +
                "de esta persona.",
                "Aceptar");

            return;
        }


        await Navigation.PushAsync(
            new JwHubSyncNewPersonPage(
                item));

#endif
    }


    // =========================================================
    // CATEGORÍAS
    // =========================================================

    private void BtnConflictos_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        MostrarCategoria(
            TipoAnalisisJwHub.Conflicto);

#endif
    }


    private void BtnNuevos_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        MostrarCategoria(
            TipoAnalisisJwHub.Nuevo);

#endif
    }


    private void BtnBajas_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        MostrarCategoria(
            TipoAnalisisJwHub.Baja);

#endif
    }


    private void BtnIguales_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        MostrarCategoria(
            TipoAnalisisJwHub.Igual);

#endif
    }

    // =========================================================
    // DESCARTAR ELEMENTO
    //
    // IMPORTANTE:
    //
    // Descartar NO modifica SQLite.
    //
    // Conflicto descartado:
    //     ignoramos los cambios detectados.
    //
    // Nuevo descartado:
    //     no se añadirá.
    //
    // Baja descartada:
    //     la persona permanece activa.
    //
    // Igual descartado:
    //     simplemente desaparece de la revisión.
    // =========================================================

    private async void BtnDescartar_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        if (sender is not Button boton)
        {
            return;
        }


        if (boton.CommandParameter
            is not JwHubSyncItemAnalisis item)
        {
            return;
        }


        if (_analisis is null)
        {
            return;
        }


        string explicacion =
            item.Tipo switch
            {
                TipoAnalisisJwHub.Conflicto =>
                    "Se ignorarán los cambios detectados " +
                    "y no se modificará la base de datos.",

                TipoAnalisisJwHub.Nuevo =>
                    "Esta persona no se añadirá a la " +
                    "base de datos.",

                TipoAnalisisJwHub.Baja =>
                    "La persona permanecerá activa en " +
                    "la base de datos.",

                _ =>
                    "El elemento desaparecerá de esta revisión."
            };


        bool confirmar =
            await DisplayAlert(
                "Descartar",
                $"¿Descartar a {item.NombreVisual} " +
                $"de esta revisión?\n\n" +
                explicacion,
                "Descartar",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        // =====================================================
        // QUITAR DE SU LISTA
        // =====================================================

        if (_analisis.Conflictos.Contains(
                item))
        {
            _analisis.Conflictos.Remove(
                item);
        }
        else if (_analisis.Nuevos.Contains(
                     item))
        {
            _analisis.Nuevos.Remove(
                item);
        }
        else if (_analisis.Bajas.Contains(
                     item))
        {
            _analisis.Bajas.Remove(
                item);
        }
        else if (_analisis.Iguales.Contains(
                     item))
        {
            _analisis.Iguales.Remove(
                item);
        }


        // =====================================================
        // PERSISTIR EL NUEVO ESTADO
        // =====================================================

        JwHubSyncAnalysisService
            .GuardarUltimoAnalisis();


        // =====================================================
        // ACTUALIZAR CONTADORES Y LISTADO
        // =====================================================

        CargarAnalisis();

#endif
    }


    // =========================================================
    // FINALIZAR REVISIÓN
    //
    // Reproduce la lógica importante del PC:
    //
    // - Nuevos pendientes -> insertar.
    // - Conflictos pendientes -> no aplicar.
    // - Bajas pendientes -> no aplicar.
    // - Elementos descartados -> no hacer nada.
    // =========================================================

    private async void BtnFinalizar_Clicked(
    object sender,
    EventArgs e)
    {
#if ANDROID

        if (_analisis is null)
        {
            await Navigation.PopAsync();
            return;
        }


        int nuevosPendientes =
            _analisis.TotalNuevos;


        int conflictosPendientes =
            _analisis.TotalConflictos;


        int bajasPendientes =
            _analisis.TotalBajas;


        // =========================================================
        // NUEVOS: SIEMPRE DEBEN REVISARSE
        //
        // Ya no insertamos automáticamente los nuevos porque
        // algunos pueden ser personas existentes cuyo nombre
        // ha cambiado en JW Hub.
        // =========================================================

        if (nuevosPendientes > 0)
        {
            await DisplayAlert(
                "Nuevos pendientes",
                $"Todavía hay {nuevosPendientes} persona(s) " +
                "clasificada(s) como nuevas.\n\n" +
                "Revísalas antes de finalizar. Cada una debe " +
                "añadirse como persona nueva, vincularse con una " +
                "persona existente o descartarse.",
                "Aceptar");

            return;
        }


        string mensaje =
            "¿Finalizar la revisión de JW Hub?";


        if (conflictosPendientes > 0)
        {
            mensaje +=
                $"\n\n• {conflictosPendientes} conflicto(s) " +
                "sin resolver se dejarán sin aplicar.";
        }


        if (bajasPendientes > 0)
        {
            mensaje +=
                $"\n\n• {bajasPendientes} posible(s) baja(s) " +
                "sin confirmar permanecerán activas.";
        }


        bool confirmar =
            await DisplayAlert(
                "Finalizar revisión",
                mensaje,
                "Finalizar",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        BtnFinalizar.IsEnabled =
            false;


        try
        {
            JwHubSyncAnalysisService
                .LimpiarUltimoAnalisis();


            await DisplayAlert(
                "JW Hub",
                "Revisión finalizada correctamente.",
                "Aceptar");


            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "JW Hub",
                "No se pudo finalizar la revisión:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            BtnFinalizar.IsEnabled =
                true;
        }

#endif
    }


    // =========================================================
    // CERRAR
    // =========================================================

    private async void BtnCerrar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void BtnConfirmarBaja_Clicked(
    object sender,
    EventArgs e)
    {
#if ANDROID

        if (sender is not Button boton)
        {
            return;
        }


        if (boton.CommandParameter
            is not JwHubSyncItemAnalisis item)
        {
            return;
        }


        if (item.Tipo !=
            TipoAnalisisJwHub.Baja)
        {
            return;
        }


        if (item.PersonaDB is null)
        {
            await DisplayAlert(
                "JW Hub",
                "No están disponibles los datos " +
                "de esta persona.",
                "Aceptar");

            return;
        }


        // =========================================================
        // CORTAFUEGOS DEL MODO PRUEBA
        // =========================================================

        if (!item.PuedeConfirmarBaja)
        {
            await DisplayAlert(
                "Modo de prueba",
                "No se puede confirmar una baja mientras " +
                "la sincronización esté limitada a unas pocas " +
                "personas.\n\n" +
                "Realiza primero una sincronización completa.",
                "Aceptar");

            return;
        }


        // =========================================================
        // CONFIRMACIÓN
        // =========================================================

        bool confirmar =
            await DisplayAlert(
                "Confirmar baja",
                $"¿Confirmar la baja de " +
                $"{item.NombreVisual}?\n\n" +
                "La persona dejará de aparecer en las " +
                "listas actuales, pero sus datos y su " +
                "historial se conservarán en la base de datos.",
                "Confirmar baja",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        boton.IsEnabled =
            false;


        try
        {
            int personaId =
                item.PersonaDB.Id;


            // =====================================================
            // SOFT DELETE REAL
            // =====================================================

            await Task.Run(
                () =>
                    DatabaseHelper
                        .MarcarPersonaEliminada(
                            personaId));


            // =====================================================
            // ACTUALIZAR ANÁLISIS EN MEMORIA
            //
            // Igual que el flujo del PC:
            // una baja confirmada desaparece de pendientes.
            // =====================================================

            ResultadoAnalisisJwHub?
                analisis =
                    JwHubSyncAnalysisService
                        .UltimoAnalisis;


            if (analisis is not null)
            {
                analisis.Bajas.Remove(
                    item);


                JwHubSyncAnalysisService
                    .GuardarUltimoAnalisis();
            }


            // Actualizar contador y listado.
            CargarAnalisis();


            await DisplayAlert(
                "JW Hub",
                $"{item.NombreVisual} se ha marcado " +
                "como baja.\n\n" +
                "Su historial permanece intacto.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "JW Hub",
                "No se pudo confirmar la baja:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            boton.IsEnabled =
                item.PuedeConfirmarBaja;
        }

#endif
    }

#if ANDROID

    // =========================================================
    // COPIAR PERSONA HUB PARA INSERTAR
    //
    // Evitamos modificar el objeto PersonaHub original.
    // =========================================================

    private static Persona CopiarPersonaParaInsertar(
        Persona hub)
    {
        return new Persona
        {
            Id =
                0,

            Grupo =
                hub.Grupo,

            Nombre =
                hub.Nombre ?? "",

            FechaNacimiento =
                hub.FechaNacimiento ?? "",

            Sexo =
                hub.Sexo ?? "",

            FechaBautismo =
                hub.FechaBautismo ?? "",

            Esperanza =
                hub.Esperanza ?? "",

            Nombramiento =
                hub.Nombramiento ?? "",

            Servicio =
                hub.Servicio ?? "",

            CongregacionCodigo =
                hub.CongregacionCodigo ?? "",

            CongregacionNombre =
                hub.CongregacionNombre ?? "",

            Telefono =
                hub.Telefono ?? "",

            Email =
                hub.Email ?? "",

            FechaInicioPrec =
                hub.FechaInicioPrec ?? "",

            FechaUltimaEscuela =
                hub.FechaUltimaEscuela ?? "",

            NombreJWHub =
                hub.NombreJWHub ?? "",

            FechaCreacion =
                hub.FechaCreacion ?? "",

            FechaEliminacion =
                "",

            Solicitudes =
                hub.Solicitudes ?? "",

            Privilegios =
                hub.Privilegios ?? "",

            Foto =
                hub.Foto ?? "",

            NombreComun =
                hub.NombreComun ?? "",

            Direccion =
                hub.Direccion ?? "",

            Latitud =
                hub.Latitud,

            Longitud =
                hub.Longitud,

            ConyugeId =
                hub.ConyugeId,

            EmailJW =
                hub.EmailJW ?? "",

            PersonaRaizId =
                hub.PersonaRaizId,

            MesHistorico =
                hub.MesHistorico,

            FechaExactaHistorico =
                hub.FechaExactaHistorico ?? "",

            Notas =
                hub.Notas ?? ""
        };
    }

#endif
}