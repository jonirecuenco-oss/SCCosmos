using System;
using System.Collections.Generic;
using System.Linq;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;


public partial class JwHubSyncConflictPage
    : ContentPage
{
#if ANDROID

    // =========================================================
    // ELEMENTO QUE SE ESTÁ COMPARANDO
    // =========================================================

    private readonly
        JwHubSyncItemAnalisis _item;


    // =========================================================
    // DATOS ADICIONALES CUANDO VENIMOS DE:
    //
    // NUEVO JW HUB + POSIBLE BAJA BD
    //
    // Es decir: posible cambio de nombre.
    // =========================================================

    private readonly
        JwHubSyncItemAnalisis?
        _itemNuevoVinculado;


    private readonly
        JwHubSyncItemAnalisis?
        _itemBajaVinculada;


    private readonly bool
        _modoVinculacion;


    // =========================================================
    // CAMPOS PRESENTADOS EN PANTALLA
    // =========================================================

    private readonly List<CampoResolucion>
        _campos =
            new();


    // =========================================================
    // PRIVILEGIOS ADMINISTRADOS POR JW HUB
    // =========================================================

    private static readonly string[]
        PrivilegiosPermitidos =
        [
            "Instructor de la Escuela del Servicio de Precursor",
            "Superintendente de circuito sustituto",
            "Miembro del CEH",
            "Superintendente de asamblea",
            "Coordinador",
            "Secretario",
            "Miembro del GVP",
            "Persona que pertenece a una entidad del LDC",
            "Superintendente auxiliar de asamblea"
        ];

#endif


    // =========================================================
    // CONSTRUCTOR NORMAL
    //
    // Se utiliza para los conflictos normales.
    // =========================================================

#if ANDROID

    public JwHubSyncConflictPage(
        JwHubSyncItemAnalisis item)
        : this(
            item,
            null,
            null)
    {
    }


    // =========================================================
    // CONSTRUCTOR PARA VINCULACIÓN
    //
    // item:
    //     comparación temporal BD <-> Hub
    //
    // itemNuevoVinculado:
    //     registro que estaba en Nuevos
    //
    // itemBajaVinculada:
    //     ficha antigua que estaba en Posibles bajas
    // =========================================================

    public JwHubSyncConflictPage(
        JwHubSyncItemAnalisis item,
        JwHubSyncItemAnalisis? itemNuevoVinculado,
        JwHubSyncItemAnalisis? itemBajaVinculada)
    {
        InitializeComponent();


        _item =
            item;


        _itemNuevoVinculado =
            itemNuevoVinculado;


        _itemBajaVinculada =
            itemBajaVinculada;


        _modoVinculacion =
            itemNuevoVinculado is not null
            &&
            itemBajaVinculada is not null;


        if (_item.PersonaDB is null ||
            _item.PersonaHub is null)
        {
            throw new ArgumentException(
                "El conflicto no contiene " +
                "las dos personas necesarias.");
        }


        LblNombre.Text =
            string.IsNullOrWhiteSpace(
                _item.NombreVisual)
                ? _item.PersonaDB.Nombre
                : _item.NombreVisual;


        // Fotografía de la persona: una sola vez, en la cabecera.
        if (!string.IsNullOrWhiteSpace(
                _item.PersonaDB.Foto))
        {
            var fuente =
                new SCCosmos.Mobile.Converters
                    .Base64ImageConverter()
                    .Convert(
                        _item.PersonaDB.Foto,
                        typeof(ImageSource),
                        null,
                        System.Globalization.CultureInfo
                            .InvariantCulture)
                as ImageSource;

            if (fuente is not null)
            {
                ImgFotoPersona.Source =
                    fuente;

                ImgFotoPersona.IsVisible =
                    true;

                LblSinFotoPersona.IsVisible =
                    false;
            }
        }


        CrearCampos();


        PrepararFoto();


        ListaCampos.ItemsSource =
            _campos;
    }

#else

    public JwHubSyncConflictPage()
    {
        InitializeComponent();


        LblNombre.Text =
            "Disponible únicamente en Android";
    }

#endif


#if ANDROID

    // =========================================================
    // CREAR CAMPOS EN CONFLICTO
    // =========================================================

    private void CrearCampos()
    {
        Persona db =
            _item.PersonaDB!;


        Persona hub =
            _item.PersonaHub!;


        // =====================================================
        // NOMBRE JW HUB
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.NombreJWHub)
            &&
            db.NombreJWHub !=
                hub.NombreJWHub)
        {
            AgregarCampo(
                "NombreJWHub",
                "Nombre JW Hub",
                db.NombreJWHub,
                hub.NombreJWHub);
        }


        // =====================================================
        // NOMBRE COMÚN
        //
        // Si la base de datos ya tiene un nombre común se respeta: puede
        // haberlo cambiado el superintendente a mano. JW Hub solo lo aporta
        // cuando el campo está vacío (y se propone su valor por defecto).
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.NombreComun)
            &&
            string.IsNullOrWhiteSpace(
                db.NombreComun))
        {
            AgregarCampo(
                "NombreComun",
                "Nombre común",
                db.NombreComun,
                hub.NombreComun);
        }


        // =====================================================
        // NOMBRE COMPLETO (S-21)
        //
        // No se ofrece como campo resoluble desde JW Hub.
        //
        // Si esta ficha ya tiene identidad S-21, Nombre debe
        // conservarse exactamente como llegó del S-21. JW Hub
        // únicamente administra NombreJWHub y NombreComun.
        // =====================================================


        // =====================================================
        // SEXO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Sexo)
            &&
            db.Sexo !=
                hub.Sexo)
        {
            AgregarCampo(
                "Sexo",
                "Sexo",
                db.Sexo,
                hub.Sexo);
        }


        // =====================================================
        // ESPERANZA
        // =====================================================

        string hubEsperanza =
            hub.Esperanza ?? "";


        string dbEsperanza =
            db.Esperanza ?? "";


        if (hubEsperanza != "" &&
            dbEsperanza !=
                hubEsperanza)
        {
            AgregarCampo(
                "Esperanza",
                "Esperanza",
                dbEsperanza,
                hubEsperanza);
        }


        // =====================================================
        // NOMBRAMIENTO
        //
        // Aquí vacío SÍ es significativo.
        // =====================================================

        string hubNombramiento =
            hub.Nombramiento ?? "";


        string dbNombramiento =
            db.Nombramiento ?? "";


        if (hubNombramiento !=
            dbNombramiento)
        {
            AgregarCampo(
                "Nombramiento",
                "Nombramiento",
                dbNombramiento,
                hubNombramiento);
        }


        // =====================================================
        // SERVICIO
        //
        // Aquí vacío SÍ es significativo.
        // =====================================================

        string hubServicio =
            hub.Servicio ?? "";


        string dbServicio =
            db.Servicio ?? "";


        if (hubServicio !=
            dbServicio)
        {
            AgregarCampo(
                "Servicio",
                "Servicio",
                dbServicio,
                hubServicio);
        }


        // =====================================================
        // PRIVILEGIOS
        // =====================================================

        if (HayCambioPrivilegios(
                db.Privilegios,
                hub.Privilegios))
        {
            AgregarCampo(
                "Privilegios",
                "Privilegios",
                db.Privilegios,
                hub.Privilegios,
                permitirCombinar: true);
        }


        // =====================================================
        // FECHA NACIMIENTO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.FechaNacimiento)
            &&
            db.FechaNacimiento !=
                hub.FechaNacimiento
            &&
            !hub.FechaNacimiento
                .StartsWith(
                    "01/01/",
                    StringComparison.Ordinal))
        {
            AgregarCampo(
                "FechaNacimiento",
                "Fecha de nacimiento",
                db.FechaNacimiento,
                hub.FechaNacimiento);
        }


        // =====================================================
        // FECHA BAUTISMO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.FechaBautismo)
            &&
            db.FechaBautismo !=
                hub.FechaBautismo
            &&
            !hub.FechaBautismo
                .StartsWith(
                    "01/01/",
                    StringComparison.Ordinal))
        {
            AgregarCampo(
                "FechaBautismo",
                "Fecha de bautismo",
                db.FechaBautismo,
                hub.FechaBautismo);
        }


        // =====================================================
        // INICIO PRECURSORADO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.FechaInicioPrec)
            &&
            db.FechaInicioPrec !=
                hub.FechaInicioPrec
            &&
            !hub.FechaInicioPrec
                .StartsWith(
                    "01/01/",
                    StringComparison.Ordinal))
        {
            AgregarCampo(
                "FechaInicioPrec",
                "Inicio de precursorado",
                db.FechaInicioPrec,
                hub.FechaInicioPrec);
        }


        // =====================================================
        // ÚLTIMA ESCUELA
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.FechaUltimaEscuela)
            &&
            db.FechaUltimaEscuela !=
                hub.FechaUltimaEscuela)
        {
            AgregarCampo(
                "FechaUltimaEscuela",
                "Última escuela",
                db.FechaUltimaEscuela,
                hub.FechaUltimaEscuela);
        }


        // =====================================================
        // EMAIL
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Email)
            &&
            db.Email !=
                hub.Email)
        {
            AgregarCampo(
                "Email",
                "Email",
                db.Email,
                hub.Email);
        }


        // =====================================================
        // EMAIL JW
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.EmailJW)
            &&
            db.EmailJW !=
                hub.EmailJW)
        {
            AgregarCampo(
                "EmailJW",
                "Email JW",
                db.EmailJW,
                hub.EmailJW);
        }


        // =====================================================
        // TELÉFONO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Telefono)
            &&
            db.Telefono !=
                hub.Telefono)
        {
            AgregarCampo(
                "Telefono",
                "Teléfono",
                db.Telefono,
                hub.Telefono);
        }


        // =====================================================
        // DIRECCIÓN
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Direccion)
            &&
            db.Direccion !=
                hub.Direccion)
        {
            AgregarCampo(
                "Direccion",
                "Dirección",
                db.Direccion,
                hub.Direccion);
        }


        // =====================================================
        // SOLICITUDES
        //
        // Vacío puede representar una pérdida.
        // =====================================================

        if (!SolicitudesIguales(
                db.Solicitudes,
                hub.Solicitudes))
        {
            AgregarCampo(
                "Solicitudes",
                "Solicitudes",
                db.Solicitudes,
                hub.Solicitudes);
        }


        // =====================================================
        // FOTOGRAFÍA
        //
        // Sigue excluida temporalmente.
        //
        // Windows recodifica la imagen a JPEG y Android
        // obtiene los bytes originales, por lo que comparar
        // los Base64 directamente produciría falsos positivos.
        // =====================================================
    }


    // =========================================================
    // AGREGAR CAMPO
    //
    // Normal:
    //
    // Base de datos
    // JW Hub          <- seleccionado por defecto
    //
    // Privilegios:
    //
    // Combinar ambos  <- seleccionado por defecto
    // Base de datos
    // JW Hub
    // =========================================================

    private void AgregarCampo(
        string clave,
        string nombre,
        string? valorBd,
        string? valorHub,
        bool permitirCombinar = false)
    {
        var opciones =
            new List<string>
            {
                "Base de datos",
                "JW Hub"
            };


        string seleccion =
            "JW Hub";


        if (permitirCombinar)
        {
            opciones.Insert(
                0,
                "Combinar ambos");


            seleccion =
                "Combinar ambos";
        }


        _campos.Add(
            new CampoResolucion
            {
                Clave =
                    clave,

                Nombre =
                    nombre,

                ValorBd =
                    valorBd ?? "",

                ValorHub =
                    valorHub ?? "",

                Opciones =
                    opciones,

                Seleccion =
                    seleccion
            });
    }


    // =========================================================
    // PRIVILEGIOS
    // =========================================================

    private static bool HayCambioPrivilegios(
        string? dbTexto,
        string? hubTexto)
    {
        List<string> db =
            SepararLineas(
                dbTexto);


        List<string> hub =
            SepararLineas(
                hubTexto);


        foreach (string privilegio
                 in db)
        {
            if (PrivilegiosPermitidos
                    .Contains(
                        privilegio)
                &&
                !hub.Contains(
                    privilegio))
            {
                return true;
            }
        }


        foreach (string privilegio
                 in hub)
        {
            if (PrivilegiosPermitidos
                    .Contains(
                        privilegio)
                &&
                !db.Contains(
                    privilegio))
            {
                return true;
            }
        }


        return false;
    }


    private static List<string> SepararLineas(
        string? texto)
    {
        return
            (texto ?? "")
                .Split(
                    new[] { '\n' },
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .Select(x =>
                    x.Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x))
                .ToList();
    }


    // =========================================================
    // SOLICITUDES
    // =========================================================

    private static bool SolicitudesIguales(
        string? dbTexto,
        string? hubTexto)
    {
        List<string> db =
            SepararSolicitudes(
                dbTexto);


        List<string> hub =
            SepararSolicitudes(
                hubTexto);


        return
            db.SequenceEqual(
                hub);
    }


    private static List<string>
        SepararSolicitudes(
            string? texto)
    {
        return
            (texto ?? "")
                .Split(
                    new[] { ',' },
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .Select(x =>
                    x.Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x))
                .OrderBy(x =>
                    x)
                .ToList();
    }


    // =========================================================
    // GUARDAR
    // =========================================================

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_item.PersonaDB is null ||
            _item.PersonaHub is null)
        {
            return;
        }


        string pregunta =
            _modoVinculacion
                ? "¿Guardar los valores seleccionados " +
                  "y vincular ambas personas?"
                : "¿Guardar las opciones seleccionadas " +
                  "en la base de datos?";


        bool confirmar =
            await DisplayAlert(
                _modoVinculacion
                    ? "Vincular persona"
                    : "Resolver conflicto",
                pregunta,
                "Guardar",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        BtnGuardar.IsEnabled =
            false;


        try
        {
            // =================================================
            // LEER DE NUEVO LA FICHA ACTUAL DESDE SQLITE
            //
            // Esto es especialmente importante al vincular:
            // conservamos el Id, grupo, cónyuge, notas,
            // historial y cualquier dato local.
            // =================================================

            Persona actualizada =
                DatabaseHelper
                    .ObtenerPersonaPorId(
                        _item.PersonaDB.Id);


            if (actualizada is null)
            {
                throw new Exception(
                    "La persona ya no existe en " +
                    "la base de datos.");
            }


            Persona hub =
                _item.PersonaHub;


            // =================================================
            // APLICAR SOLAMENTE LOS CAMPOS ELEGIDOS
            // =================================================

            foreach (CampoResolucion campo
                     in _campos)
            {
                switch (campo.Seleccion)
                {
                    // -----------------------------------------
                    // CONSERVAR BD
                    // -----------------------------------------

                    case "Base de datos":

                        break;


                    // -----------------------------------------
                    // TOMAR JW HUB
                    // -----------------------------------------

                    case "JW Hub":

                        AplicarValorHub(
                            actualizada,
                            hub,
                            campo.Clave);

                        break;


                    // -----------------------------------------
                    // COMBINAR PRIVILEGIOS
                    // -----------------------------------------

                    case "Combinar ambos":

                        if (campo.Clave ==
                            "Privilegios")
                        {
                            actualizada.Privilegios =
                                CombinarPrivilegios(
                                    campo.ValorBd,
                                    campo.ValorHub);
                        }

                        break;
                }
            }


            // =================================================
            // FOTOGRAFÍA
            // =================================================

            if (_fotoVisible
                &&
                _usarFotoHub)
            {
                actualizada.Foto =
                    hub.Foto;
            }


            // =================================================
            // GUARDAR EN SQLITE
            //
            // En modo vinculación sigue siendo la misma
            // persona y conserva su Id.
            // =================================================

            DatabaseHelper
                .ActualizarPersona(
                    actualizada);


            // =================================================
            // ACTUALIZAR EL ANÁLISIS EN MEMORIA
            // =================================================

            ResultadoAnalisisJwHub?
                analisis =
                    JwHubSyncAnalysisService
                        .UltimoAnalisis;


            if (analisis is not null)
            {
                // =============================================
                // VINCULACIÓN:
                //
                // NUEVO + BAJA -> IGUAL
                // =============================================

                if (_modoVinculacion
                    &&
                    _itemNuevoVinculado is not null
                    &&
                    _itemBajaVinculada is not null)
                {
                    // El registro que JW Hub parecía presentar
                    // como nuevo deja de estar pendiente.

                    analisis.Nuevos.Remove(
                        _itemNuevoVinculado);


                    // La ficha antigua tampoco es ya una
                    // posible baja.

                    analisis.Bajas.Remove(
                        _itemBajaVinculada);


                    // Conservamos el elemento que representaba
                    // al registro Hub y lo transformamos en un
                    // elemento procesado.

                    _itemNuevoVinculado.Tipo =
                        TipoAnalisisJwHub.Igual;


                    _itemNuevoVinculado.PersonaDB =
                        actualizada;


                    _itemNuevoVinculado.Descripcion =
                        "Vinculado con una persona existente.";


                    _itemNuevoVinculado
                        .CamposCambiados
                        .Clear();


                    analisis.Iguales.Add(
                        _itemNuevoVinculado);
                }

                // =============================================
                // CONFLICTO NORMAL
                // =============================================

                else
                {
                    analisis.Conflictos.Remove(
                        _item);


                    _item.Tipo =
                        TipoAnalisisJwHub.Igual;


                    _item.PersonaDB =
                        actualizada;


                    _item.Descripcion =
                        "Resuelto manualmente.";


                    _item.CamposCambiados.Clear();


                    analisis.Iguales.Add(
                        _item);
                }
            }


            // =================================================
            // PERSISTIR LA REVISIÓN ACTUALIZADA
            //
            // Si Android cierra el proceso después de resolver
            // este conflicto o esta vinculación, el elemento
            // no volverá a aparecer como pendiente.
            // =================================================

            JwHubSyncAnalysisService
                .GuardarUltimoAnalisis();


            // =================================================
            // MENSAJE
            // =================================================

            await DisplayAlert(
                "JW Hub",
                _modoVinculacion
                    ? "La persona se ha vinculado " +
                      "correctamente.\n\n" +
                      "Se ha conservado la ficha existente " +
                      "y todo su historial."
                    : "El conflicto se ha resuelto " +
                      "correctamente.",
                "Aceptar");


            // =================================================
            // NAVEGACIÓN
            //
            // Conflicto normal:
            //
            // Review -> Conflicto
            //          ↓
            //       volvemos a Review.
            //
            // Vinculación:
            //
            // Review -> Nueva persona -> Conflicto
            //
            // Queremos volver directamente a Review.
            // =================================================

            if (_modoVinculacion)
            {
                INavigation navegacion =
                    Navigation;


                // Quitar pantalla de resolución.
                await navegacion.PopAsync();


                // Quitar también pantalla "Nueva persona".
                await navegacion.PopAsync();


                return;
            }


            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "JW Hub",
                _modoVinculacion
                    ? "No se pudo vincular la persona:\n\n" +
                      ex.Message
                    : "No se pudo guardar la resolución:\n\n" +
                      ex.Message,
                "Aceptar");
        }
        finally
        {
            BtnGuardar.IsEnabled =
                true;
        }
    }


    // =========================================================
    // APLICAR VALOR DE JW HUB
    // =========================================================

    private static void AplicarValorHub(
        Persona destino,
        Persona hub,
        string clave)
    {
        switch (clave)
        {
            case "Nombre":

                // Nombre pertenece al S-21.
                // Nunca se sustituye desde JW Hub.
                break;


            case "NombreComun":

                destino.NombreComun =
                    hub.NombreComun;

                break;


            case "NombreJWHub":

                destino.NombreJWHub =
                    hub.NombreJWHub;

                break;


            case "Sexo":

                destino.Sexo =
                    hub.Sexo;

                break;


            case "Esperanza":

                destino.Esperanza =
                    hub.Esperanza;

                break;


            case "Nombramiento":

                destino.Nombramiento =
                    hub.Nombramiento;

                break;


            case "Servicio":

                destino.Servicio =
                    hub.Servicio;

                break;


            case "Privilegios":

                destino.Privilegios =
                    hub.Privilegios;

                break;


            case "FechaNacimiento":

                destino.FechaNacimiento =
                    hub.FechaNacimiento;

                break;


            case "FechaBautismo":

                destino.FechaBautismo =
                    hub.FechaBautismo;

                break;


            case "FechaInicioPrec":

                destino.FechaInicioPrec =
                    hub.FechaInicioPrec;

                break;


            case "FechaUltimaEscuela":

                destino.FechaUltimaEscuela =
                    hub.FechaUltimaEscuela;

                break;


            case "Email":

                destino.Email =
                    hub.Email;

                break;


            case "EmailJW":

                destino.EmailJW =
                    hub.EmailJW;

                break;


            case "Telefono":

                destino.Telefono =
                    hub.Telefono;

                break;


            case "Direccion":

                destino.Direccion =
                    hub.Direccion;

                break;


            case "Solicitudes":

                destino.Solicitudes =
                    hub.Solicitudes;

                break;
        }
    }


    // =========================================================
    // FOTOGRAFÍA
    //
    // La fotografía se excluye de la lista de campos porque
    // no es texto. Cuando el análisis detectó un cambio real
    // (dHash), se muestra esta tarjeta con las dos versiones
    // y se elige cuál conservar.
    // =========================================================

    private bool _usarFotoHub =
        true;


    private bool _fotoVisible;


    private void PrepararFoto()
    {
        _fotoVisible =
            _item.PersonaDB is not null
            &&
            _item.PersonaHub is not null
            &&
            !string.IsNullOrWhiteSpace(
                _item.PersonaHub.Foto)
            &&
            _item.CamposCambiados.Any(c =>
                string.Equals(
                    c,
                    "Fotografía",
                    StringComparison.OrdinalIgnoreCase));


        if (!_fotoVisible)
        {
            PanelFoto.IsVisible =
                false;

            return;
        }


        PanelFoto.IsVisible =
            true;


        ImgFotoBd.Source =
            ConvertirFoto(
                _item.PersonaDB!.Foto);


        ImgFotoHub.Source =
            ConvertirFoto(
                _item.PersonaHub!.Foto);


        // Por defecto se propone la de JW Hub, igual que en el
        // resto de campos.
        _usarFotoHub =
            true;


        RefrescarSeleccionFoto();
    }


    private static ImageSource? ConvertirFoto(
        string? foto)
    {
        if (string.IsNullOrWhiteSpace(foto))
        {
            return null;
        }


        return new SCCosmos.Mobile.Converters
            .Base64ImageConverter()
            .Convert(
                foto,
                typeof(ImageSource),
                null,
                System.Globalization.CultureInfo
                    .InvariantCulture)
            as ImageSource;
    }


    private void RefrescarSeleccionFoto()
    {
        LblMarcaFotoBd.Text =
            _usarFotoHub
                ? " "
                : "✓";


        LblMarcaFotoHub.Text =
            _usarFotoHub
                ? "✓"
                : " ";


        FotoBdBorder.StrokeThickness =
            _usarFotoHub
                ? 1
                : 3;


        FotoHubBorder.StrokeThickness =
            _usarFotoHub
                ? 3
                : 1;
    }


    private void FotoBd_Tapped(
        object? sender,
        TappedEventArgs e)
    {
        if (!_fotoVisible)
        {
            return;
        }


        _usarFotoHub =
            false;


        RefrescarSeleccionFoto();
    }


    private void FotoHub_Tapped(
        object? sender,
        TappedEventArgs e)
    {
        if (!_fotoVisible)
        {
            return;
        }


        _usarFotoHub =
            true;


        RefrescarSeleccionFoto();
    }


    // =========================================================
    // COMBINAR PRIVILEGIOS
    // =========================================================

    private static string CombinarPrivilegios(
        string? bd,
        string? hub)
    {
        List<string> resultado =
            new();


        foreach (string privilegio
                 in SepararLineas(bd))
        {
            if (!resultado.Contains(
                    privilegio,
                    StringComparer.OrdinalIgnoreCase))
            {
                resultado.Add(
                    privilegio);
            }
        }


        foreach (string privilegio
                 in SepararLineas(hub))
        {
            if (!resultado.Contains(
                    privilegio,
                    StringComparer.OrdinalIgnoreCase))
            {
                resultado.Add(
                    privilegio);
            }
        }


        return
            string.Join(
                "\n",
                resultado);
    }

#endif


    // =========================================================
    // CANCELAR
    // =========================================================

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }


#if ANDROID

    // =========================================================
    // MODELO VISUAL DE UN CAMPO
    // =========================================================

    public sealed class CampoResolucion
    {
        public string Clave
        {
            get;
            set;
        } = "";


        public string Nombre
        {
            get;
            set;
        } = "";


        public string ValorBd
        {
            get;
            set;
        } = "";


        public string ValorHub
        {
            get;
            set;
        } = "";


        public string ValorBdVisual =>
            string.IsNullOrWhiteSpace(
                ValorBd)
                ? "(Vacío)"
                : ValorBd;


        public string ValorHubVisual =>
            string.IsNullOrWhiteSpace(
                ValorHub)
                ? "(Vacío)"
                : ValorHub;


        public List<string> Opciones
        {
            get;
            set;
        } = new();


        public string Seleccion
        {
            get;
            set;
        } = "";
    }

#endif
}