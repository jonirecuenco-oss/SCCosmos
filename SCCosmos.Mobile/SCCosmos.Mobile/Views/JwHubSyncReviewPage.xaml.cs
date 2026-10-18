using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

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
            ListaResultados.ItemsSource =
                null;

            LblTituloLista.Text =
                "Sin resultados";

            LblEstado.Text =
                "No hay una comparación disponible.";

            ActualizarBotonesCategoria();

            return;
        }


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


            case TipoAnalisisJwHub.Traslado:

                elementos =
                    _analisis.Traslados;

                LblTituloLista.Text =
                    $"Posibles traslados ({elementos.Count})";

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
        int conflictos =
            _analisis?.TotalConflictos
            ?? 0;

        int nuevos =
            _analisis?.TotalNuevos
            ?? 0;

        int bajas =
            _analisis?.TotalBajas
            ?? 0;

        int traslados =
            _analisis?.TotalTraslados
            ?? 0;

        int iguales =
            _analisis?.TotalIguales
            ?? 0;


        // Los contadores viven ahora en las propias pestañas.
        // Así evitamos duplicar la misma información con los
        // cuatro cuadros-resumen que había encima.
        BtnConflictos.Text =
            $"Conflictos ({conflictos})";

        BtnNuevos.Text =
            $"Nuevos ({nuevos})";

        BtnBajas.Text =
            $"Bajas ({bajas})";

        BtnTraslados.Text =
            $"Traslados ({traslados})";

        BtnIguales.Text =
            $"Sin cambios ({iguales})";


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


        BtnTraslados.Opacity =
            _categoriaActual ==
            TipoAnalisisJwHub.Traslado
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


    private void BtnTraslados_Clicked(
        object sender,
        EventArgs e)
    {
#if ANDROID

        MostrarCategoria(
            TipoAnalisisJwHub.Traslado);

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
    // CONFIRMAR TRASLADO
    // =========================================================

    private async void BtnConfirmarTraslado_Clicked(
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
                TipoAnalisisJwHub.Traslado
            ||
            item.PersonaDB is null
            ||
            item.PersonaHub is null)
        {
            return;
        }


        if (_analisis is null)
        {
            return;
        }


        string origen =
            !string.IsNullOrWhiteSpace(
                item.PersonaDB.CongregacionNombre)
                ? item.PersonaDB.CongregacionNombre
                : item.PersonaDB.CongregacionCodigo;


        string destino =
            item.PersonaHub.CongregacionCodigo ?? "";


        bool confirmar =
            await DisplayAlert(
                "Confirmar traslado",
                $"¿Confirmas el traslado de " +
                $"{item.NombreVisual}?\n\n" +
                $"{origen}  →  {destino}\n\n" +
                "La ficha anterior se dará de baja y se " +
                "conservará para las visitas históricas. " +
                "Se creará una ficha nueva enlazada en la " +
                "congregación de destino.\n\n" +
                "La actividad mensual NO se copiará a la " +
                "ficha nueva.",
                "Confirmar traslado",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        boton.IsEnabled =
            false;


        try
        {
            Persona anterior =
                item.PersonaDB;


            Persona hub =
                item.PersonaHub;


            int nuevoId =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .RegistrarTrasladoConHubMobile(
                                anterior,
                                hub,
                                DateTime.Today));


            if (nuevoId <= 0)
            {
                throw new Exception(
                    "No se pudo crear la ficha nueva.");
            }


            // =============================================
            // LA FICHA NUEVA PASA A "SIN CAMBIOS"
            // =============================================

            _analisis.Traslados.Remove(
                item);


            Persona? nuevaGuardada =
                DatabaseHelper
                    .ObtenerPersonaPorId(
                        nuevoId);


            item.Tipo =
                TipoAnalisisJwHub.Igual;


            item.PersonaDB =
                nuevaGuardada
                ?? anterior;


            item.Descripcion =
                $"Traslado registrado: {origen} → {destino}.";


            item.CamposCambiados.Clear();


            _analisis.Iguales.Add(
                item);


            JwHubSyncAnalysisService
                .GuardarUltimoAnalisis();


            CargarAnalisis();


            await DisplayAlert(
                "JW Hub",
                "El traslado se ha registrado " +
                "correctamente.\n\n" +
                "La ficha anterior queda como baja " +
                "histórica y la nueva está enlazada.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            boton.IsEnabled =
                true;


            await DisplayAlert(
                "Traslado",
                "No se pudo registrar el traslado:\n\n" +
                ex.Message,
                "Aceptar");
        }

#endif
    }


    // =========================================================
    // NO ES TRASLADO
    //
    // El registro de Hub vuelve a la lista de nuevos y la
    // ficha antigua a la lista de posibles bajas.
    // =========================================================

    private async void BtnNoEsTraslado_Clicked(
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
                TipoAnalisisJwHub.Traslado
            ||
            item.PersonaDB is null
            ||
            item.PersonaHub is null)
        {
            return;
        }


        if (_analisis is null)
        {
            return;
        }


        bool confirmar =
            await DisplayAlert(
                "No es traslado",
                $"¿Seguro que {item.NombreVisual} no se ha " +
                "trasladado?\n\n" +
                "El registro de JW Hub volverá a la lista de " +
                "nuevos y la ficha antigua a la lista de " +
                "posibles bajas.",
                "No es traslado",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        _analisis.Traslados.Remove(
            item);


        _analisis.Nuevos.Add(
            new JwHubSyncItemAnalisis
            {
                Tipo =
                    TipoAnalisisJwHub.Nuevo,

                PersonaDB =
                    null,

                PersonaHub =
                    item.PersonaHub,

                NombreVisual =
                    !string.IsNullOrWhiteSpace(
                        item.PersonaHub.Nombre)
                        ? item.PersonaHub.Nombre
                        : item.NombreVisual,

                Descripcion =
                    "Aparece en JW Hub pero no está en la " +
                    "Base de Datos."
            });


        _analisis.Bajas.Add(
            new JwHubSyncItemAnalisis
            {
                Tipo =
                    TipoAnalisisJwHub.Baja,

                PersonaDB =
                    item.PersonaDB,

                PersonaHub =
                    null,

                NombreVisual =
                    item.NombreVisual,

                Descripcion =
                    "Ya no aparece con esta identidad en " +
                    "JW Hub. Puede tratarse de una baja, un " +
                    "traslado o un cambio de nombre.",

                PuedeConfirmarBaja =
                    item.PuedeConfirmarBaja
            });


        JwHubSyncAnalysisService
            .GuardarUltimoAnalisis();


        CargarAnalisis();

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


        // "Sin cambios" no se descarta: ya es un resultado
        // informativo y no requiere ninguna decisión.
        if (item.EsIgual)
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

                TipoAnalisisJwHub.Traslado =>
                    "El traslado no se registrará. Ambas " +
                    "fichas quedarán como están.",

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
        else if (_analisis.Traslados.Contains(
                     item))
        {
            _analisis.Traslados.Remove(
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


        int trasladosPendientes =
            _analisis.TotalTraslados;


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


        // =========================================================
        // TRASLADOS: IGUAL QUE EN WINDOWS, DEBEN REVISARSE ANTES
        // DE PODER FINALIZAR.
        // =========================================================

        if (trasladosPendientes > 0)
        {
            await DisplayAlert(
                "Traslados pendientes",
                $"Todavía hay {trasladosPendientes} posible(s) " +
                "traslado(s) sin revisar.\n\n" +
                "Confirma cada traslado o pulsa «No es " +
                "traslado» antes de finalizar.",
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


            // La revisión ya terminó. Volvemos el sincronizador
            // a su estado inicial para que, al regresar a Personas:
            //
            // - el botón vuelva a "Sincronizar JW Hub";
            // - desaparezca "Sincronización completada";
            // - desaparezca el detalle/resumen de la extracción;
            // - desaparezca "Revisar resultados JW Hub".
            JwHubSyncState.Establecer(
                EstadoJwHubSync.Inactivo,
                0,
                0,
                "",
                false);


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