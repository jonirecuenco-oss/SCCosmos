using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;


public partial class JwHubSyncNewPersonPage
    : ContentPage
{
#if ANDROID

    // =========================================================
    // ELEMENTO NUEVO PROCEDENTE DEL ANÁLISIS
    // =========================================================

    private readonly
        JwHubSyncItemAnalisis _item;


    // =========================================================
    // CANDIDATOS A VINCULAR
    // =========================================================

    private readonly List<CandidatoVinculacion>
        _candidatosVinculacion =
            new();

#endif


    // =========================================================
    // CONSTRUCTOR ANDROID
    // =========================================================

#if ANDROID

    public JwHubSyncNewPersonPage(
        JwHubSyncItemAnalisis item)
    {
        InitializeComponent();


        if (item.PersonaHub is null)
        {
            throw new ArgumentException(
                "El elemento no contiene una " +
                "persona de JW Hub.",
                nameof(item));
        }


        _item =
            item;


        BindingContext =
            item.PersonaHub;


        CargarCandidatosVinculacion();
    }

#else

    public JwHubSyncNewPersonPage()
    {
        InitializeComponent();
    }

#endif


#if ANDROID

    // =========================================================
    // CANDIDATOS DE VINCULACIÓN
    //
    // Utilizamos las posibles bajas de esta misma revisión.
    //
    // El caso típico es:
    //
    // HUB:
    //     nombre nuevo -> NUEVO
    //
    // BD:
    //     nombre antiguo -> POSIBLE BAJA
    //
    // Por eso las bajas son los candidatos naturales para
    // vincular.
    // =========================================================

    private void CargarCandidatosVinculacion()
    {
        _candidatosVinculacion.Clear();


        ResultadoAnalisisJwHub?
            analisis =
                JwHubSyncAnalysisService
                    .UltimoAnalisis;


        Persona? hub =
            _item.PersonaHub;


        if (analisis is null
            ||
            hub is null)
        {
            PanelVincular.IsVisible =
                false;

            return;
        }


        foreach (
            JwHubSyncItemAnalisis baja
            in analisis.Bajas)
        {
            if (baja.PersonaDB is null)
            {
                continue;
            }


            Persona db =
                baja.PersonaDB;


            int puntuacion =
                CalcularPuntuacionVinculacion(
                    db,
                    hub);


            string nombre =
                !string.IsNullOrWhiteSpace(
                    db.NombreMostrarTabla)
                    ? db.NombreMostrarTabla
                    : !string.IsNullOrWhiteSpace(
                        db.NombreComun)
                        ? db.NombreComun
                        : db.Nombre;


            string congregacion =
                !string.IsNullOrWhiteSpace(
                    db.CongregacionNombre)
                    ? db.CongregacionNombre
                    : db.CongregacionCodigo;


            string nacimiento =
                string.IsNullOrWhiteSpace(
                    db.FechaNacimiento)
                    ? ""
                    : $" · Nac. {db.FechaNacimiento}";


            string estrella =
                puntuacion >= 6
                    ? "★ "
                    : "";


            _candidatosVinculacion.Add(
                new CandidatoVinculacion
                {
                    ItemBaja =
                        baja,

                    Puntuacion =
                        puntuacion,

                    Texto =
                        $"{estrella}" +
                        $"{nombre}" +
                        $" · {congregacion}" +
                        $"{nacimiento}"
                });
        }


        List<CandidatoVinculacion>
            ordenados =
                _candidatosVinculacion
                    .OrderByDescending(x =>
                        x.Puntuacion)
                    .ThenBy(x =>
                        x.ItemBaja
                            .PersonaDB?
                            .NombreMostrarTabla)
                    .ToList();


        PickerVincular.ItemsSource =
            ordenados;


        PanelVincular.IsVisible =
            ordenados.Count > 0;


        // =====================================================
        // SI SOLO HAY UN CANDIDATO MUY CLARO, LO DEJAMOS
        // SELECCIONADO.
        //
        // NO se vincula automáticamente.
        // =====================================================

        if (ordenados.Count == 1
            &&
            ordenados[0].Puntuacion >= 6)
        {
            PickerVincular.SelectedItem =
                ordenados[0];
        }
    }


    // =========================================================
    // CALCULAR PUNTUACIÓN DE VINCULACIÓN
    //
    // Solo sirve para ordenar candidatos.
    //
    // Nunca decide automáticamente que sean la misma persona.
    // =========================================================

    private static int
        CalcularPuntuacionVinculacion(
            Persona db,
            Persona hub)
    {
        int puntos =
            0;


        // =====================================================
        // MISMA CONGREGACIÓN
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                db.CongregacionCodigo)
            &&
            !string.IsNullOrWhiteSpace(
                hub.CongregacionCodigo)
            &&
            string.Equals(
                db.CongregacionCodigo,
                hub.CongregacionCodigo,
                StringComparison.OrdinalIgnoreCase))
        {
            puntos +=
                4;
        }


        // =====================================================
        // NACIMIENTO
        // =====================================================

        if (FechasParecidas(
                db.FechaNacimiento,
                hub.FechaNacimiento))
        {
            puntos +=
                4;
        }


        // =====================================================
        // BAUTISMO
        // =====================================================

        if (FechasParecidas(
                db.FechaBautismo,
                hub.FechaBautismo))
        {
            puntos +=
                3;
        }


        // =====================================================
        // EMAIL JW
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                db.EmailJW)
            &&
            !string.IsNullOrWhiteSpace(
                hub.EmailJW)
            &&
            string.Equals(
                db.EmailJW.Trim(),
                hub.EmailJW.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            puntos +=
                5;
        }


        // =====================================================
        // EMAIL PERSONAL
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                db.Email)
            &&
            !string.IsNullOrWhiteSpace(
                hub.Email)
            &&
            string.Equals(
                db.Email.Trim(),
                hub.Email.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            puntos +=
                4;
        }


        // =====================================================
        // TELÉFONO
        // =====================================================

        string telefonoDb =
            NormalizarTelefono(
                db.Telefono);


        string telefonoHub =
            NormalizarTelefono(
                hub.Telefono);


        if (!string.IsNullOrEmpty(
                telefonoDb)
            &&
            !string.IsNullOrEmpty(
                telefonoHub)
            &&
            telefonoDb ==
                telefonoHub)
        {
            puntos +=
                3;
        }


        return puntos;
    }


    // =========================================================
    // FECHAS PARECIDAS
    //
    // Ejemplos:
    //
    // 14/05/1980 == 14/05/1980
    //
    // 01/01/1980 ~ 14/05/1980
    //
    // si una de ellas es aproximada.
    // =========================================================

    private static bool FechasParecidas(
        string? a,
        string? b)
    {
        if (string.IsNullOrWhiteSpace(a)
            ||
            string.IsNullOrWhiteSpace(b))
        {
            return false;
        }


        string f1 =
            a.Trim();


        string f2 =
            b.Trim();


        if (string.Equals(
                f1,
                f2,
                StringComparison.Ordinal))
        {
            return true;
        }


        bool aproximada1 =
            f1.StartsWith(
                "01/01/",
                StringComparison.Ordinal);


        bool aproximada2 =
            f2.StartsWith(
                "01/01/",
                StringComparison.Ordinal);


        if (!aproximada1
            &&
            !aproximada2)
        {
            return false;
        }


        int anio1 =
            ObtenerAnioFecha(
                f1);


        int anio2 =
            ObtenerAnioFecha(
                f2);


        return
            anio1 > 0
            &&
            anio2 > 0
            &&
            anio1 == anio2;
    }


    // =========================================================
    // OBTENER AÑO
    // =========================================================

    private static int ObtenerAnioFecha(
        string fecha)
    {
        if (string.IsNullOrWhiteSpace(
                fecha))
        {
            return 0;
        }


        string limpia =
            fecha.Trim();


        if (limpia.Length < 4)
        {
            return 0;
        }


        string ultimos4 =
            limpia.Substring(
                limpia.Length - 4);


        return
            int.TryParse(
                ultimos4,
                out int anio)
                ? anio
                : 0;
    }


    // =========================================================
    // NORMALIZAR TELÉFONO
    // =========================================================

    private static string NormalizarTelefono(
        string? telefono)
    {
        if (string.IsNullOrWhiteSpace(
                telefono))
        {
            return "";
        }


        string digitos =
            new string(
                telefono
                    .Where(
                        char.IsDigit)
                    .ToArray());


        // España:
        //
        // +34 612345678
        //
        // queda:
        //
        // 612345678

        if (digitos.Length == 11
            &&
            digitos.StartsWith(
                "34",
                StringComparison.Ordinal))
        {
            digitos =
                digitos.Substring(
                    2);
        }


        return digitos;
    }


    // =========================================================
    // VINCULAR CON PERSONA EXISTENTE
    // =========================================================

    private async void BtnVincular_Clicked(
        object sender,
        EventArgs e)
    {
        if (_item.PersonaHub is null)
        {
            return;
        }


        if (PickerVincular.SelectedItem
            is not CandidatoVinculacion candidato)
        {
            await DisplayAlert(
                "Vincular persona",
                "Selecciona primero la persona " +
                "existente con la que quieres " +
                "vincular este registro.",
                "Aceptar");

            return;
        }


        if (candidato.ItemBaja.PersonaDB
            is null)
        {
            await DisplayAlert(
                "Vincular persona",
                "La ficha seleccionada ya no está " +
                "disponible.",
                "Aceptar");

            return;
        }


        Persona existente =
            candidato
                .ItemBaja
                .PersonaDB;


        string nombreExistente =
            !string.IsNullOrWhiteSpace(
                existente.NombreMostrarTabla)
                ? existente.NombreMostrarTabla
                : !string.IsNullOrWhiteSpace(
                    existente.NombreComun)
                    ? existente.NombreComun
                    : existente.Nombre;


        bool confirmar =
            await DisplayAlert(
                "Vincular persona",
                $"Vas a indicar que:\n\n" +
                $"JW Hub:\n" +
                $"{_item.NombreVisual}\n\n" +
                $"es la misma persona que:\n\n" +
                $"SCCosmos:\n" +
                $"{nombreExistente}\n\n" +
                "No se creará una nueva ficha. " +
                "La persona existente conservará " +
                "su Id y todo su historial.\n\n" +
                "A continuación podrás decidir " +
                "campo por campo qué valores conservar.",
                "Continuar",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        // =====================================================
        // CREAR COMPARACIÓN TEMPORAL
        //
        // No se añade a Conflictos.
        //
        // JwHubSyncConflictPage utilizará:
        //
        // PersonaDB  -> ficha existente
        // PersonaHub -> registro de JW Hub
        // =====================================================

        var comparacion =
            new JwHubSyncItemAnalisis
            {
                Tipo =
                    TipoAnalisisJwHub.Conflicto,

                PersonaDB =
                    existente,

                PersonaHub =
                    _item.PersonaHub,

                NombreVisual =
                    _item.NombreVisual,

                Descripcion =
                    "Vinculación con una persona existente."
            };


        await Navigation.PushAsync(
            new JwHubSyncConflictPage(
                comparacion,
                _item,
                candidato.ItemBaja));
    }


    // =========================================================
    // AÑADIR COMO PERSONA REALMENTE NUEVA
    // =========================================================

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        if (_item.PersonaHub is null)
        {
            return;
        }


        bool confirmar =
            await DisplayAlert(
                "Añadir persona",
                $"¿Añadir a {_item.NombreVisual} " +
                "como una nueva persona en SCCosmos?\n\n" +
                "Utiliza esta opción solo si has comprobado " +
                "que no corresponde a ninguna ficha existente.",
                "Añadir",
                "Cancelar");


        if (!confirmar)
        {
            return;
        }


        BtnAnadir.IsEnabled =
            false;


        try
        {
            Persona personaNueva =
                CrearPersonaParaInsertar(
                    _item.PersonaHub);


            int nuevoId =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .InsertarPersonaNueva(
                                personaNueva));


            if (nuevoId <= 0)
            {
                throw new Exception(
                    "La base de datos no devolvió " +
                    "un identificador válido.");
            }


            personaNueva.Id =
                nuevoId;


            // =================================================
            // NUEVO -> IGUAL
            // =================================================

            ResultadoAnalisisJwHub?
                analisis =
                    JwHubSyncAnalysisService
                        .UltimoAnalisis;


            if (analisis is not null)
            {
                analisis.Nuevos.Remove(
                    _item);


                _item.Tipo =
                    TipoAnalisisJwHub.Igual;


                _item.PersonaDB =
                    personaNueva;


                _item.Descripcion =
                    "Añadido desde JW Hub.";


                _item.CamposCambiados.Clear();


                analisis.Iguales.Add(
                    _item);


                // =============================================
                // PERSISTIR EL NUEVO ESTADO
                //
                // Si Android cierra el proceso a continuación,
                // esta persona no reaparecerá como Nuevo.
                // =============================================

                JwHubSyncAnalysisService
                    .GuardarUltimoAnalisis();
            }


            // =================================================
            // OFRECER ABRIR LA FICHA NORMAL
            // =================================================

            bool abrirFicha =
                await DisplayAlert(
                    "Persona añadida",
                    "La persona se ha añadido " +
                    "correctamente.\n\n" +
                    "¿Quieres abrir ahora su ficha " +
                    "para completar datos locales?",
                    "Abrir ficha",
                    "Volver");


            // Volvemos a la revisión.

            await Navigation.PopAsync();


            if (abrirFicha)
            {
                await Navigation.PushAsync(
                    new PersonaDetallePage(
                        nuevoId));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "JW Hub",
                "No se pudo añadir la persona:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            BtnAnadir.IsEnabled =
                true;
        }
    }


    // =========================================================
    // COPIAR PERSONA HUB PARA INSERTAR
    //
    // Evitamos insertar directamente el objeto PersonaHub
    // porque sigue formando parte del análisis.
    // =========================================================

    private static Persona CrearPersonaParaInsertar(
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


    // =========================================================
    // MODELO DE CANDIDATO
    // =========================================================

    public sealed class CandidatoVinculacion
    {
        public JwHubSyncItemAnalisis ItemBaja
        {
            get;
            set;
        } = null!;


        public int Puntuacion
        {
            get;
            set;
        }


        public string Texto
        {
            get;
            set;
        } = "";
    }

#endif


    // =========================================================
    // CANCELAR
    //
    // No modifica ni SQLite ni el análisis.
    // =========================================================

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }
}