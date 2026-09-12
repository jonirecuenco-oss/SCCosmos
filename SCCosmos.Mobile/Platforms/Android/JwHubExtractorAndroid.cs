using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MauiMainThread =
    Microsoft.Maui.ApplicationModel.MainThread;

using MauiPlatform =
    Microsoft.Maui.ApplicationModel.Platform;

using AndroidWebView =
    global::Android.Webkit.WebView;

using AndroidCookieManager =
    global::Android.Webkit.CookieManager;

using AndroidIValueCallback =
    global::Android.Webkit.IValueCallback;

using AndroidContext =
    global::Android.Content.Context;

using AndroidApplication =
    global::Android.App.Application;

using JavaObject =
    global::Java.Lang.Object;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;


// =============================================================
// PERSONA EXTRAÍDA DE JW HUB
// =============================================================

public sealed class PersonaExtraidaJwHubAndroid
{
    public string NombreCompleto { get; set; } = "";
    public string NombreComun { get; set; } = "";
    public string Apellidos { get; set; } = "";

    public string FechaNacimiento { get; set; } = "";
    public string FechaBautismo { get; set; } = "";
    public string FechaInicioPrec { get; set; } = "";
    public string FechaUltimaEscuela { get; set; } = "";

    public string Email { get; set; } = "";
    public string EmailJW { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Direccion { get; set; } = "";

    public string Congregacion { get; set; } = "";
    public string Servicio { get; set; } = "";
    public string Nombramiento { get; set; } = "";

    public string Solicitudes { get; set; } = "";
    public string Privilegios { get; set; } = "";
    public string Esperanza { get; set; } = "";

    public string Foto { get; set; } = "";

    public string UrlFoto { get; set; } = "";
    public string UrlPerfil { get; set; } = "";
}


// =============================================================
// ERROR DE UNA PERSONA
// =============================================================

public sealed class ErrorExtraccionJwHubAndroid
{
    public int Indice { get; set; }

    public string Nombre { get; set; } = "";

    public string Mensaje { get; set; } = "";
}


// =============================================================
// RESULTADO COMPLETO
// =============================================================

public sealed class ResultadoSintonizacionJwHubAndroid
{
    public int TotalPersonas { get; set; }

    public bool EsPruebaLimitada { get; set; }

    public string PrimeraPersona { get; set; } = "";

    public List<PersonaExtraidaJwHubAndroid>
        Personas
    { get; set; } = [];

    public List<ErrorExtraccionJwHubAndroid>
        Errores
    { get; set; } = [];


    public int TotalExtraidas =>
        Personas.Count;


    public int TotalProcesadas =>
        Personas.Count +
        Errores.Count;


    public bool ExtraccionCompleta =>
        TotalPersonas > 0
        &&
        TotalExtraidas == TotalPersonas
        &&
        Errores.Count == 0;


    public string CrearResumen()
    {
        var sb =
            new StringBuilder();


        if (ExtraccionCompleta)
        {
            sb.Append(
                "Sincronización JW Hub completada. ");

            sb.Append(
                $"{TotalExtraidas}/{TotalPersonas} " +
                "personas extraídas correctamente.");

            return sb.ToString();
        }


        sb.Append(
            "Sincronización JW Hub terminada con incidencias. ");

        sb.Append(
            $"{TotalExtraidas}/{TotalPersonas} " +
            "personas extraídas correctamente.");

        if (Errores.Count > 0)
        {
            sb.Append(
                $" Fallaron {Errores.Count}.");

            foreach (
                ErrorExtraccionJwHubAndroid error
                in Errores.Take(3))
            {
                sb.AppendLine();

                sb.Append(
                    $"{error.Indice}. " +
                    $"{error.Nombre}: " +
                    $"{error.Mensaje}");
            }

            if (Errores.Count > 3)
            {
                sb.AppendLine();

                sb.Append(
                    $"...y {Errores.Count - 3} " +
                    "incidencias más.");
            }
        }


        return sb.ToString();
    }
}


// =============================================================
// SESIÓN CADUCADA
// =============================================================

public sealed class JwHubSessionRequiredException
    : Exception
{
    public JwHubSessionRequiredException(
        string mensaje)
        : base(mensaje)
    {
    }
}


// =============================================================
// EXTRACTOR JW HUB ANDROID
// =============================================================

public static class JwHubExtractorAndroid
{
    private const string UrlBase =
        "https://hub.jw.org/";


    private const string UrlPersonas =
        "https://hub.jw.org/" +
        "congregation-persons/es/circuits/" +
        "512bc4a5-94aa-ea11-815e-0050560012f5/" +
        "records";

    // =============================================================
    // MODO DE PRUEBA
    //
    // 5 = procesar solo las 5 primeras personas.
    // 0 = procesar todas las personas de JW Hub.
    // =============================================================

    private const int LimitePersonasPrueba = 0;


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


    private static readonly
        JsonSerializerOptions OpcionesJson =
        new()
        {
            PropertyNameCaseInsensitive =
                true
        };


    // Conservamos temporalmente el último resultado
    // en memoria.
    //
    // Lo utilizaremos en el siguiente paso para
    // comparar contra SQLite.
    public static
        ResultadoSintonizacionJwHubAndroid?
        UltimoResultado
    { get; private set; }


    private static readonly
        HttpClient ClienteHttp =
        CrearClienteHttp();


    // =========================================================
    // SINCRONIZACIÓN COMPLETA
    // =========================================================

    public static async Task
    <ResultadoSintonizacionJwHubAndroid>
    SintonizarTodasLasPersonasAsync(
        CancellationToken token)
    {
        AndroidWebView? webViewListado =
            null;

        AndroidWebView? webViewDetalle =
            null;


        UltimoResultado =
            null;


        try
        {
            token.ThrowIfCancellationRequested();


            // =====================================================
            // WEBVIEW 1:
            // LISTADO
            // =====================================================

            webViewListado =
                await CrearWebViewAsync();


            ActualizarEstado(
                "Accediendo a JW Hub...");


            await CargarUrlYEsperarElementoAsync(
                webViewListado,
                UrlPersonas,
                "app-circuit-persons",
                token);


            bool accesoValido =
                await EsperarAccesoPersonasAsync(
                    webViewListado,
                    token);


            if (!accesoValido)
            {
                JwHubSessionState
                    .MarcarSesionNoValida();


                throw new
                    JwHubSessionRequiredException(
                        "La sesión de JW Hub " +
                        "no es válida o ha caducado.");
            }


            // =====================================================
            // BUSCAR
            // =====================================================

            ActualizarEstado(
                "Buscando publicadores...");


            await Task.Delay(
                700,
                token);


            string resultadoBuscar =
                await PulsarBuscarAsync(
                    webViewListado,
                    token);


            if (resultadoBuscar != "1")
            {
                throw new Exception(
                    "No se encontró el botón Buscar.");
            }


            // =====================================================
            // ESPERAR PRIMERAS TARJETAS
            // =====================================================

            bool hayTarjetas =
                await EsperarCondicionAsync(
                    webViewListado,
                    """
                (() => {
                    return document.querySelectorAll(
                        '#cards-list > app-circuit-person-card'
                    ).length > 0
                        ? '1'
                        : '0';
                })();
                """,
                    TimeSpan.FromSeconds(15),
                    token);


            if (!hayTarjetas)
            {
                throw new Exception(
                    "JW Hub no mostró las tarjetas " +
                    "de publicadores.");
            }


            // =====================================================
            // LEER TOTAL REAL DE JW HUB
            //
            // Por ejemplo: 769.
            // =====================================================

            int totalDisponible =
                await ObtenerTotalResultadosAsync(
                    webViewListado,
                    token);


            if (totalDisponible <= 0)
            {
                throw new Exception(
                    "No se pudo determinar el " +
                    "número total de publicadores.");
            }


            // =====================================================
            // DECIDIR CUÁNTAS VAMOS A PROCESAR
            //
            // LimitePersonasPrueba = 3
            //      -> solo 3.
            //
            // LimitePersonasPrueba = 0
            //      -> todas.
            // =====================================================

            int totalAProcesar =
                LimitePersonasPrueba > 0
                    ? Math.Min(
                        LimitePersonasPrueba,
                        totalDisponible)
                    : totalDisponible;


            ActualizarEstado(
                LimitePersonasPrueba > 0
                    ? $"Modo prueba: " +
                      $"{totalAProcesar} de " +
                      $"{totalDisponible} personas"
                    : $"Preparando " +
                      $"{totalDisponible} personas...");


            // =====================================================
            // COMPROBAR CUÁNTAS TARJETAS YA ESTÁN CARGADAS
            //
            // En nuestro caso JW Hub carga 75 inicialmente,
            // por lo que para una prueba de 3 no necesitamos
            // pulsar "Mostrar más".
            // =====================================================

            int tarjetasCargadas =
                await ContarTarjetasPersonaAsync(
                    webViewListado,
                    token);


            // Solo cargamos más resultados si realmente
            // hacen falta para alcanzar el límite de prueba.
            if (tarjetasCargadas <
                totalAProcesar)
            {
                await CargarTodosLosResultadosAsync(
                    webViewListado,
                    totalAProcesar,
                    token);


                tarjetasCargadas =
                    await ContarTarjetasPersonaAsync(
                        webViewListado,
                        token);
            }


            if (tarjetasCargadas <
                totalAProcesar)
            {
                throw new Exception(
                    $"Solo se cargaron " +
                    $"{tarjetasCargadas} tarjetas " +
                    $"y necesitamos " +
                    $"{totalAProcesar}.");
            }


            // =====================================================
            // PRIMER NOMBRE
            // =====================================================

            string primeraPersona =
                await ObtenerNombrePersonaAsync(
                    webViewListado,
                    0,
                    token);


            // =====================================================
            // WEBVIEW 2:
            // PERFIL / ESTADO / SOLICITUDES / EDUCACIÓN
            //
            // Lo reutilizamos para las personas de la prueba.
            // =====================================================

            webViewDetalle =
                await CrearWebViewAsync();


            var resultado =
    new ResultadoSintonizacionJwHubAndroid
    {
        TotalPersonas =
            totalAProcesar,

        EsPruebaLimitada =
            LimitePersonasPrueba > 0,

        PrimeraPersona =
            primeraPersona
    };


            // =====================================================
            // RECORRER SOLO LAS PERSONAS DE LA PRUEBA
            // =====================================================

            for (int indice = 0;
                 indice < totalAProcesar;
                 indice++)
            {
                token.ThrowIfCancellationRequested();


                int numeroPersona =
                    indice + 1;


                string nombre =
                    await ObtenerNombrePersonaAsync(
                        webViewListado,
                        indice,
                        token);


                if (string.IsNullOrWhiteSpace(
                        nombre))
                {
                    nombre =
                        $"Persona {numeroPersona}";
                }


                ActualizarEstado(
                    $"Sincronizando " +
                    $"{numeroPersona}/" +
                    $"{totalAProcesar} · " +
                    $"{nombre}",
                    numeroPersona - 1,
                    totalAProcesar,
                    EstadoJwHubSync.Sintonizando);


                PersonaExtraidaJwHubAndroid?
                    persona =
                        await ExtraerPersonaConReintentosAsync(
                            webViewListado,
                            webViewDetalle,
                            indice,
                            totalAProcesar,
                            nombre,
                            token);


                if (persona is not null)
                {
                    resultado.Personas.Add(
                        persona);
                }
                else
                {
                    string mensajeError =
                        _ultimoErrorPersona;


                    if (string.IsNullOrWhiteSpace(
                            mensajeError))
                    {
                        mensajeError =
                            "No se pudo obtener una " +
                            "extracción válida tras " +
                            "cinco intentos.";
                    }


                    resultado.Errores.Add(
                        new ErrorExtraccionJwHubAndroid
                        {
                            Indice =
                                numeroPersona,

                            Nombre =
                                nombre,

                            Mensaje =
                                mensajeError
                        });
                }


                // =================================================
                // PROGRESO DE LA PERSONA TERMINADA
                // =================================================

                string mensajeFinalPersona =
                    persona is not null
                        ? $"Sincronizando " +
                          $"{numeroPersona}/" +
                          $"{totalAProcesar} · " +
                          $"{nombre}"
                        : $"Sincronizando " +
                          $"{numeroPersona}/" +
                          $"{totalAProcesar} · " +
                          $"incidencia en {nombre}";


                ActualizarEstado(
                    mensajeFinalPersona,
                    numeroPersona,
                    totalAProcesar,
                    EstadoJwHubSync.Sintonizando);
            }


            // =====================================================
            // FIN
            // =====================================================

            JwHubSessionState
                .MarcarSesionValida();


            UltimoResultado =
                resultado;


            return resultado;
        }
        finally
        {
            if (webViewDetalle is not null)
            {
                await DestruirWebViewAsync(
                    webViewDetalle);
            }


            if (webViewListado is not null)
            {
                await DestruirWebViewAsync(
                    webViewListado);
            }
        }
    }


    // =========================================================
    // ÚLTIMO ERROR DE LA PERSONA ACTUAL
    //
    // Solo lo usamos para informar al terminar sus
    // cinco reintentos.
    // =========================================================

    private static string
        _ultimoErrorPersona = "";


    // =========================================================
    // EXTRAER UNA PERSONA CON REINTENTOS
    // =========================================================

    private static async Task
        <PersonaExtraidaJwHubAndroid?>
        ExtraerPersonaConReintentosAsync(
            AndroidWebView webViewListado,
            AndroidWebView webViewDetalle,
            int indice,
            int totalPersonas,
            string nombreEsperado,
            CancellationToken token)
    {
        _ultimoErrorPersona =
            "";


        // Igual que Windows:
        // hasta 5 intentos por fallos críticos.
        for (int revision = 1;
             revision <= 5;
             revision++)
        {
            token.ThrowIfCancellationRequested();


            try
            {
                if (revision > 1)
                {
                    ActualizarEstado(
                        $"Reintentando " +
                        $"{indice + 1}/{totalPersonas} · " +
                        $"{nombreEsperado} " +
                        $"({revision}/5)",
                        indice,
                        totalPersonas,
                        EstadoJwHubSync.Sintonizando);


                    // En Android no necesitamos el
                    // Thread.Sleep(1500) fijo del PC.
                    // Esperamos un poco y después todos
                    // los pasos esperan al DOM real.
                    await Task.Delay(
                        500,
                        token);
                }


                // =============================================
                // ABRIR SOLO LA TARJETA DE ESTA PERSONA
                // =============================================

                bool preparada =
                    await PrepararTarjetaPersonaAsync(
                        webViewListado,
                        indice,
                        token);


                if (!preparada)
                {
                    throw new Exception(
                        "No se pudo desplegar " +
                        "el detalle de la tarjeta.");
                }


                DatosTarjetaJs datosTarjeta;


                try
                {
                    datosTarjeta =
                        await ObtenerDatosTarjetaPersonaAsync(
                            webViewListado,
                            indice,
                            token);
                }
                finally
                {
                    // Cerramos cuanto antes la tarjeta.
                    //
                    // No necesitamos mantener 769
                    // detalles abiertos en memoria.
                    try
                    {
                        await CerrarTarjetaPersonaAsync(
                            webViewListado,
                            indice,
                            CancellationToken.None);
                    }
                    catch
                    {
                    }
                }


                var persona =
                    new PersonaExtraidaJwHubAndroid();


                AplicarDatosTarjeta(
                    persona,
                    datosTarjeta);


                if (string.IsNullOrWhiteSpace(
                        persona.NombreCompleto))
                {
                    persona.NombreCompleto =
                        nombreEsperado;
                }


                if (string.IsNullOrWhiteSpace(
                        persona.UrlPerfil))
                {
                    throw new Exception(
                        "No apareció el enlace " +
                        "«Ver más» de la persona.");
                }


                persona.UrlPerfil =
                    AbsolutizarUrl(
                        persona.UrlPerfil);


                // =============================================
                // RESUMEN + ESTADO + SOLICITUDES + EDUCACIÓN
                // =============================================

                await ExtraerSeccionesPersonaAsync(
                    webViewDetalle,
                    persona,
                    indice + 1,
                    totalPersonas,
                    token);


                // =============================================
                // VALIDACIÓN CRÍTICA
                //
                // Igual que el PC:
                //
                // - Congregación
                // - Fecha nacimiento
                // - Fecha bautismo
                // =============================================

                bool faltanDatosGraves =
                    string.IsNullOrWhiteSpace(
                        persona.Congregacion)
                    ||
                    string.IsNullOrWhiteSpace(
                        persona.FechaNacimiento)
                    ||
                    string.IsNullOrWhiteSpace(
                        persona.FechaBautismo);


                if (faltanDatosGraves)
                {
                    var faltantes =
                        new List<string>();


                    if (string.IsNullOrWhiteSpace(
                            persona.Congregacion))
                    {
                        faltantes.Add(
                            "congregación");
                    }


                    if (string.IsNullOrWhiteSpace(
                            persona.FechaNacimiento))
                    {
                        faltantes.Add(
                            "nacimiento");
                    }


                    if (string.IsNullOrWhiteSpace(
                            persona.FechaBautismo))
                    {
                        faltantes.Add(
                            "bautismo");
                    }


                    throw new Exception(
                        "Faltan datos obligatorios: " +
                        string.Join(
                            ", ",
                            faltantes));
                }


                // =============================================
                // COMPROBACIÓN DE QUE EL PERFIL ABIERTO
                // CORRESPONDE A LA PERSONA CORRECTA
                // =============================================

                if (!string.IsNullOrWhiteSpace(
                        datosTarjeta.NombreCompleto)
                    &&
                    !string.IsNullOrWhiteSpace(
                        nombreEsperado))
                {
                    string a =
                        NormalizarTexto(
                            datosTarjeta.NombreCompleto);


                    string b =
                        NormalizarTexto(
                            nombreEsperado);


                    if (a != b)
                    {
                        throw new Exception(
                            "El nombre obtenido de " +
                            "la tarjeta no coincide " +
                            "con la persona esperada.");
                    }
                }


                // Extracción válida.
                return persona;
            }
            catch (
                System.OperationCanceledException)
            {
                throw;
            }
            catch (
                JwHubSessionRequiredException)
            {
                // Si caduca la sesión no tiene sentido
                // reintentar cinco veces cada persona.
                throw;
            }
            catch (Exception ex)
            {
                _ultimoErrorPersona =
                    ex.Message;


                // Nos aseguramos de dejar cerrada
                // la tarjeta antes del siguiente intento.
                try
                {
                    await CerrarTarjetaPersonaAsync(
                        webViewListado,
                        indice,
                        CancellationToken.None);
                }
                catch
                {
                }


                if (revision >= 5)
                {
                    return null;
                }
            }
        }


        return null;
    }


    // =========================================================
    // EXTRAER LAS SECCIONES DE UNA PERSONA
    // =========================================================

    private static async Task
        ExtraerSeccionesPersonaAsync(
            AndroidWebView webViewDetalle,
            PersonaExtraidaJwHubAndroid persona,
            int numeroPersona,
            int totalPersonas,
            CancellationToken token)
    {
        // =====================================================
        // 1. RESUMEN
        // =====================================================

        ActualizarEstado(
            $"Sincronizando " +
            $"{numeroPersona}/{totalPersonas} · " +
            $"perfil",
            numeroPersona - 1,
            totalPersonas,
            EstadoJwHubSync.Sintonizando);



        await CargarUrlYEsperarElementoAsync(
    webViewDetalle,
    persona.UrlPerfil,
    "app-circuit-person-overview",
    token);


        // =====================================================
        // COMPROBAR IDENTIDAD POR ID DE JW HUB
        //
        // Mucho más fiable que comparar nombres.
        //
        // Una persona puede no tener:
        // - foto
        // - email
        // - email JW
        // - dirección
        //
        // y eso NO afecta a esta comprobación.
        // =====================================================

        bool perfilCorrecto =
            await EsperarPerfilPersonaAsync(
                webViewDetalle,
                persona.UrlPerfil,
                token);


        if (!perfilCorrecto)
        {
            string urlActual =
                "";


            try
            {
                urlActual =
                    await ObtenerUrlActualAsync(
                        webViewDetalle,
                        token);
            }
            catch
            {
            }


            throw new Exception(
                $"El perfil no terminó de cargar " +
                $"correctamente. " +
                $"ID esperado: " +
                $"«{ObtenerIdPersonaDesdeUrl(persona.UrlPerfil)}». " +
                $"URL actual: «{urlActual}».");
        }


        // =====================================================
        // ESPERAR COMPONENTES DEL RESUMEN
        //
        // app-contact-overview puede existir aunque sus campos
        // concretos estén vacíos.
        //
        // No exigimos foto, email ni dirección.
        // =====================================================

        bool resumenPreparado =
            await EsperarCondicionAsync(
                webViewDetalle,
                """
        (() => {
            const pagina =
                document.querySelector(
                    'app-circuit-person-overview');

            if (!pagina)
                return '0';

            const persona =
                pagina.querySelector(
                    'app-person-overview');

            return persona
                ? '1'
                : '0';
        })();
        """,
                TimeSpan.FromSeconds(10),
                token);


        if (!resumenPreparado)
        {
            throw new Exception(
                "JW Hub abrió el perfil correcto, " +
                "pero no terminó de cargar el resumen.");
        }


        // Ahora sí leemos los campos.
        //
        // Todos ellos pueden estar vacíos sin que la
        // persona se considere errónea.
        DatosPerfilJs perfil =
            await ObtenerDatosPerfilAsync(
                webViewDetalle,
                token);


        persona.Email =
            perfil.Email;


        persona.EmailJW =
            perfil.EmailJW;


        persona.Direccion =
            perfil.Direccion;


        persona.UrlFoto =
            perfil.UrlFoto;


        // =====================================================
        // FOTO
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                persona.UrlFoto))
        {
            ActualizarEstado(
                $"Sincronizando " +
                $"{numeroPersona}/{totalPersonas} · " +
                $"fotografía",
                numeroPersona - 1,
                totalPersonas,
                EstadoJwHubSync.Sintonizando);


            persona.Foto =
                await DescargarFotoBase64Async(
                    persona.UrlFoto,
                    token);
        }


        // =====================================================
        // RAÍZ DE ESTA PERSONA
        // =====================================================

        string raizPersona =
            ObtenerRaizPersona(
                persona.UrlPerfil);


        // =====================================================
        // 2. ESTADO
        // =====================================================

        ActualizarEstado(
            $"Sincronizando " +
            $"{numeroPersona}/{totalPersonas} · " +
            $"estado",
            numeroPersona - 1,
            totalPersonas,
            EstadoJwHubSync.Sintonizando);


        string urlEstado =
            raizPersona +
            "/spiritual-status";


        await CargarUrlYEsperarElementoAsync(
            webViewDetalle,
            urlEstado,
            "app-spiritual-status-view-container",
            token);


        // Esperamos a que existan los pares reales.
        await EsperarCondicionAsync(
            webViewDetalle,
            """
            (() => {
                const pagina =
                    document.querySelector(
                        'app-spiritual-status-view-container');

                return (
                    pagina &&
                    pagina.querySelectorAll(
                        '.data__pair').length > 0
                )
                    ? '1'
                    : '0';
            })();
            """,
            TimeSpan.FromSeconds(10),
            token);


        DatosEstadoJs estado =
            await ObtenerDatosEstadoAsync(
                webViewDetalle,
                token);


        if (!string.IsNullOrWhiteSpace(
                estado.Esperanza))
        {
            if (estado.Esperanza.Contains(
                    "Otras",
                    StringComparison.OrdinalIgnoreCase))
            {
                persona.Esperanza =
                    "Otras ovejas";
            }
            else if (estado.Esperanza.Contains(
                         "Ungido",
                         StringComparison.OrdinalIgnoreCase))
            {
                persona.Esperanza =
                    "Ungido";
            }
            else
            {
                persona.Esperanza =
                    estado.Esperanza.Trim();
            }
        }


        if (!string.IsNullOrWhiteSpace(
                estado.FechaInicioPrec))
        {
            persona.FechaInicioPrec =
                ConvertirFechaJWHub(
                    LimpiarFechaCruda(
                        estado.FechaInicioPrec));
        }


        // =====================================================
        // 3. SOLICITUDES VIGENTES
        // =====================================================

        ActualizarEstado(
            $"Sincronizando " +
            $"{numeroPersona}/{totalPersonas} · " +
            $"solicitudes",
            numeroPersona - 1,
            totalPersonas,
            EstadoJwHubSync.Sintonizando);


        string urlSolicitudes =
            raizPersona +
            "/applications";


        await CargarUrlYEsperarElementoAsync(
            webViewDetalle,
            urlSolicitudes,
            "app-applications-container",
            token);


        persona.Solicitudes =
            await ObtenerSolicitudesAsync(
                webViewDetalle,
                token);


        // =====================================================
        // 4. EDUCACIÓN TEOCRÁTICA
        // =====================================================

        ActualizarEstado(
            $"Sincronizando " +
            $"{numeroPersona}/{totalPersonas} · " +
            $"educación",
            numeroPersona - 1,
            totalPersonas,
            EstadoJwHubSync.Sintonizando);


        string urlEducacion =
            raizPersona +
            "/theocratic-education";


        await CargarUrlYEsperarElementoAsync(
            webViewDetalle,
            urlEducacion,
            "app-circuit-person-theocratic-education",
            token);


        string fechaEscuela =
            await ObtenerFechaEscuelaAsync(
                webViewDetalle,
                token);


        if (!string.IsNullOrWhiteSpace(
                fechaEscuela))
        {
            persona.FechaUltimaEscuela =
                ConvertirFechaJWHub(
                    LimpiarFechaCruda(
                        fechaEscuela));
        }
    }


    // =========================================================
    // PREPARAR TARJETA POR ÍNDICE
    // =========================================================

    private static async Task<bool>
        PrepararTarjetaPersonaAsync(
            AndroidWebView webView,
            int indice,
            CancellationToken token)
    {
        string indiceJs =
            indice.ToString(
                CultureInfo.InvariantCulture);


        string javascript =
            """
            (() => {
                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                if (!tarjeta)
                    return '0';

                const card =
                    tarjeta.querySelector(
                        'ptrn-card');

                if (!card)
                    return '0';

                const boton =
                    card.querySelector(
                        'button.card__collapse-button');

                if (!boton)
                    return '0';

                tarjeta.scrollIntoView({
                    block: 'center'
                });

                if (boton.getAttribute(
                        'aria-expanded') !== 'true')
                {
                    boton.click();
                }

                return '1';
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        string pulsado =
            await EvaluarJsAsync(
                webView,
                javascript,
                token);


        if (pulsado != "1")
        {
            return false;
        }


        // Esperar a que la tarjeta esté abierta.
        string scriptAbierta =
            """
            (() => {
                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                const boton =
                    tarjeta?.querySelector(
                        'ptrn-card ' +
                        'button.card__collapse-button');

                return (
                    boton &&
                    boton.getAttribute(
                        'aria-expanded') === 'true'
                )
                    ? '1'
                    : '0';
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        bool abierta =
            await EsperarCondicionAsync(
                webView,
                scriptAbierta,
                TimeSpan.FromSeconds(8),
                token);


        if (!abierta)
        {
            return false;
        }


        // Esperar datos detallados.
        string scriptDatos =
            """
            (() => {
                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                const card =
                    tarjeta?.querySelector(
                        'ptrn-card');

                if (!card)
                    return '0';

                return card.querySelectorAll(
                    'dl div.data__pair'
                ).length > 0
                    ? '1'
                    : '0';
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        bool datos =
            await EsperarCondicionAsync(
                webView,
                scriptDatos,
                TimeSpan.FromSeconds(10),
                token);


        if (!datos)
        {
            return false;
        }


        // Esperar EXCLUSIVAMENTE el botón "Ver más" de esta persona.
        //
        // La tarjeta también puede contener enlaces /persons/.../overview
        // correspondientes al cónyuge. Por eso NO debemos aceptar un
        // enlace solo porque apunte a /persons/ y /overview.
        string scriptPerfil =
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .normalize('NFD')
                        .replace(
                            /[\u0300-\u036f]/g,
                            '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();

                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                const card =
                    tarjeta?.querySelector(
                        'ptrn-card');

                if (!card)
                    return '0';

                const enlacesVerMas =
                    Array.from(
                        card.querySelectorAll(
                            'a.button--action' +
                            '[href*="/persons/"]' +
                            '[href*="/overview"]'));

                const enlace =
                    enlacesVerMas.find(a => {

                        // Protección adicional: un enlace situado dentro
                        // de un <dd> pertenece a un dato de la tarjeta
                        // (por ejemplo, Cónyuge), no al botón de acción.
                        if (a.closest('dd'))
                            return false;

                        const texto =
                            normalizar(
                                a.textContent);

                        return texto.includes(
                            'ver mas');
                    });

                return enlace
                    ? '1'
                    : '0';
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        bool perfil =
            await EsperarCondicionAsync(
                webView,
                scriptPerfil,
                TimeSpan.FromSeconds(10),
                token);


        if (!perfil)
        {
            return false;
        }


        await Task.Delay(
            100,
            token);


        return true;
    }


    // =========================================================
    // CERRAR TARJETA
    // =========================================================

    private static async Task
        CerrarTarjetaPersonaAsync(
            AndroidWebView webView,
            int indice,
            CancellationToken token)
    {
        string indiceJs =
            indice.ToString(
                CultureInfo.InvariantCulture);


        string javascript =
            """
            (() => {
                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                const boton =
                    tarjeta?.querySelector(
                        'ptrn-card ' +
                        'button.card__collapse-button');

                if (!boton)
                    return '0';

                if (boton.getAttribute(
                        'aria-expanded') === 'true')
                {
                    boton.click();
                }

                return '1';
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        await EvaluarJsAsync(
            webView,
            javascript,
            token);
    }


    // =========================================================
    // NOMBRE DE UNA PERSONA POR ÍNDICE
    // =========================================================

    private static Task<string>
        ObtenerNombrePersonaAsync(
            AndroidWebView webView,
            int indice,
            CancellationToken token)
    {
        string indiceJs =
            indice.ToString(
                CultureInfo.InvariantCulture);


        string javascript =
            """
            (() => {
                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');

                const tarjeta =
                    tarjetas[INDICE];

                if (!tarjeta)
                    return '';

                const nombre =
                    tarjeta.querySelector(
                        'ptrn-card ' +
                        '.person-name ' +
                        'span.transliteration__vernacular');

                if (!nombre)
                    return '';

                return (
                    nombre.textContent ||
                    ''
                )
                    .replace(/\u200B/g, '')
                    .trim();
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        return EvaluarJsAsync(
            webView,
            javascript,
            token);
    }


    // =========================================================
    // EXTRAER DATOS DE TARJETA POR ÍNDICE
    // =========================================================

    private static Task<DatosTarjetaJs>
        ObtenerDatosTarjetaPersonaAsync(
            AndroidWebView webView,
            int indice,
            CancellationToken token)
    {
        string indiceJs =
            indice.ToString(
                CultureInfo.InvariantCulture);


        string javascript =
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .normalize('NFD')
                        .replace(
                            /[\u0300-\u036f]/g,
                            '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const tarjetas =
                    document.querySelectorAll(
                        '#cards-list > app-circuit-person-card');


                const tarjeta =
                    tarjetas[INDICE];


                if (!tarjeta)
                    return '';


                // Solo la tarjeta interactiva.
                const card =
                    tarjeta.querySelector(
                        'ptrn-card');


                if (!card)
                    return '';


                // ---------------------------------------------
                // NOMBRE
                // ---------------------------------------------

                const nombreElemento =
                    card.querySelector(
                        '.person-name ' +
                        'span.transliteration__vernacular');


                const nombre =
                    nombreElemento
                        ? (
                            nombreElemento.textContent ||
                            ''
                          )
                            .replace(/\u200B/g, '')
                            .trim()
                        : '';


                // ---------------------------------------------
                // BADGES
                // ---------------------------------------------

                const badges =
                    Array.from(
                        card.querySelectorAll(
                            'ptrn-badges .badge'))
                        .map(x =>
                            (x.textContent || '')
                                .replace(/\s+/g, ' ')
                                .trim())
                        .filter(x => x);


                // ---------------------------------------------
                // PARES
                // ---------------------------------------------

                const pares =
                    Array.from(
                        card.querySelectorAll(
                            'dl div.data__pair'))
                        .map(par => {

                            const dt =
                                par.querySelector(
                                    'dt');

                            const dds =
                                Array.from(
                                    par.querySelectorAll(
                                        'dd'));

                            return {
                                Etiqueta:
                                    dt
                                        ? (
                                            dt.textContent ||
                                            ''
                                          )
                                            .replace(
                                                /\s+/g,
                                                ' ')
                                            .trim()
                                        : '',

                                Valores:
                                    dds
                                        .map(dd =>
                                            (
                                                dd.textContent ||
                                                ''
                                            )
                                                .replace(
                                                    /\u200B/g,
                                                    '')
                                                .replace(
                                                    /\s+/g,
                                                    ' ')
                                                .trim())
                                        .filter(x => x)
                            };
                        });


                // ---------------------------------------------
                // URL DEL PERFIL
                // ---------------------------------------------
                //
                // IMPORTANTE:
                // dentro de la tarjeta puede existir también un enlace
                // /persons/.../overview al CÓNYUGE.
                //
                // Solo aceptamos el enlace que sea el botón de acción
                // "Ver más". No hay fallback genérico a cualquier enlace
                // de persona, porque eso volvería a introducir el error.

                const enlacesVerMas =
                    Array.from(
                        card.querySelectorAll(
                            'a.button--action' +
                            '[href*="/persons/"]' +
                            '[href*="/overview"]'));


                const enlacePerfil =
                    enlacesVerMas.find(a => {

                        if (a.closest('dd'))
                            return false;

                        const texto =
                            normalizar(
                                a.textContent);

                        return texto.includes(
                            'ver mas');
                    });


                return JSON.stringify({
                    NombreCompleto:
                        nombre,

                    Badges:
                        badges,

                    Pares:
                        pares,

                    UrlPerfil:
                        enlacePerfil
                            ? (
                                enlacePerfil.href ||
                                enlacePerfil.getAttribute(
                                    'href') ||
                                ''
                              )
                            : ''
                });
            })();
            """
            .Replace(
                "INDICE",
                indiceJs);


        return EvaluarJsonAsync<DatosTarjetaJs>(
            webView,
            javascript,
            token);
    }


    // =========================================================
    // APLICAR DATOS DE TARJETA
    // =========================================================

    private static void AplicarDatosTarjeta(
        PersonaExtraidaJwHubAndroid persona,
        DatosTarjetaJs datos)
    {
        persona.NombreCompleto =
            datos.NombreCompleto?.Trim()
            ?? "";


        // =====================================================
        // NOMBRE COMÚN / APELLIDOS
        // =====================================================

        string[] partes =
            persona.NombreCompleto
                .Split(',');


        if (partes.Length >= 2)
        {
            string apellidos =
                partes[0].Trim();


            string nombrePila =
                string.Join(
                    ",",
                    partes.Skip(1))
                .Trim();


            persona.NombreComun =
                $"{nombrePila} {apellidos}"
                    .Trim();


            persona.Apellidos =
                apellidos;
        }
        else
        {
            persona.NombreComun =
                persona.NombreCompleto.Trim();


            persona.Apellidos =
                "";
        }


        // =====================================================
        // BADGES
        // =====================================================

        foreach (string badge in
                 datos.Badges)
        {
            string texto =
                badge.Trim();


            if (texto.Contains(
                    "Misionero",
                    StringComparison.OrdinalIgnoreCase))
            {
                persona.Servicio =
                    "Misionero";
            }
            else if (texto.Contains(
                         "Precursor especial",
                         StringComparison.OrdinalIgnoreCase))
            {
                persona.Servicio =
                    "Precursor especial";
            }
            else if (texto.Contains(
                         "Precursor",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (persona.Servicio !=
                    "Precursor especial")
                {
                    persona.Servicio =
                        "Precursor regular";
                }
            }
            else if (texto.Contains(
                         "Anciano",
                         StringComparison.OrdinalIgnoreCase))
            {
                persona.Nombramiento =
                    "Anciano";
            }
            else if (texto.Contains(
                         "Siervo",
                         StringComparison.OrdinalIgnoreCase))
            {
                persona.Nombramiento =
                    "Siervo ministerial";
            }
        }


        // =====================================================
        // DATOS DETALLADOS
        // =====================================================

        foreach (ParDatoJs par in
                 datos.Pares)
        {
            string etiqueta =
                NormalizarTexto(
                    par.Etiqueta);


            List<string> valores =
                par.Valores
                    .Where(v =>
                        !string.IsNullOrWhiteSpace(v))
                    .Select(v =>
                        v.Trim())
                    .ToList();


            // ---------------------------------------------
            // PRIVILEGIOS
            // ---------------------------------------------

            if (etiqueta.Contains(
                    "privilegio"))
            {
                var encontrados =
                    new List<string>();


                foreach (string valor in valores)
                {
                    foreach (
                        string permitido
                        in PrivilegiosPermitidos)
                    {
                        if (valor.Contains(
                                permitido,
                                StringComparison
                                    .OrdinalIgnoreCase))
                        {
                            if (!encontrados.Contains(
                                    valor,
                                    StringComparer
                                        .OrdinalIgnoreCase))
                            {
                                encontrados.Add(
                                    valor);
                            }

                            break;
                        }
                    }
                }


                if (encontrados.Count > 0)
                {
                    persona.Privilegios =
                        string.Join(
                            "\n",
                            encontrados);
                }
            }


            // ---------------------------------------------
            // CONGREGACIÓN
            // ---------------------------------------------

            else if (etiqueta.Contains(
                         "congregacion"))
            {
                if (valores.Count > 0)
                {
                    persona.Congregacion =
                        string.Join(
                            ", ",
                            valores);
                }
            }


            // ---------------------------------------------
            // NACIMIENTO
            // ---------------------------------------------

            else if (
                etiqueta.Contains(
                    "nacimiento")
                ||
                etiqueta == "edad"
                ||
                etiqueta.StartsWith(
                    "edad "))
            {
                if (valores.Count > 0)
                {
                    persona.FechaNacimiento =
                        ProcesarFechaOEdad(
                            valores[0]);
                }
            }


            // ---------------------------------------------
            // BAUTISMO
            // ---------------------------------------------

            else if (
                etiqueta.Contains(
                    "bautismo")
                ||
                etiqueta.Contains(
                    "bautizado"))
            {
                if (valores.Count > 0)
                {
                    persona.FechaBautismo =
                        ProcesarFechaOEdad(
                            valores[0]);
                }
            }


            // ---------------------------------------------
            // TELÉFONO
            // ---------------------------------------------

            else if (
                etiqueta.Contains(
                    "telefono")
                ||
                etiqueta.Contains(
                    "celular")
                ||
                etiqueta.Contains(
                    "movil"))
            {
                foreach (string valor in valores)
                {
                    string telefono =
                        valor
                            .Replace(
                                " ",
                                "")
                            .Replace(
                                "+34",
                                "")
                            .Replace(
                                "-",
                                "")
                            .Trim();


                    if (
                        (
                            telefono.StartsWith("6")
                            ||
                            telefono.StartsWith("7")
                        )
                        &&
                        string.IsNullOrEmpty(
                            persona.Telefono)
                    )
                    {
                        persona.Telefono =
                            telefono;

                        break;
                    }
                }
            }
        }


        persona.UrlPerfil =
            datos.UrlPerfil?.Trim()
            ?? "";
    }


    // =========================================================
    // PERFIL / RESUMEN
    // =========================================================

    private static Task<DatosPerfilJs>
        ObtenerDatosPerfilAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsonAsync<DatosPerfilJs>(
            webView,
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .normalize('NFD')
                        .replace(
                            /[\u0300-\u036f]/g,
                            '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const pagina =
                    document.querySelector(
                        'app-circuit-person-overview');


                if (!pagina)
                    return '';


                // ---------------------------------------------
                // NOMBRE DEL PERFIL
                // ---------------------------------------------

                const nombrePerfilElemento =
                    document.querySelector(
                        'app-circuit-person-side-nav ' +
                        'h2 ' +
                        'span.transliteration__vernacular');


                const nombrePerfil =
                    nombrePerfilElemento
                        ? (
                            nombrePerfilElemento.textContent ||
                            ''
                          )
                            .replace(/\u200B/g, '')
                            .trim()
                        : '';


                // ---------------------------------------------
                // FOTO
                // ---------------------------------------------

                const personaOverview =
                    pagina.querySelector(
                        'app-person-overview');


                const foto =
                    personaOverview
                        ? personaOverview.querySelector(
                            "img[alt='Fotografía principal']")
                        : null;


                const urlFoto =
                    foto
                        ? (
                            foto.currentSrc ||
                            foto.src ||
                            foto.getAttribute(
                                'src') ||
                            ''
                          )
                        : '';


                // ---------------------------------------------
                // CONTACTO
                // ---------------------------------------------

                const contacto =
                    pagina.querySelector(
                        'app-contact-overview');


                let email =
                    '';

                let emailJW =
                    '';

                let direccion =
                    '';


                if (contacto) {

                    // -----------------------------------------
                    // EMAILS
                    // -----------------------------------------

                    const emails =
                        Array.from(
                            contacto.querySelectorAll(
                                'bdi.email'));


                    for (const elemento of emails) {

                        const texto =
                            (
                                elemento.textContent ||
                                ''
                            ).trim();


                        if (!texto.includes('@'))
                            continue;


                        if (
                            texto
                                .toLowerCase()
                                .endsWith(
                                    '@jwpub.org')
                        ) {
                            emailJW =
                                texto;
                        }
                        else if (!email) {
                            email =
                                texto;
                        }
                    }


                    // -----------------------------------------
                    // DIRECCIÓN
                    // -----------------------------------------

                    const pares =
                        Array.from(
                            contacto.querySelectorAll(
                                'dl > div, ' +
                                'dl .grid'));


                    for (const par of pares) {

                        const dt =
                            par.querySelector(
                                'dt');

                        const dd =
                            par.querySelector(
                                'dd');


                        if (!dt || !dd)
                            continue;


                        const etiqueta =
                            normalizar(
                                dt.textContent);


                        if (
                            !etiqueta.includes(
                                'direccion fisica')
                            &&
                            !etiqueta.includes(
                                'direccion postal')
                        )
                        {
                            continue;
                        }


                        const address =
                            dd.querySelector(
                                'app-address');


                        const origen =
                            address ||
                            dd;


                        direccion =
                            (
                                origen.textContent ||
                                ''
                            )
                                .split(/\r?\n/)
                                .map(x =>
                                    x.trim())
                                .filter(x => x)
                                .join(', ')
                                .replace(
                                    /\s+/g,
                                    ' ')
                                .trim();


                        if (direccion)
                            break;
                    }


                    // Fallback directo a app-address.
                    if (!direccion) {

                        const address =
                            contacto.querySelector(
                                'app-address');


                        if (address) {

                            direccion =
                                (
                                    address.textContent ||
                                    ''
                                )
                                    .split(/\r?\n/)
                                    .map(x =>
                                        x.trim())
                                    .filter(x => x)
                                    .join(', ')
                                    .replace(
                                        /\s+/g,
                                        ' ')
                                    .trim();
                        }
                    }
                }


                return JSON.stringify({
                    NombrePerfil:
                        nombrePerfil,

                    Email:
                        email,

                    EmailJW:
                        emailJW,

                    Direccion:
                        direccion,

                    UrlFoto:
                        urlFoto
                });
            })();
            """,
            token);
    }

    // =============================================================
    // OBTENER ID ÚNICO DE LA PERSONA DESDE SU URL
    //
    // Ejemplo:
    //
    // .../persons/d8145a5f-8342-46e6-a34d-772a936451be/overview
    //
    // devuelve:
    //
    // d8145a5f-8342-46e6-a34d-772a936451be
    // =============================================================

    private static string ObtenerIdPersonaDesdeUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }


        try
        {
            Uri uri =
                new(
                    AbsolutizarUrl(
                        url));


            Match match =
                Regex.Match(
                    uri.AbsolutePath,
                    @"/persons/([^/]+)",
                    RegexOptions.IgnoreCase);


            if (match.Success)
            {
                return match
                    .Groups[1]
                    .Value
                    .Trim();
            }
        }
        catch
        {
        }


        return "";
    }


    // =============================================================
    // ESPERAR A QUE EL CONTENIDO DEL PERFIL SEA REALMENTE
    // EL DE LA PERSONA QUE ACABAMOS DE ABRIR.
    //
    // NO dependemos del nombre.
    // NO dependemos de que tenga email.
    // NO dependemos de que tenga foto.
    // NO dependemos de que tenga dirección.
    //
    // Comprobamos el ID único de JW Hub.
    // =============================================================

    private static async Task<bool>
        EsperarPerfilPersonaAsync(
            AndroidWebView webView,
            string urlPerfil,
            CancellationToken token)
    {
        string idPersona =
            ObtenerIdPersonaDesdeUrl(
                urlPerfil);


        if (string.IsNullOrWhiteSpace(
                idPersona))
        {
            return false;
        }


        string idJson =
            JsonSerializer.Serialize(
                idPersona.ToLowerInvariant());


        string javascript =
            "(() => {" +
            "const id = " +
            idJson +
            ";" +

            "const ruta = " +
            "(location.pathname || '').toLowerCase();" +

            "if (!ruta.includes('/persons/' + id + '/overview')) " +
            "return '0';" +

            "const pagina = " +
            "document.querySelector(" +
            "'app-circuit-person-overview');" +

            "if (!pagina) return '0';" +

            // El bloque principal de la persona debe existir.
            "const persona = " +
            "pagina.querySelector('app-person-overview');" +

            "if (!persona) return '0';" +

            // Dentro del propio resumen buscamos cualquier enlace
            // que pertenezca a ESA persona.
            "const enlaces = " +
            "Array.from(pagina.querySelectorAll('a[href]'));" +

            "const correcto = enlaces.some(a => {" +
            "const href = " +
            "(a.getAttribute('href') || '').toLowerCase();" +
            "return href.includes('/persons/' + id + '/');" +
            "});" +

            "return correcto ? '1' : '0';" +
            "})();";


        return await EsperarCondicionAsync(
            webView,
            javascript,
            TimeSpan.FromSeconds(15),
            token);
    }


    // =========================================================
    // ESTADO
    // =========================================================

    private static Task<DatosEstadoJs>
        ObtenerDatosEstadoAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsonAsync<DatosEstadoJs>(
            webView,
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .normalize('NFD')
                        .replace(
                            /[\u0300-\u036f]/g,
                            '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const pagina =
                    document.querySelector(
                        'app-spiritual-status-view-container');


                if (!pagina)
                    return '';


                let esperanza =
                    '';

                let fechaInicioPrec =
                    '';


                const pares =
                    Array.from(
                        pagina.querySelectorAll(
                            '.data__pair'));


                for (const par of pares) {

                    const dt =
                        par.querySelector(
                            'dt');

                    const dd =
                        par.querySelector(
                            'dd');


                    if (!dt || !dd)
                        continue;


                    const etiqueta =
                        normalizar(
                            dt.textContent);


                    const valor =
                        (
                            dd.textContent ||
                            ''
                        )
                            .replace(/\u200B/g, '')
                            .replace(/\s+/g, ' ')
                            .trim();


                    if (
                        !esperanza
                        &&
                        etiqueta.includes(
                            'esperanza')
                    )
                    {
                        esperanza =
                            valor;
                    }


                    if (
                        !fechaInicioPrec
                        &&
                        (
                            etiqueta.includes(
                                'inicio del precursorado')
                            ||
                            etiqueta.includes(
                                'tiempo completo')
                        )
                    )
                    {
                        fechaInicioPrec =
                            valor;
                    }
                }


                return JSON.stringify({
                    Esperanza:
                        esperanza,

                    FechaInicioPrec:
                        fechaInicioPrec
                });
            })();
            """,
            token);
    }


    // =========================================================
    // SOLICITUDES VIGENTES
    // =========================================================

    private static Task<string>
        ObtenerSolicitudesAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsAsync(
            webView,
            """
            (() => {
                const pagina =
                    document.querySelector(
                        'app-applications-container');


                if (!pagina)
                    return '';


                const texto =
                    pagina.innerText ||
                    '';


                const encontrados =
                    texto.match(
                        /\b(A-2|A-8|G-8|A-19)\b/g)
                    || [];


                return Array.from(
                    new Set(
                        encontrados)
                ).join(', ');
            })();
            """,
            token);
    }


    // =========================================================
    // EDUCACIÓN TEOCRÁTICA
    // =========================================================

    private static Task<string>
        ObtenerFechaEscuelaAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsAsync(
            webView,
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .normalize('NFD')
                        .replace(
                            /[\u0300-\u036f]/g,
                            '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const pagina =
                    document.querySelector(
                        'app-circuit-person-theocratic-education');


                if (!pagina)
                    return '';


                const cards =
                    Array.from(
                        pagina.querySelectorAll(
                            'article.card'));


                for (const card of cards) {

                    const h2 =
                        card.querySelector(
                            'h2');


                    if (!h2)
                        continue;


                    const titulo =
                        normalizar(
                            h2.textContent);


                    if (
                        !titulo.includes(
                            'pioneer service school')
                        &&
                        !titulo.includes(
                            'escuela del servicio de precursor')
                    )
                    {
                        continue;
                    }


                    const pares =
                        Array.from(
                            card.querySelectorAll(
                                '.data__pair'));


                    for (const par of pares) {

                        const dt =
                            par.querySelector(
                                'dt');

                        const dd =
                            par.querySelector(
                                'dd');


                        if (!dt || !dd)
                            continue;


                        const etiqueta =
                            normalizar(
                                dt.textContent);


                        if (!etiqueta.includes(
                                'fecha de graduacion'))
                        {
                            continue;
                        }


                        return (
                            dd.textContent ||
                            ''
                        )
                            .replace(/\u200B/g, '')
                            .replace(/\s+/g, ' ')
                            .trim();
                    }
                }


                return '';
            })();
            """,
            token);
    }


    // =========================================================
    // FOTO
    // =========================================================

    private static async Task<string>
        DescargarFotoBase64Async(
            string url,
            CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(
                url))
        {
            return "";
        }


        // Caso de una imagen ya codificada.
        if (url.StartsWith(
                "data:image/",
                StringComparison.OrdinalIgnoreCase))
        {
            int coma =
                url.IndexOf(',');


            if (coma >= 0 &&
                coma < url.Length - 1)
            {
                return url.Substring(
                    coma + 1);
            }
        }


        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);


            request.Headers.Referrer =
                new Uri(
                    UrlBase);


            using HttpResponseMessage response =
                await ClienteHttp.SendAsync(
                    request,
                    HttpCompletionOption
                        .ResponseHeadersRead,
                    token);


            if (!response.IsSuccessStatusCode)
            {
                return "";
            }


            byte[] bytes =
                await response.Content
                    .ReadAsByteArrayAsync(
                        token);


            if (bytes.Length == 0)
            {
                return "";
            }


            return Convert.ToBase64String(
                bytes);
        }
        catch (
            System.OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // La ausencia de foto no hace fallar
            // la extracción de la persona.
            return "";
        }
    }


    private static HttpClient
        CrearClienteHttp()
    {
        var cliente =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(20)
            };


        cliente
            .DefaultRequestHeaders
            .UserAgent
            .ParseAdd(
                "Mozilla/5.0 " +
                "(Linux; Android 14) " +
                "AppleWebKit/537.36 " +
                "Chrome/130.0 Mobile Safari/537.36");


        return cliente;
    }


    // =========================================================
    // RAÍZ DE LA PERSONA
    // =========================================================

    private static string ObtenerRaizPersona(
        string urlPerfil)
    {
        Uri uri =
            new(
                AbsolutizarUrl(
                    urlPerfil));


        string ruta =
            uri.AbsolutePath
                .TrimEnd('/');


        string[] finales =
        [
            "/overview",
            "/personal-information",
            "/spiritual-status",
            "/applications",
            "/theocratic-education"
        ];


        foreach (string final in finales)
        {
            if (ruta.EndsWith(
                    final,
                    StringComparison.OrdinalIgnoreCase))
            {
                ruta =
                    ruta.Substring(
                        0,
                        ruta.Length -
                        final.Length);

                break;
            }
        }


        return
            uri.GetLeftPart(
                UriPartial.Authority)
            +
            ruta;
    }


    // =========================================================
    // CARGAR TODO EL LISTADO
    //
    // YA PROBADO:
    //
    // no depende de 769
    // no depende de lotes de 75
    // =========================================================

    private static async Task
        CargarTodosLosResultadosAsync(
            AndroidWebView webView,
            int totalEsperado,
            CancellationToken token)
    {
        int actual =
            await ContarTarjetasPersonaAsync(
                webView,
                token);


        int numeroClics =
            0;


        const int MaximoClics =
            200;


        while (actual <
               totalEsperado)
        {
            token.ThrowIfCancellationRequested();


            if (numeroClics >=
                MaximoClics)
            {
                throw new Exception(
                    "Se alcanzó el límite de " +
                    "seguridad al cargar personas.");
            }


            int antes =
                actual;


            string textoBoton =
                await PulsarMostrarMasAsync(
                    webView,
                    token);


            if (string.IsNullOrWhiteSpace(
                    textoBoton))
            {
                bool reaparecio =
                    await EsperarMostrarMasAsync(
                        webView,
                        totalEsperado,
                        token);


                actual =
                    await ContarTarjetasPersonaAsync(
                        webView,
                        token);


                if (actual >=
                    totalEsperado)
                {
                    break;
                }


                if (!reaparecio)
                {
                    throw new Exception(
                        "Desapareció el botón " +
                        "«Mostrar más» antes de " +
                        $"terminar: {actual}/" +
                        $"{totalEsperado}.");
                }


                continue;
            }


            numeroClics++;


            bool aumento =
                await EsperarAumentoTarjetasAsync(
                    webView,
                    antes,
                    totalEsperado,
                    token);


            actual =
                await ContarTarjetasPersonaAsync(
                    webView,
                    token);


            if (!aumento &&
                actual <= antes)
            {
                await Task.Delay(
                    300,
                    token);


                actual =
                    await ContarTarjetasPersonaAsync(
                        webView,
                        token);


                if (actual <= antes)
                {
                    throw new Exception(
                        $"Se pulsó «{textoBoton}», " +
                        "pero la cantidad de " +
                        "publicadores no aumentó.");
                }
            }


            ActualizarEstado(
                $"Cargando publicadores... " +
                $"{actual}/{totalEsperado}",
                actual,
                totalEsperado);


            await Task.Delay(
                120,
                token);
        }
    }


    // =========================================================
    // PULSAR "MOSTRAR N MÁS"
    // =========================================================

    private static Task<string>
        PulsarMostrarMasAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsAsync(
            webView,
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const botones =
                    Array.from(
                        document.querySelectorAll(
                            'button.margin-top-05'));


                for (const boton of botones) {

                    const texto =
                        normalizar(
                            boton.textContent);


                    if (!texto.includes(
                            'mostrar'))
                        continue;


                    if (!texto.includes(
                            'más'))
                        continue;


                    if (texto.includes(
                            'mostrar todo'))
                        continue;


                    const estilo =
                        window.getComputedStyle(
                            boton);


                    if (
                        estilo.display === 'none'
                        ||
                        estilo.visibility === 'hidden'
                    )
                    {
                        continue;
                    }


                    if (
                        boton.disabled
                        ||
                        boton.getAttribute(
                            'aria-disabled') === 'true'
                    )
                    {
                        continue;
                    }


                    const original =
                        (
                            boton.textContent ||
                            ''
                        )
                            .replace(/\s+/g, ' ')
                            .trim();


                    boton.scrollIntoView({
                        block: 'center'
                    });


                    boton.click();


                    return original;
                }


                return '';
            })();
            """,
            token);
    }


    // =========================================================
    // ESPERAR MOSTRAR MÁS
    // =========================================================

    private static async Task<bool>
        EsperarMostrarMasAsync(
            AndroidWebView webView,
            int totalEsperado,
            CancellationToken token)
    {
        for (int intento = 0;
             intento < 20;
             intento++)
        {
            token.ThrowIfCancellationRequested();


            int actual =
                await ContarTarjetasPersonaAsync(
                    webView,
                    token);


            if (actual >=
                totalEsperado)
            {
                return false;
            }


            string existe =
                await EvaluarJsAsync(
                    webView,
                    """
                    (() => {
                        const normalizar = t =>
                            (t || '')
                                .replace(/\s+/g, ' ')
                                .trim()
                                .toLowerCase();


                        const botones =
                            Array.from(
                                document.querySelectorAll(
                                    'button.margin-top-05'));


                        for (const boton of botones) {

                            const texto =
                                normalizar(
                                    boton.textContent);


                            if (!texto.includes(
                                    'mostrar'))
                                continue;


                            if (!texto.includes(
                                    'más'))
                                continue;


                            if (texto.includes(
                                    'mostrar todo'))
                                continue;


                            return '1';
                        }


                        return '0';
                    })();
                    """,
                    token);


            if (existe == "1")
            {
                return true;
            }


            await Task.Delay(
                150,
                token);
        }


        return false;
    }


    // =========================================================
    // ESPERAR AUMENTO DE TARJETAS
    // =========================================================

    private static async Task<bool>
        EsperarAumentoTarjetasAsync(
            AndroidWebView webView,
            int cantidadAnterior,
            int totalEsperado,
            CancellationToken token)
    {
        DateTime limite =
            DateTime.UtcNow
                .AddSeconds(5);


        while (DateTime.UtcNow <
               limite)
        {
            token.ThrowIfCancellationRequested();


            await Task.Delay(
                100,
                token);


            int actual =
                await ContarTarjetasPersonaAsync(
                    webView,
                    token);


            if (actual >
                cantidadAnterior)
            {
                ActualizarEstado(
                    $"Cargando publicadores... " +
                    $"{actual}/{totalEsperado}",
                    actual,
                    totalEsperado);


                return true;
            }
        }


        return false;
    }


    // =========================================================
    // CONTAR TARJETAS
    // =========================================================

    private static async Task<int>
        ContarTarjetasPersonaAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        string resultado =
            await EvaluarJsAsync(
                webView,
                """
                (() => {
                    return document
                        .querySelectorAll(
                            '#cards-list > ' +
                            'app-circuit-person-card')
                        .length
                        .toString();
                })();
                """,
                token);


        return int.TryParse(
            resultado,
            out int total)
                ? total
                : 0;
    }


    // =========================================================
    // TOTAL REAL DE JW HUB
    // =========================================================

    private static async Task<int>
        ObtenerTotalResultadosAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        string resultado =
            await EvaluarJsAsync(
                webView,
                """
                (() => {
                    const soloDigitos = valor =>
                        (valor || '')
                            .replace(/[^\d]/g, '');


                    const texto =
                        document.body
                            ? document.body.innerText || ''
                            : '';


                    const de =
                        texto.match(
                            /Resultados:\s*([\d.,]+)\s+de\s+([\d.,]+)/i);


                    if (de) {

                        const total =
                            soloDigitos(
                                de[2]);

                        if (total)
                            return total;
                    }


                    const cabecera =
                        document.querySelector(
                            'app-person-cards-header');


                    if (cabecera) {

                        const textoCabecera =
                            (
                                cabecera.textContent ||
                                ''
                            )
                                .replace(/\s+/g, ' ')
                                .trim();


                        const simple =
                            textoCabecera.match(
                                /Resultados:\s*([\d.,]+)/i);


                        if (simple) {

                            const total =
                                soloDigitos(
                                    simple[1]);

                            if (total)
                                return total;
                        }
                    }


                    return '0';
                })();
                """,
                token);


        return int.TryParse(
            resultado,
            out int total)
                ? total
                : 0;
    }


    // =========================================================
    // PULSAR BUSCAR
    // =========================================================

    private static Task<string>
        PulsarBuscarAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsAsync(
            webView,
            """
            (() => {
                const normalizar = t =>
                    (t || '')
                        .replace(/\s+/g, ' ')
                        .trim()
                        .toLowerCase();


                const formulario =
                    document.querySelector(
                        'app-circuit-person-filter-bar');


                if (!formulario)
                    return '0';


                const botones =
                    Array.from(
                        formulario.querySelectorAll(
                            'button'));


                const boton =
                    botones.find(b =>
                        normalizar(
                            b.textContent)
                            .includes(
                                'buscar'));


                if (!boton)
                    return '0';


                boton.scrollIntoView({
                    block: 'center'
                });


                boton.click();


                return '1';
            })();
            """,
            token);
    }


    // =========================================================
    // COMPROBAR ACCESO
    // =========================================================

    private static Task<bool>
        EsperarAccesoPersonasAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EsperarCondicionAsync(
            webView,
            """
            (() => {
                const url =
                    location.href || '';


                if (!url.includes(
                        '/records'))
                {
                    return '0';
                }


                const pagina =
                    document.querySelector(
                        'app-circuit-persons');


                const buscador =
                    document.querySelector(
                        'app-circuit-person-filter-bar ' +
                        'input[type="search"]');


                return (
                    pagina &&
                    buscador
                )
                    ? '1'
                    : '0';
            })();
            """,
            TimeSpan.FromSeconds(30),
            token);
    }


    // =========================================================
    // CARGAR URL Y ESPERAR LA PÁGINA CORRECTA
    //
    // No usamos solo readyState.
    //
    // Se comprueba:
    //
    // - pathname exacto
    // - readyState complete
    // - componente Angular concreto
    // =========================================================

    private static async Task
        CargarUrlYEsperarElementoAsync(
            AndroidWebView webView,
            string url,
            string selectorEsperado,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();


        string urlAbsoluta =
            AbsolutizarUrl(
                url);


        Uri uriEsperada =
            new(
                urlAbsoluta);


        string rutaEsperada =
            uriEsperada.AbsolutePath
                .TrimEnd('/');


        string rutaJson =
            JsonSerializer.Serialize(
                rutaEsperada);


        string selectorJson =
            JsonSerializer.Serialize(
                selectorEsperado);


        await MauiMainThread
            .InvokeOnMainThreadAsync(
                () =>
                {
                    webView.StopLoading();

                    webView.LoadUrl(
                        urlAbsoluta);
                });


        // Damos tiempo a que la navegación
        // realmente comience antes de consultar
        // el DOM anterior.
        await Task.Delay(
            100,
            token);


        string javascript =
            "(() => {" +
            "const rutaEsperada = " +
            rutaJson +
            ";" +
            "const selectorEsperado = " +
            selectorJson +
            ";" +
            "const rutaActual = " +
            "(location.pathname || '')" +
            ".replace(/\\/+$/, '');" +
            "return (" +
            "rutaActual === rutaEsperada " +
            "&& document.readyState === 'complete' " +
            "&& document.querySelector(selectorEsperado)" +
            ") ? '1' : '0';" +
            "})();";


        bool correcta =
            await EsperarCondicionAsync(
                webView,
                javascript,
                TimeSpan.FromSeconds(30),
                token);


        if (!correcta)
        {
            string actual =
                "";


            try
            {
                actual =
                    await ObtenerUrlActualAsync(
                        webView,
                        token);
            }
            catch
            {
            }


            // Si ya no estamos en la aplicación
            // congregation-persons, probablemente
            // se perdió la sesión.
            if (
                !string.IsNullOrWhiteSpace(actual)
                &&
                !actual.Contains(
                    "/congregation-persons/",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                JwHubSessionState
                    .MarcarSesionNoValida();


                throw new
                    JwHubSessionRequiredException(
                        "La sesión de JW Hub " +
                        "ha caducado durante la " +
                        "sincronización.");
            }


            throw new TimeoutException(
                "JW Hub no terminó de cargar " +
                $"«{selectorEsperado}». " +
                $"URL actual: {actual}");
        }


        // Pequeño margen para terminar de pintar
        // los datos del componente.
        await Task.Delay(
            200,
            token);
    }


    // =========================================================
    // URL ACTUAL
    // =========================================================

    private static Task<string>
        ObtenerUrlActualAsync(
            AndroidWebView webView,
            CancellationToken token)
    {
        return EvaluarJsAsync(
            webView,
            """
            (() => {
                return location.href || '';
            })();
            """,
            token);
    }


    // =========================================================
    // EVALUAR JSON
    // =========================================================

    private static async Task<T>
        EvaluarJsonAsync<T>(
            AndroidWebView webView,
            string javascript,
            CancellationToken token)
    {
        string json =
            await EvaluarJsAsync(
                webView,
                javascript,
                token);


        if (string.IsNullOrWhiteSpace(
                json))
        {
            throw new Exception(
                "JW Hub devolvió una respuesta " +
                "vacía al leer los datos.");
        }


        T? resultado =
            JsonSerializer.Deserialize<T>(
                json,
                OpcionesJson);


        if (resultado is null)
        {
            throw new Exception(
                "No se pudieron interpretar " +
                "los datos obtenidos de JW Hub.");
        }


        return resultado;
    }


    // =========================================================
    // ESPERAR CONDICIÓN
    // =========================================================

    private static async Task<bool>
        EsperarCondicionAsync(
            AndroidWebView webView,
            string javascript,
            TimeSpan timeout,
            CancellationToken token)
    {
        DateTime limite =
            DateTime.UtcNow +
            timeout;


        while (DateTime.UtcNow <
               limite)
        {
            token.ThrowIfCancellationRequested();


            try
            {
                string resultado =
                    await EvaluarJsAsync(
                        webView,
                        javascript,
                        token);


                if (resultado == "1")
                {
                    return true;
                }
            }
            catch (
                System.OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Angular/WebView puede no responder
                // durante un instante mientras navega.
            }


            await Task.Delay(
                150,
                token);
        }


        return false;
    }


    // =========================================================
    // EVALUAR JAVASCRIPT
    // =========================================================

    private static async Task<string>
        EvaluarJsAsync(
            AndroidWebView webView,
            string javascript,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();


        var tcs =
            new TaskCompletionSource<string>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);


        using CancellationTokenRegistration
            registro =
                token.Register(
                    () =>
                    {
                        tcs.TrySetCanceled(
                            token);
                    });


        MauiMainThread
            .BeginInvokeOnMainThread(
                () =>
                {
                    try
                    {
                        webView.EvaluateJavascript(
                            javascript,
                            new ResultadoJsCallback(
                                valor =>
                                {
                                    tcs.TrySetResult(
                                        valor ?? "");
                                }));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(
                            ex);
                    }
                });


        Task timeout =
            Task.Delay(
                TimeSpan.FromSeconds(15));


        Task terminada =
            await Task.WhenAny(
                tcs.Task,
                timeout);


        token.ThrowIfCancellationRequested();


        if (terminada !=
            tcs.Task)
        {
            throw new TimeoutException(
                "El navegador interno " +
                "no respondió.");
        }


        string bruto =
            await tcs.Task;


        return LimpiarResultadoJs(
            bruto);
    }


    // =========================================================
    // LIMPIAR RESULTADO JAVASCRIPT
    // =========================================================

    private static string
        LimpiarResultadoJs(
            string? resultado)
    {
        if (string.IsNullOrWhiteSpace(
                resultado))
        {
            return "";
        }


        string valor =
            resultado.Trim();


        if (valor.Equals(
                "null",
                StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }


        try
        {
            string? texto =
                JsonSerializer
                    .Deserialize<string>(
                        valor);


            if (texto is not null)
            {
                return texto;
            }
        }
        catch
        {
        }


        return valor;
    }


    // =========================================================
    // PROCESAR FECHA O EDAD
    // =========================================================

    private static string
        ProcesarFechaOEdad(
            string valor)
    {
        if (string.IsNullOrWhiteSpace(
                valor))
        {
            return "";
        }


        string limpio =
            LimpiarFechaCruda(
                valor);


        Match matchYear =
            Regex.Match(
                limpio,
                @"\b(19|20)\d{2}\b");


        // Si JW Hub da fecha completa o al
        // menos mes/año.
        if (matchYear.Success)
        {
            return ConvertirFechaJWHub(
                limpio);
        }


        // Si únicamente hay edad,
        // mantenemos la aproximación 01/01/año
        // que utiliza el PC.
        string primeraParte =
            limpio
                .Split(
                    [',', '.'],
                    StringSplitOptions.None)[0]
                .Trim();


        string digitos =
            new(
                primeraParte
                    .Where(
                        char.IsDigit)
                    .ToArray());


        if (int.TryParse(
                digitos,
                out int edad))
        {
            return
                $"01/01/" +
                $"{DateTime.Now.Year - edad}";
        }


        return "";
    }


    // =========================================================
    // LIMPIAR FECHA
    // =========================================================

    private static string
        LimpiarFechaCruda(
            string valor)
    {
        string resultado =
            valor
                .Replace(
                    "\u200B",
                    "")
                .Trim();


        int parentesis =
            resultado.IndexOf('(');


        if (parentesis >= 0)
        {
            resultado =
                resultado
                    .Substring(
                        0,
                        parentesis)
                    .Trim();
        }


        return resultado;
    }


    // =========================================================
    // CONVERTIR FECHA DE JW HUB
    // =========================================================

    private static string
        ConvertirFechaJWHub(
            string textoFecha)
    {
        if (string.IsNullOrWhiteSpace(
                textoFecha))
        {
            return "";
        }


        string txt =
            textoFecha
                .ToLowerInvariant()
                .Replace(
                    " de ",
                    " ")
                .Replace(
                    ".",
                    "");


        string[] meses =
        [
            "ene",
            "feb",
            "mar",
            "abr",
            "may",
            "jun",
            "jul",
            "ago",
            "sep",
            "oct",
            "nov",
            "dic"
        ];


        string[] completos =
        [
            "enero",
            "febrero",
            "marzo",
            "abril",
            "mayo",
            "junio",
            "julio",
            "agosto",
            "septiembre",
            "octubre",
            "noviembre",
            "diciembre"
        ];


        Match matchYear =
            Regex.Match(
                txt,
                @"\b(19|20)\d{2}\b");


        if (!matchYear.Success)
        {
            return textoFecha;
        }


        int anio =
            int.Parse(
                matchYear.Value);


        int mes =
            1;


        for (int i = 0;
             i < 12;
             i++)
        {
            if (
                txt.Contains(
                    meses[i])
                ||
                txt.Contains(
                    completos[i])
            )
            {
                mes =
                    i + 1;

                break;
            }
        }


        int dia =
            1;


        string sinAnio =
            txt.Replace(
                anio.ToString(),
                "");


        Match matchDay =
            Regex.Match(
                sinAnio,
                @"\b([1-9]|[12][0-9]|3[01])\b");


        if (matchDay.Success)
        {
            dia =
                int.Parse(
                    matchDay.Value);
        }


        return
            $"{dia:D2}/{mes:D2}/{anio}";
    }


    // =========================================================
    // NORMALIZAR TEXTO
    // =========================================================

    private static string
        NormalizarTexto(
            string? texto)
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return "";
        }


        string normalizado =
            texto.Normalize(
                NormalizationForm.FormD);


        var sb =
            new StringBuilder();


        foreach (char c in
                 normalizado)
        {
            UnicodeCategory categoria =
                CharUnicodeInfo
                    .GetUnicodeCategory(c);


            if (categoria !=
                UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }


        return sb
            .ToString()
            .Replace(
                "\u200B",
                "")
            .ToLowerInvariant()
            .Trim();
    }


    // =========================================================
    // URL ABSOLUTA
    // =========================================================

    private static string
        AbsolutizarUrl(
            string url)
    {
        if (Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? absoluta))
        {
            return absoluta.ToString();
        }


        return new Uri(
            new Uri(
                UrlBase),
            url)
            .ToString();
    }


    // =========================================================
    // ESTADO GLOBAL
    // =========================================================

    private static void
        ActualizarEstado(
            string mensaje,
            int actual = 0,
            int total = 0,
            EstadoJwHubSync estado =
                EstadoJwHubSync.Preparando)
    {
        bool automatico =
            JwHubSyncState
                .Actual
                .Automatico;


        JwHubSyncState.Establecer(
            estado,
            actual,
            total,
            mensaje,
            automatico);
    }


    // =========================================================
    // CREAR WEBVIEW
    // =========================================================

    private static Task<AndroidWebView>
        CrearWebViewAsync()
    {
        return MauiMainThread
            .InvokeOnMainThreadAsync(
                () =>
                {
                    AndroidContext context;


                    if (MauiPlatform.CurrentActivity
                        is AndroidContext activity)
                    {
                        context =
                            activity;
                    }
                    else
                    {
                        context =
                            AndroidApplication.Context;
                    }


                    var webView =
                        new AndroidWebView(
                            context);


                    webView.Settings
                        .JavaScriptEnabled =
                        true;


                    webView.Settings
                        .DomStorageEnabled =
                        true;


                    webView.Settings
                        .DatabaseEnabled =
                        true;


                    webView.Settings
                        .LoadsImagesAutomatically =
                        true;


                    webView.Settings
                        .JavaScriptCanOpenWindowsAutomatically =
                        false;


                    AndroidCookieManager?
                        cookies =
                            AndroidCookieManager
                                .Instance;


                    if (cookies is not null)
                    {
                        cookies.SetAcceptCookie(
                            true);


                        cookies.SetAcceptThirdPartyCookies(
                            webView,
                            true);


                        cookies.Flush();
                    }


                    return webView;
                });
    }


    // =========================================================
    // DESTRUIR WEBVIEW
    // =========================================================

    private static Task
        DestruirWebViewAsync(
            AndroidWebView webView)
    {
        return MauiMainThread
            .InvokeOnMainThreadAsync(
                () =>
                {
                    try
                    {
                        AndroidCookieManager?
                            cookies =
                                AndroidCookieManager
                                    .Instance;


                        cookies?.Flush();


                        webView.StopLoading();


                        webView.LoadUrl(
                            "about:blank");


                        webView.ClearHistory();


                        webView.RemoveAllViews();


                        webView.Destroy();
                    }
                    catch
                    {
                    }
                });
    }


    // =========================================================
    // CALLBACK DE JAVASCRIPT
    // =========================================================

    private sealed class
        ResultadoJsCallback
        : JavaObject,
          AndroidIValueCallback
    {
        private readonly
            Action<string?> _callback;


        public ResultadoJsCallback(
            Action<string?> callback)
        {
            _callback =
                callback;
        }


        public void OnReceiveValue(
            JavaObject? value)
        {
            _callback(
                value?.ToString());
        }
    }


    // =========================================================
    // DTO TARJETA
    // =========================================================

    private sealed class DatosTarjetaJs
    {
        public string NombreCompleto
        {
            get;
            set;
        } = "";


        public List<string> Badges
        {
            get;
            set;
        } = [];


        public List<ParDatoJs> Pares
        {
            get;
            set;
        } = [];


        public string UrlPerfil
        {
            get;
            set;
        } = "";
    }


    // =========================================================
    // DTO PAR
    // =========================================================

    private sealed class ParDatoJs
    {
        public string Etiqueta
        {
            get;
            set;
        } = "";


        public List<string> Valores
        {
            get;
            set;
        } = [];
    }


    // =========================================================
    // DTO PERFIL
    // =========================================================

    private sealed class DatosPerfilJs
    {
        public string NombrePerfil
        {
            get;
            set;
        } = "";


        public string Email
        {
            get;
            set;
        } = "";


        public string EmailJW
        {
            get;
            set;
        } = "";


        public string Direccion
        {
            get;
            set;
        } = "";


        public string UrlFoto
        {
            get;
            set;
        } = "";
    }


    // =========================================================
    // DTO ESTADO
    // =========================================================

    private sealed class DatosEstadoJs
    {
        public string Esperanza
        {
            get;
            set;
        } = "";


        public string FechaInicioPrec
        {
            get;
            set;
        } = "";
    }
}