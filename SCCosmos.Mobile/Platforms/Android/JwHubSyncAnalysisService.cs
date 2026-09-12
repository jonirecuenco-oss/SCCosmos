using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Platforms.Android;


// =============================================================
// TIPO DE RESULTADO DE LA COMPARACIÓN
// =============================================================

public enum TipoAnalisisJwHub
{
    Conflicto,
    Nuevo,
    Baja,
    Igual
}


// =============================================================
// ELEMENTO INDIVIDUAL DEL ANÁLISIS
// =============================================================

public sealed class JwHubSyncItemAnalisis
{
    public TipoAnalisisJwHub Tipo
    {
        get;
        set;
    }


    public Persona? PersonaDB
    {
        get;
        set;
    }


    public Persona? PersonaHub
    {
        get;
        set;
    }


    public string NombreVisual
    {
        get;
        set;
    } = "";


    public string Descripcion
    {
        get;
        set;
    } = "";


    public List<string> CamposCambiados
    {
        get;
        set;
    } = new();


    // =========================================================
    // PROPIEDADES AUXILIARES PARA LA INTERFAZ
    // =========================================================

    public bool EsConflicto =>
        Tipo ==
        TipoAnalisisJwHub.Conflicto;


    public bool EsNuevo =>
        Tipo ==
        TipoAnalisisJwHub.Nuevo;


    public bool EsBaja =>
        Tipo ==
        TipoAnalisisJwHub.Baja;


    // =========================================================
    // BAJAS
    //
    // Solo será true cuando:
    //
    // - la extracción haya terminado correctamente
    // - y NO estemos en modo de prueba limitado.
    // =========================================================

    public bool PuedeConfirmarBaja
    {
        get;
        set;
    }


    public bool MostrarAvisoBajaPrueba =>
        EsBaja
        &&
        !PuedeConfirmarBaja;
}


// =============================================================
// RESULTADO COMPLETO DEL ANÁLISIS
// =============================================================

public sealed class ResultadoAnalisisJwHub
{
    public List<JwHubSyncItemAnalisis> Conflictos
    {
        get;
        set;
    } = new();


    public List<JwHubSyncItemAnalisis> Nuevos
    {
        get;
        set;
    } = new();


    public List<JwHubSyncItemAnalisis> Bajas
    {
        get;
        set;
    } = new();


    public List<JwHubSyncItemAnalisis> Iguales
    {
        get;
        set;
    } = new();


    public bool EsPruebaLimitada
    {
        get;
        set;
    }


    public int TotalConflictos =>
        Conflictos.Count;


    public int TotalNuevos =>
        Nuevos.Count;


    public int TotalBajas =>
        Bajas.Count;


    public int TotalIguales =>
        Iguales.Count;


    public string CrearResumen()
    {
        return
            $"Comparación completada. " +
            $"Conflictos: {TotalConflictos} · " +
            $"Nuevos: {TotalNuevos} · " +
            $"Posibles bajas: {TotalBajas} · " +
            $"Sin cambios: {TotalIguales}";
    }
}


// =============================================================
// SERVICIO DE COMPARACIÓN JW HUB / SQLITE
// =============================================================

public static class JwHubSyncAnalysisService
{
    // =========================================================
    // PRIVILEGIOS QUE JW HUB ADMINISTRA
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


    // =========================================================
    // ÚLTIMO ANÁLISIS
    //
    // La primera vez que se solicita tras arrancar la app,
    // intentamos recuperarlo del almacenamiento persistente.
    // =========================================================

    private static ResultadoAnalisisJwHub?
        _ultimoAnalisis;


    private static bool
        _intentoCargaPersistida;


    public static ResultadoAnalisisJwHub?
        UltimoAnalisis
    {
        get
        {
            if (_ultimoAnalisis is null
                &&
                !_intentoCargaPersistida)
            {
                _intentoCargaPersistida =
                    true;


                _ultimoAnalisis =
                    JwHubSyncReviewStore
                        .Cargar();
            }


            return _ultimoAnalisis;
        }

        private set
        {
            _ultimoAnalisis =
                value;


            _intentoCargaPersistida =
                true;
        }
    }


    // =========================================================
    // GUARDAR REVISIÓN ACTUAL
    // =========================================================

    public static void GuardarUltimoAnalisis()
    {
        if (_ultimoAnalisis is null)
        {
            return;
        }


        JwHubSyncReviewStore
            .Guardar(
                _ultimoAnalisis);
    }


    // =========================================================
    // LIMPIAR REVISIÓN
    //
    // Se utiliza al pulsar FINALIZAR.
    // =========================================================

    public static void LimpiarUltimoAnalisis()
    {
        _ultimoAnalisis =
            null;


        _intentoCargaPersistida =
            true;


        JwHubSyncReviewStore
            .Eliminar();
    }


    // =========================================================
    // ANALIZAR
    // =========================================================

    public static ResultadoAnalisisJwHub Analizar(
        ResultadoSintonizacionJwHubAndroid extraccion,
        bool calcularBajas = true)
    {
        if (extraccion is null)
        {
            throw new ArgumentNullException(
                nameof(extraccion));
        }


        // =====================================================
        // PARA CALCULAR BAJAS NECESITAMOS QUE EL TRABAJO
        // SOLICITADO HAYA TERMINADO COMPLETAMENTE.
        //
        // En modo prueba:
        //
        // 3/3 o 5/5 también puede ser ExtraccionCompleta,
        // pero las bajas quedarán BLOQUEADAS por
        // EsPruebaLimitada.
        // =====================================================

        if (calcularBajas
            &&
            !extraccion.ExtraccionCompleta)
        {
            throw new InvalidOperationException(
                "No se pueden calcular posibles bajas " +
                "porque la extracción de JW Hub no " +
                "terminó completamente.");
        }


        // =====================================================
        // CONVERTIR PERSONAS JW HUB -> PERSONA SCCOSMOS
        // =====================================================

        List<Persona> personasHub =
            ConvertirPersonasHub(
                extraccion.Personas);


        // =====================================================
        // CARGAR PERSONAS ACTIVAS DE SQLITE
        // =====================================================

        List<Persona> personasDB =
            DatabaseHelper
                .ObtenerPersonas()
                .Where(
                    PersonaEstaActiva)
                .ToList();


        var procesadosDB =
            new HashSet<int>();


        var resultado =
            new ResultadoAnalisisJwHub
            {
                EsPruebaLimitada =
                    extraccion.EsPruebaLimitada
            };


        // =====================================================
        // RECORRER JW HUB
        // =====================================================

        foreach (Persona hubItem
                 in personasHub)
        {
            // =================================================
            // POSIBLES COINCIDENCIAS
            //
            // Igual que Windows:
            //
            // Nombre o NombreJWHub
            // +
            // CongregacionCodigo
            // =================================================

            List<Persona> posiblesMatches =
                personasDB
                    .Where(p =>
                        (
                            p.Nombre ==
                                hubItem.Nombre
                            ||
                            p.NombreJWHub ==
                                hubItem.Nombre
                        )
                        &&
                        p.CongregacionCodigo ==
                            hubItem.CongregacionCodigo)
                    .ToList();


            Persona? matchDB =
                null;


            // =================================================
            // FILTRO ANTI-HOMÓNIMOS
            // =================================================

            if (posiblesMatches.Count > 0)
            {
                matchDB =
                    posiblesMatches
                        .FirstOrDefault(p =>
                            !FechasIncompatibles(
                                p.FechaNacimiento,
                                hubItem.FechaNacimiento)
                            &&
                            !FechasIncompatibles(
                                p.FechaBautismo,
                                hubItem.FechaBautismo));
            }


            // =================================================
            // NO EXISTE EN SQLITE
            //
            // Puede ser:
            //
            // - persona realmente nueva
            // - persona cuyo nombre ha cambiado
            //
            // La interfaz permitirá vincularla con una
            // posible baja.
            // =================================================

            if (matchDB is null)
            {
                resultado.Nuevos.Add(
                    new JwHubSyncItemAnalisis
                    {
                        Tipo =
                            TipoAnalisisJwHub.Nuevo,

                        PersonaDB =
                            null,

                        PersonaHub =
                            hubItem,

                        NombreVisual =
                            ObtenerNombreVisual(
                                hubItem),

                        Descripcion =
                            "Aparece en JW Hub pero no " +
                            "se ha encontrado una ficha " +
                            "coincidente en la base de datos."
                    });


                continue;
            }


            // =================================================
            // PERSONA EMPAREJADA
            // =================================================

            procesadosDB.Add(
                matchDB.Id);


            List<string> camposCambiados =
                ObtenerCamposCambiados(
                    matchDB,
                    hubItem);


            // =================================================
            // CONFLICTO
            // =================================================

            if (camposCambiados.Count > 0)
            {
                resultado.Conflictos.Add(
                    new JwHubSyncItemAnalisis
                    {
                        Tipo =
                            TipoAnalisisJwHub.Conflicto,

                        PersonaDB =
                            matchDB,

                        PersonaHub =
                            hubItem,

                        NombreVisual =
                            ObtenerNombreVisual(
                                matchDB),

                        Descripcion =
                            "Cambios: " +
                            string.Join(
                                ", ",
                                camposCambiados),

                        CamposCambiados =
                            camposCambiados
                    });
            }

            // =================================================
            // IGUAL
            // =================================================

            else
            {
                resultado.Iguales.Add(
                    new JwHubSyncItemAnalisis
                    {
                        Tipo =
                            TipoAnalisisJwHub.Igual,

                        PersonaDB =
                            matchDB,

                        PersonaHub =
                            hubItem,

                        NombreVisual =
                            ObtenerNombreVisual(
                                matchDB),

                        Descripcion =
                            "Coinciden todos los datos."
                    });
            }
        }


        // =====================================================
        // POSIBLES BAJAS
        //
        // Personas activas de SQLite que no encontraron
        // correspondencia en JW Hub.
        //
        // Una posible baja también puede resultar ser una
        // persona cuyo nombre cambió en JW Hub. Por eso nunca
        // se aplica automáticamente.
        // =====================================================

        if (calcularBajas)
        {
            foreach (Persona dbItem
                     in personasDB)
            {
                if (procesadosDB.Contains(
                        dbItem.Id))
                {
                    continue;
                }


                resultado.Bajas.Add(
                    new JwHubSyncItemAnalisis
                    {
                        Tipo =
                            TipoAnalisisJwHub.Baja,

                        PersonaDB =
                            dbItem,

                        PersonaHub =
                            null,

                        NombreVisual =
                            ObtenerNombreVisual(
                                dbItem),

                        Descripcion =
                            "Ya no aparece con esta identidad " +
                            "en JW Hub. Puede tratarse de una " +
                            "baja, un traslado o un cambio de nombre.",

                        PuedeConfirmarBaja =
                            extraccion.ExtraccionCompleta
                            &&
                            !extraccion.EsPruebaLimitada
                    });
            }
        }


        // =====================================================
        // CONSERVAR Y PERSISTIR
        // =====================================================

        UltimoAnalisis =
            resultado;


        GuardarUltimoAnalisis();


        return resultado;
    }


    // =========================================================
    // CONVERTIR PERSONAS EXTRAÍDAS
    // =========================================================

    private static List<Persona>
        ConvertirPersonasHub(
            IEnumerable<PersonaExtraidaJwHubAndroid>
                personasExtraidas)
    {
        List<Congregacion> congregaciones =
            DatabaseHelper
                .ObtenerCongregaciones();


        var resultado =
            new List<Persona>();


        foreach (
            PersonaExtraidaJwHubAndroid pe
            in personasExtraidas)
        {
            string congreHub =
                pe.Congregacion
                ?? "";


            string codigoDetectado =
                "";


            string nombreLimpiado =
                congreHub;


            // =================================================
            // EJEMPLO:
            //
            // Almendralejo (778)
            // =================================================

            int start =
                congreHub.LastIndexOf(
                    '(');


            int end =
                congreHub.LastIndexOf(
                    ')');


            if (start >= 0
                &&
                end > start)
            {
                codigoDetectado =
                    congreHub
                        .Substring(
                            start + 1,
                            end - start - 1)
                        .Trim();


                nombreLimpiado =
                    congreHub
                        .Substring(
                            0,
                            start)
                        .Trim();
            }


            // =================================================
            // CONFIRMAR CONTRA TABLA CONGREGACIONES
            // =================================================

            Congregacion? congreMatch =
                congregaciones
                    .FirstOrDefault(c =>
                        (
                            !string.IsNullOrEmpty(
                                codigoDetectado)
                            &&
                            c.Codigo ==
                                codigoDetectado
                        )
                        ||
                        (
                            !string.IsNullOrWhiteSpace(
                                c.Nombre)
                            &&
                            c.Nombre.Equals(
                                nombreLimpiado,
                                StringComparison
                                    .OrdinalIgnoreCase)
                        )
                        ||
                        (
                            !string.IsNullOrWhiteSpace(
                                c.Nombre)
                            &&
                            congreHub.Contains(
                                c.Nombre,
                                StringComparison
                                    .OrdinalIgnoreCase)
                        ));


            if (congreMatch is not null)
            {
                codigoDetectado =
                    congreMatch.Codigo
                    ?? "";


                nombreLimpiado =
                    congreMatch.Nombre
                    ?? nombreLimpiado;
            }


            resultado.Add(
                new Persona
                {
                    Nombre =
                        pe.NombreCompleto
                        ?? "",

                    NombreJWHub =
                        pe.NombreCompleto
                        ?? "",

                    NombreComun =
                        pe.NombreComun
                        ?? "",

                    // El extractor actual no obtiene Sexo.
                    Sexo =
                        "",

                    Esperanza =
                        pe.Esperanza
                        ?? "",

                    FechaNacimiento =
                        pe.FechaNacimiento
                        ?? "",

                    FechaBautismo =
                        pe.FechaBautismo
                        ?? "",

                    FechaInicioPrec =
                        pe.FechaInicioPrec
                        ?? "",

                    FechaUltimaEscuela =
                        pe.FechaUltimaEscuela
                        ?? "",

                    Solicitudes =
                        pe.Solicitudes
                        ?? "",

                    Nombramiento =
                        pe.Nombramiento
                        ?? "",

                    Servicio =
                        pe.Servicio
                        ?? "",

                    Privilegios =
                        pe.Privilegios
                        ?? "",

                    CongregacionNombre =
                        nombreLimpiado,

                    CongregacionCodigo =
                        codigoDetectado,

                    Telefono =
                        pe.Telefono
                        ?? "",

                    Email =
                        pe.Email
                        ?? "",

                    EmailJW =
                        pe.EmailJW
                        ?? "",

                    Direccion =
                        pe.Direccion
                        ?? "",

                    Foto =
                        pe.Foto
                        ?? ""
                });
        }


        return resultado;
    }


    // =========================================================
    // PERSONA ACTIVA
    // =========================================================

    private static bool PersonaEstaActiva(
        Persona persona)
    {
        if (string.IsNullOrWhiteSpace(
                persona.FechaEliminacion))
        {
            return true;
        }


        DateTime? fecha =
            ParsearFechaSegura(
                persona.FechaEliminacion);


        return
            fecha.HasValue
            &&
            fecha.Value >
                DateTime.Now;
    }


    // =========================================================
    // FILTRO ANTI-HOMÓNIMOS
    // =========================================================

    private static bool FechasIncompatibles(
        string? fechaDB,
        string? fechaHub)
    {
        if (string.IsNullOrWhiteSpace(
                fechaDB)
            ||
            string.IsNullOrWhiteSpace(
                fechaHub))
        {
            return false;
        }


        string db =
            fechaDB.Trim();


        string hub =
            fechaHub.Trim();


        bool dbAproximada =
            db.StartsWith(
                "01/01/",
                StringComparison.Ordinal);


        bool hubAproximada =
            hub.StartsWith(
                "01/01/",
                StringComparison.Ordinal);


        // Si las dos son exactas, deben coincidir.

        if (!dbAproximada
            &&
            !hubAproximada)
        {
            return
                db != hub;
        }


        // Si alguna es aproximada, solo consideramos
        // incompatibilidad clara cuando los años están
        // separados por más de dos años.

        int anioDB =
            ObtenerAnio(
                db);


        int anioHub =
            ObtenerAnio(
                hub);


        if (anioDB > 0
            &&
            anioHub > 0
            &&
            Math.Abs(
                anioDB -
                anioHub) > 2)
        {
            return true;
        }


        return false;
    }


    // =========================================================
    // OBTENER CAMPOS CAMBIADOS
    // =========================================================

    private static List<string>
        ObtenerCamposCambiados(
            Persona db,
            Persona hub)
    {
        var campos =
            new List<string>();


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
                campos,
                "Nombre JW Hub");
        }


        // =====================================================
        // NOMBRE COMÚN
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.NombreComun)
            &&
            db.NombreComun !=
                hub.NombreComun)
        {
            AgregarCampo(
                campos,
                "Nombre común");
        }


        // =====================================================
        // NOMBRE COMPLETO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Nombre)
            &&
            db.Nombre !=
                hub.Nombre)
        {
            AgregarCampo(
                campos,
                "Nombre completo");
        }


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
                campos,
                "Sexo");
        }


        // =====================================================
        // ESPERANZA
        // =====================================================

        string hubEsperanza =
            hub.Esperanza
            ?? "";


        string dbEsperanza =
            db.Esperanza
            ?? "";


        if (hubEsperanza != ""
            &&
            dbEsperanza !=
                hubEsperanza)
        {
            AgregarCampo(
                campos,
                "Esperanza");
        }


        // =====================================================
        // NOMBRAMIENTO
        //
        // El vacío es significativo.
        // =====================================================

        string hubNombramiento =
            hub.Nombramiento
            ?? "";


        string dbNombramiento =
            db.Nombramiento
            ?? "";


        if (hubNombramiento !=
            dbNombramiento)
        {
            AgregarCampo(
                campos,
                "Nombramiento");
        }


        // =====================================================
        // SERVICIO
        //
        // El vacío también es significativo.
        // =====================================================

        string hubServicio =
            hub.Servicio
            ?? "";


        string dbServicio =
            db.Servicio
            ?? "";


        if (hubServicio !=
            dbServicio)
        {
            AgregarCampo(
                campos,
                "Servicio");
        }


        // =====================================================
        // PRIVILEGIOS
        // =====================================================

        List<string> hubPrivilegios =
            SepararPrivilegios(
                hub.Privilegios);


        List<string> dbPrivilegios =
            SepararPrivilegios(
                db.Privilegios);


        bool cambioPrivilegios =
            false;


        foreach (string privilegio
                 in dbPrivilegios)
        {
            if (PrivilegiosPermitidos
                    .Contains(
                        privilegio)
                &&
                !hubPrivilegios.Contains(
                    privilegio))
            {
                cambioPrivilegios =
                    true;

                break;
            }
        }


        if (!cambioPrivilegios)
        {
            foreach (string privilegio
                     in hubPrivilegios)
            {
                if (PrivilegiosPermitidos
                        .Contains(
                            privilegio)
                    &&
                    !dbPrivilegios.Contains(
                        privilegio))
                {
                    cambioPrivilegios =
                        true;

                    break;
                }
            }
        }


        if (cambioPrivilegios)
        {
            AgregarCampo(
                campos,
                "Privilegios");
        }


        // =====================================================
        // NACIMIENTO
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
                campos,
                "Fecha de nacimiento");
        }


        // =====================================================
        // BAUTISMO
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
                campos,
                "Fecha de bautismo");
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
                campos,
                "Inicio de precursorado");
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
                campos,
                "Última escuela");
        }


        // =====================================================
        // EMAIL PERSONAL
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Email)
            &&
            db.Email !=
                hub.Email)
        {
            AgregarCampo(
                campos,
                "Email");
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
                campos,
                "Email JW");
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
                campos,
                "Teléfono");
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
                campos,
                "Dirección");
        }


        // =====================================================
        // FOTOGRAFÍA
        //
        // Nunca comparamos Base64 directamente.
        //
        // Si JW Hub no trae fotografía, no proponemos borrar
        // una fotografía local.
        //
        // Si JW Hub sí trae fotografía:
        // - SQLite vacío -> conflicto.
        // - ambas presentes -> comparación perceptual dHash.
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                hub.Foto)
            &&
            !JwHubPhotoComparer
                .SonVisualmenteIguales(
                    db.Foto,
                    hub.Foto))
        {
            AgregarCampo(
                campos,
                "Fotografía");
        }


        // =====================================================
        // SOLICITUDES
        //
        // Aquí el vacío SÍ tiene significado.
        // =====================================================

        List<string> hubSolicitudes =
            SepararSolicitudes(
                hub.Solicitudes);


        List<string> dbSolicitudes =
            SepararSolicitudes(
                db.Solicitudes);


        if (!hubSolicitudes
                .SequenceEqual(
                    dbSolicitudes))
        {
            AgregarCampo(
                campos,
                "Solicitudes");
        }


        return campos;
    }


    // =========================================================
    // AGREGAR CAMPO
    // =========================================================

    private static void AgregarCampo(
        List<string> campos,
        string campo)
    {
        if (!campos.Contains(
                campo,
                StringComparer.OrdinalIgnoreCase))
        {
            campos.Add(
                campo);
        }
    }


    // =========================================================
    // SEPARAR PRIVILEGIOS
    // =========================================================

    private static List<string>
        SepararPrivilegios(
            string? texto)
    {
        return
            (texto ?? "")
                .Split(
                    new[] { '\n' },
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .Select(p =>
                    p.Trim())
                .Where(p =>
                    !string.IsNullOrWhiteSpace(
                        p))
                .ToList();
    }


    // =========================================================
    // SEPARAR SOLICITUDES
    // =========================================================

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
                .Select(s =>
                    s.Trim())
                .Where(s =>
                    !string.IsNullOrWhiteSpace(
                        s))
                .OrderBy(s =>
                    s)
                .ToList();
    }


    // =========================================================
    // NOMBRE VISUAL
    // =========================================================

    private static string ObtenerNombreVisual(
        Persona persona)
    {
        if (!string.IsNullOrWhiteSpace(
                persona.NombreComun))
        {
            return
                persona.NombreComun;
        }


        if (!string.IsNullOrWhiteSpace(
                persona.Nombre))
        {
            return
                persona.Nombre;
        }


        if (!string.IsNullOrWhiteSpace(
                persona.NombreJWHub))
        {
            return
                persona.NombreJWHub;
        }


        return
            "(Sin nombre)";
    }


    // =========================================================
    // OBTENER AÑO
    // =========================================================

    private static int ObtenerAnio(
        string? fecha)
    {
        if (string.IsNullOrWhiteSpace(
                fecha))
        {
            return 0;
        }


        Match match =
            Regex.Match(
                fecha,
                @"\b(\d{4})\b",
                RegexOptions.RightToLeft);


        if (!match.Success)
        {
            return 0;
        }


        return
            int.TryParse(
                match.Groups[1].Value,
                out int anio)
                ? anio
                : 0;
    }


    // =========================================================
    // PARSEAR FECHA DE FORMA SEGURA
    // =========================================================

    private static DateTime? ParsearFechaSegura(
        string? fecha)
    {
        if (string.IsNullOrWhiteSpace(
                fecha))
        {
            return null;
        }


        if (DateTime.TryParseExact(
                fecha.Trim(),
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime exacta))
        {
            return exacta;
        }


        if (DateTime.TryParse(
                fecha,
                new CultureInfo(
                    "es-ES"),
                DateTimeStyles.None,
                out DateTime espanola))
        {
            return espanola;
        }


        if (DateTime.TryParse(
                fecha,
                out DateTime general))
        {
            return general;
        }


        return null;
    }
}