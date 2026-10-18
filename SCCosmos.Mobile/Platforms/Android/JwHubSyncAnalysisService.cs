using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    Traslado,
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


    public bool EsTraslado =>
        Tipo ==
        TipoAnalisisJwHub.Traslado;


    public bool EsIgual =>
        Tipo ==
        TipoAnalisisJwHub.Igual;


    // =========================================================
    // DESCARTAR
    //
    // Disponible en todo lo pendiente de decisión.
    // "Sin cambios" no tiene nada que descartar.
    // =========================================================

    public bool MostrarDescartar =>
        !EsIgual;


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


    // =========================================================
    // POSIBLES TRASLADOS
    //
    // Personas que aparecen en JW Hub en otra congregación y
    // que, por las señales de identidad, parecen ser la misma
    // persona que una ficha antigua que ya no aparece.
    // =========================================================

    public List<JwHubSyncItemAnalisis> Traslados
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


    public int TotalTraslados =>
        Traslados.Count;


    public int TotalIguales =>
        Iguales.Count;


    public string CrearResumen()
    {
        return
            $"Comparación completada. " +
            $"Conflictos: {TotalConflictos} · " +
            $"Nuevos: {TotalNuevos} · " +
            $"Posibles bajas: {TotalBajas} · " +
            $"Posibles traslados: {TotalTraslados} · " +
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
            // Orden:
            //
            // 1. Identidad JW Hub ya vinculada.
            // 2. Nombre literal de la ficha (compatibilidad).
            // 3. Persona procedente de S-21 todavía SIN
            //    NombreJWHub, usando una coincidencia fuerte.
            //
            // En el punto 3 NO se guarda nada automáticamente:
            // simplemente se emparejan ambas identidades para
            // que aparezcan como un conflicto revisable.
            // =================================================

            List<Persona> personasCongregacion =
                personasDB
                    .Where(p =>
                        p.CongregacionCodigo ==
                            hubItem.CongregacionCodigo
                        &&
                        !procesadosDB.Contains(
                            p.Id))
                    .ToList();


            Persona? matchDB =
                personasCongregacion
                    .FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(
                            p.NombreJWHub)
                        &&
                        string.Equals(
                            p.NombreJWHub.Trim(),
                            hubItem.NombreJWHub?.Trim()
                                ?? hubItem.Nombre?.Trim()
                                ?? "",
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        !FechasIncompatibles(
                            p.FechaNacimiento,
                            hubItem.FechaNacimiento)
                        &&
                        !FechasIncompatibles(
                            p.FechaBautismo,
                            hubItem.FechaBautismo));


            if (matchDB is null)
            {
                matchDB =
                    personasCongregacion
                        .FirstOrDefault(p =>
                            !string.IsNullOrWhiteSpace(
                                p.Nombre)
                            &&
                            string.Equals(
                                p.Nombre.Trim(),
                                hubItem.Nombre?.Trim()
                                    ?? "",
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            !FechasIncompatibles(
                                p.FechaNacimiento,
                                hubItem.FechaNacimiento)
                            &&
                            !FechasIncompatibles(
                                p.FechaBautismo,
                                hubItem.FechaBautismo));
            }


            bool matchDetectadoDesdeS21 =
                false;


            if (matchDB is null)
            {
                matchDB =
                    BuscarCoincidenciaS21Segura(
                        hubItem,
                        personasCongregacion);

                matchDetectadoDesdeS21 =
                    matchDB is not null;
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
                            matchDetectadoDesdeS21
                                ? "Posible equivalencia S-21 / JW Hub " +
                                  "detectada con alta confianza. " +
                                  "Revisa los cambios antes de guardar: " +
                                  string.Join(
                                      ", ",
                                      camposCambiados)
                                : "Cambios: " +
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
        // POSIBLES TRASLADOS
        //
        // Un "nuevo" de Hub que coincide con una "baja" de
        // otra congregación es, casi siempre, un traslado.
        // Solo se propone cuando hay una única candidata
        // compatible (si hay homónimos se deja como
        // alta/baja normal para que el usuario decida).
        // =====================================================

        DetectarPosiblesTraslados(
            resultado);


        // =====================================================
        // CONSERVAR Y PERSISTIR
        // =====================================================

        UltimoAnalisis =
            resultado;


        GuardarUltimoAnalisis();


        return resultado;
    }


    // =========================================================
    // POSIBLES TRASLADOS
    //
    // Réplica de DetectarPosiblesTraslados de Windows.
    // =========================================================

    private static void DetectarPosiblesTraslados(
        ResultadoAnalisisJwHub resultado)
    {
        var nuevosProcesados =
            new List<JwHubSyncItemAnalisis>();


        var bajasProcesadas =
            new List<JwHubSyncItemAnalisis>();


        foreach (JwHubSyncItemAnalisis nuevo in
                 resultado.Nuevos.ToList())
        {
            if (nuevo.PersonaHub is null)
            {
                continue;
            }


            List<JwHubSyncItemAnalisis> candidatos =
                resultado.Bajas
                    .Where(b =>
                        b.PersonaDB is not null
                        &&
                        !string.Equals(
                            b.PersonaDB.CongregacionCodigo,
                            nuevo.PersonaHub.CongregacionCodigo,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        EsMismaPersonaParaTraslado(
                            b.PersonaDB,
                            nuevo.PersonaHub))
                    .ToList();


            // Por seguridad, solo se propone con una única
            // ficha anterior compatible.
            if (candidatos.Count != 1)
            {
                continue;
            }


            JwHubSyncItemAnalisis baja =
                candidatos[0];


            if (bajasProcesadas.Contains(baja))
            {
                continue;
            }


            string origen =
                !string.IsNullOrWhiteSpace(
                    baja.PersonaDB!.CongregacionNombre)
                    ? baja.PersonaDB.CongregacionNombre
                    : baja.PersonaDB.CongregacionCodigo;


            string destino =
                nuevo.PersonaHub.CongregacionCodigo ?? "";


            resultado.Traslados.Add(
                new JwHubSyncItemAnalisis
                {
                    Tipo =
                        TipoAnalisisJwHub.Traslado,

                    PersonaDB =
                        baja.PersonaDB,

                    PersonaHub =
                        nuevo.PersonaHub,

                    NombreVisual =
                        !string.IsNullOrWhiteSpace(
                            baja.NombreVisual)
                            ? baja.NombreVisual
                            : baja.PersonaDB.Nombre,

                    Descripcion =
                        $"Posible traslado: {origen} → {destino}. " +
                        "Se conservarán ambas fichas enlazadas.",

                    PuedeConfirmarBaja =
                        baja.PuedeConfirmarBaja
                });


            nuevosProcesados.Add(
                nuevo);


            bajasProcesadas.Add(
                baja);
        }


        foreach (JwHubSyncItemAnalisis item in
                 nuevosProcesados)
        {
            resultado.Nuevos.Remove(
                item);
        }


        foreach (JwHubSyncItemAnalisis item in
                 bajasProcesadas)
        {
            resultado.Bajas.Remove(
                item);
        }
    }


    // =========================================================
    // MISMA PERSONA (TRASLADO)
    //
    // Réplica de EsMismaPersonaParaTraslado de Windows:
    // hace falta una señal fuerte de identidad (email JW,
    // email o nombre) y fechas compatibles.
    // =========================================================

    private static bool EsMismaPersonaParaTraslado(
        Persona anterior,
        Persona nueva)
    {
        bool mismoEmailJw =
            !string.IsNullOrWhiteSpace(
                anterior.EmailJW)
            &&
            !string.IsNullOrWhiteSpace(
                nueva.EmailJW)
            &&
            string.Equals(
                anterior.EmailJW.Trim(),
                nueva.EmailJW.Trim(),
                StringComparison.OrdinalIgnoreCase);


        bool mismoEmail =
            !string.IsNullOrWhiteSpace(
                anterior.Email)
            &&
            !string.IsNullOrWhiteSpace(
                nueva.Email)
            &&
            string.Equals(
                anterior.Email.Trim(),
                nueva.Email.Trim(),
                StringComparison.OrdinalIgnoreCase);


        string[] nombresAnterior =
        [
            anterior.NombreJWHub ?? "",
            anterior.Nombre ?? ""
        ];


        string[] nombresNueva =
        [
            nueva.NombreJWHub ?? "",
            nueva.Nombre ?? ""
        ];


        bool mismoNombre =
            nombresAnterior
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x))
                .Any(a =>
                    nombresNueva.Any(n =>
                        !string.IsNullOrWhiteSpace(
                            n)
                        &&
                        string.Equals(
                            a.Trim(),
                            n.Trim(),
                            StringComparison.OrdinalIgnoreCase)));


        if (!mismoEmailJw
            && !mismoEmail
            && !mismoNombre)
        {
            return false;
        }


        if (FechasIncompatiblesTraslado(
                anterior.FechaNacimiento,
                nueva.FechaNacimiento))
        {
            return false;
        }


        if (FechasIncompatiblesTraslado(
                anterior.FechaBautismo,
                nueva.FechaBautismo))
        {
            return false;
        }


        return true;
    }


    private static bool FechasIncompatiblesTraslado(
        string? anterior,
        string? nueva)
    {
        if (string.IsNullOrWhiteSpace(anterior)
            || string.IsNullOrWhiteSpace(nueva))
        {
            return false;
        }


        int anioAnterior =
            ExtraerAnio(anterior);


        int anioNueva =
            ExtraerAnio(nueva);


        if (anioAnterior > 0
            && anioNueva > 0
            && Math.Abs(anioAnterior - anioNueva) > 2)
        {
            return true;
        }


        bool aproxAnterior =
            anterior.Trim()
                .StartsWith(
                    "01/01/",
                    StringComparison.Ordinal);


        bool aproxNueva =
            nueva.Trim()
                .StartsWith(
                    "01/01/",
                    StringComparison.Ordinal);


        // Si ambas son fechas definitivas, una discrepancia
        // completa sirve para descartar homónimos.
        if (!aproxAnterior
            && !aproxNueva)
        {
            return !string.Equals(
                anterior.Trim(),
                nueva.Trim(),
                StringComparison.Ordinal);
        }


        return false;
    }


    private static int ExtraerAnio(
        string fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
        {
            return 0;
        }


        for (int i = fecha.Length - 4;
             i >= 0;
             i--)
        {
            if (i + 4 > fecha.Length)
            {
                continue;
            }


            string trozo =
                fecha.Substring(
                    i,
                    4);


            if (int.TryParse(
                    trozo,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int anio)
                && anio >= 1900
                && anio <= 2100)
            {
                return anio;
            }
        }


        return 0;
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

                    Sexo =
                        pe.Sexo
                        ?? "",

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
    // COINCIDENCIA SEGURA S-21 -> JW HUB
    //
    // Solo considera fichas que todavía NO tienen NombreJWHub.
    //
    // La coincidencia no modifica SQLite. Su único efecto es que
    // la revisión presente las dos identidades juntas para que el
    // usuario confirme los campos de JW Hub.
    // =========================================================

    private static Persona?
        BuscarCoincidenciaS21Segura(
            Persona hub,
            IEnumerable<Persona> candidatos)
    {
        var puntuados =
            candidatos
                .Where(p =>
                    string.IsNullOrWhiteSpace(
                        p.NombreJWHub)
                    &&
                    !string.IsNullOrWhiteSpace(
                        p.Nombre)
                    &&
                    !FechasIncompatibles(
                        p.FechaNacimiento,
                        hub.FechaNacimiento)
                    &&
                    !FechasIncompatibles(
                        p.FechaBautismo,
                        hub.FechaBautismo))
                .Select(p =>
                    new
                    {
                        Persona = p,
                        Puntos =
                            CalcularPuntuacionS21Hub(
                                p,
                                hub)
                    })
                .Where(x =>
                    x.Puntos >= 120)
                .OrderByDescending(x =>
                    x.Puntos)
                .ToList();


        if (puntuados.Count == 0)
        {
            return null;
        }


        if (puntuados.Count == 1)
        {
            return
                puntuados[0]
                    .Persona;
        }


        // Si hay más de un candidato exigimos una ventaja clara.
        // Evitamos enlazar automáticamente homónimos.
        if (puntuados[0].Puntos
            -
            puntuados[1].Puntos
            >= 35)
        {
            return
                puntuados[0]
                    .Persona;
        }


        return null;
    }


    private static int
        CalcularPuntuacionS21Hub(
            Persona s21,
            Persona hub)
    {
        int puntos =
            0;


        string[] tokensS21 =
            ObtenerTokensNombre(
                s21.Nombre);


        string[] tokensHub =
            ObtenerTokensNombre(
                hub.NombreJWHub
                ?? hub.Nombre);


        int comunes =
            tokensS21
                .Intersect(
                    tokensHub,
                    StringComparer.OrdinalIgnoreCase)
                .Count();


        int minimoTokens =
            Math.Min(
                tokensS21.Length,
                tokensHub.Length);


        // Los mismos componentes de nombre, aunque estén en
        // distinto orden ("Apellidos, Nombre" / "Nombre Apellidos").
        bool mismosTokens =
            tokensS21.Length > 0
            &&
            tokensS21.Length ==
                tokensHub.Length
            &&
            comunes ==
                tokensS21.Length;


        if (mismosTokens)
        {
            puntos +=
                140;
        }
        else if (minimoTokens > 0)
        {
            double cobertura =
                (double)comunes /
                minimoTokens;


            if (comunes >= 2
                &&
                cobertura >= 0.66)
            {
                puntos +=
                    70 +
                    comunes * 10;
            }
            else if (comunes >= 1)
            {
                puntos +=
                    10;
            }
        }


        if (FechasIgualesSeguras(
                s21.FechaNacimiento,
                hub.FechaNacimiento))
        {
            puntos +=
                90;
        }
        else if (MismoAnio(
                     s21.FechaNacimiento,
                     hub.FechaNacimiento))
        {
            puntos +=
                30;
        }


        if (FechasIgualesSeguras(
                s21.FechaBautismo,
                hub.FechaBautismo))
        {
            puntos +=
                50;
        }
        else if (MismoAnio(
                     s21.FechaBautismo,
                     hub.FechaBautismo))
        {
            puntos +=
                20;
        }


        if (!string.IsNullOrWhiteSpace(
                s21.Nombramiento)
            &&
            !string.IsNullOrWhiteSpace(
                hub.Nombramiento)
            &&
            string.Equals(
                s21.Nombramiento.Trim(),
                hub.Nombramiento.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            puntos +=
                10;
        }


        if (!string.IsNullOrWhiteSpace(
                s21.Servicio)
            &&
            !string.IsNullOrWhiteSpace(
                hub.Servicio)
            &&
            string.Equals(
                s21.Servicio.Trim(),
                hub.Servicio.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            puntos +=
                10;
        }


        return puntos;
    }


    private static string[]
        ObtenerTokensNombre(
            string? nombre)
    {
        if (string.IsNullOrWhiteSpace(
                nombre))
        {
            return
                Array.Empty<string>();
        }


        string descompuesto =
            nombre
                .Normalize(
                    NormalizationForm.FormD);


        var caracteres =
            descompuesto
                .Where(c =>
                    CharUnicodeInfo
                        .GetUnicodeCategory(c)
                    !=
                    UnicodeCategory
                        .NonSpacingMark)
                .Select(c =>
                    char.IsLetterOrDigit(c)
                        ? char.ToUpperInvariant(c)
                        : ' ')
                .ToArray();


        HashSet<string> ignorar =
            new(
                StringComparer.OrdinalIgnoreCase)
            {
                "DE",
                "DEL",
                "LA",
                "LAS",
                "LOS",
                "Y"
            };


        return
            new string(
                    caracteres)
                .Split(
                    ' ',
                    StringSplitOptions
                        .RemoveEmptyEntries)
                .Select(x =>
                    x.Trim())
                .Where(x =>
                    x.Length > 1
                    &&
                    !ignorar.Contains(
                        x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }


    private static bool
        FechasIgualesSeguras(
            string? a,
            string? b)
    {
        if (string.IsNullOrWhiteSpace(
                a)
            ||
            string.IsNullOrWhiteSpace(
                b))
        {
            return false;
        }


        DateTime? fechaA =
            ParsearFechaSegura(
                a);


        DateTime? fechaB =
            ParsearFechaSegura(
                b);


        if (fechaA.HasValue
            &&
            fechaB.HasValue)
        {
            return
                fechaA.Value.Date ==
                fechaB.Value.Date;
        }


        return
            string.Equals(
                a.Trim(),
                b.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }


    private static bool
        MismoAnio(
            string? a,
            string? b)
    {
        int anioA =
            ObtenerAnio(
                a);


        int anioB =
            ObtenerAnio(
                b);


        return
            anioA > 0
            &&
            anioB > 0
            &&
            anioA ==
                anioB;
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
            !MismoTexto(db.NombreJWHub, hub.NombreJWHub))
        {
            AgregarCampo(
                campos,
                "Nombre JW Hub");
        }


        // =====================================================
        // NOMBRE COMÚN
        //
        // Si la base de datos ya tiene un nombre común se respeta: puede
        // haberlo cambiado el superintendente a mano. JW Hub solo lo aporta
        // cuando el campo está vacío.
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.NombreComun)
            &&
            string.IsNullOrWhiteSpace(
                db.NombreComun))
        {
            AgregarCampo(
                campos,
                "Nombre común");
        }


        // =====================================================
        // NOMBRE COMPLETO (S-21)
        //
        // No se compara aquí.
        //
        // "Nombre" es la identidad oficial procedente del S-21.
        // JW Hub administra NombreJWHub y NombreComun, pero nunca
        // debe convertir una diferencia de Nombre en conflicto ni
        // sustituir el nombre S-21.
        // =====================================================


        // =====================================================
        // SEXO
        // =====================================================

        if (!string.IsNullOrEmpty(
                hub.Sexo)
            &&
            !MismoTexto(db.Sexo, hub.Sexo))
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
            !MismoTexto(dbEsperanza, hubEsperanza))
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
            !MismoTexto(db.FechaNacimiento, hub.FechaNacimiento)
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
            !MismoTexto(db.FechaBautismo, hub.FechaBautismo)
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
            !MismoTexto(db.FechaInicioPrec, hub.FechaInicioPrec)
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
            !MismoTexto(db.FechaUltimaEscuela, hub.FechaUltimaEscuela))
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
            !MismoTexto(db.Email, hub.Email))
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
            !MismoTexto(db.EmailJW, hub.EmailJW))
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
            !MismoTexto(db.Telefono, hub.Telefono))
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
            !MismoTexto(db.Direccion, hub.Direccion))
        {
            AgregarCampo(
                campos,
                "Dirección");
        }


        // =====================================================
        // FOTOGRAFÍA
        //
        // No comparamos los Base64 directamente: Windows puede
        // recodificar la imagen a JPEG y Android descarga los
        // bytes originales. Usamos una comparación visual (dHash)
        // conservadora que evita falsos conflictos.
        // =====================================================

        if (!string.IsNullOrEmpty(
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

    /// <summary>
    /// Compara dos textos ignorando espacios sobrantes: al principio, al
    /// final y los repetidos por dentro. Así no se detectan cambios
    /// irreales cuando el mismo dato (dirección, teléfono, correo…) llega
    /// escrito con un espacio de más.
    /// </summary>
    private static bool MismoTexto(
        string? a,
        string? b)
    {
        return string.Equals(
            NormalizarTexto(a),
            NormalizarTexto(b),
            StringComparison.Ordinal);
    }


    private static string NormalizarTexto(
        string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "";
        }

        var resultado =
            new System.Text.StringBuilder(
                texto.Length);

        bool huecoPendiente =
            false;

        foreach (
            char caracter in texto.Trim())
        {
            if (char.IsWhiteSpace(caracter))
            {
                huecoPendiente = true;

                continue;
            }

            if (huecoPendiente
                &&
                resultado.Length > 0)
            {
                resultado.Append(' ');
            }

            huecoPendiente = false;

            resultado.Append(caracter);
        }

        return resultado.ToString();
    }


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