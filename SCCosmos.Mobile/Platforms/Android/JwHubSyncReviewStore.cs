using System.Text.Json;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Platforms.Android;


// =============================================================
// PERSISTENCIA DE LA REVISIÓN JW HUB
//
// Permite recuperar Conflictos / Nuevos / Bajas / Iguales
// aunque Android cierre completamente el proceso.
//
// Las personas que ya existen en SQLite se almacenan únicamente
// mediante su Id.
//
// Para los Nuevos sí almacenamos PersonaHub completa.
// =============================================================

public static class JwHubSyncReviewStore
{
    private const string NombreArchivo =
        "jwhub_revision_pendiente.json";


    private static string RutaArchivo =>
        Path.Combine(
            FileSystem.AppDataDirectory,
            NombreArchivo);


    private static readonly JsonSerializerOptions
        OpcionesJson =
            new()
            {
                PropertyNameCaseInsensitive =
                    true,

                WriteIndented =
                    false,

                // Evita serializar propiedades calculadas
                // como EsConflicto, NombreMostrarTabla, etc.
                IgnoreReadOnlyProperties =
                    true
            };


    // =========================================================
    // EXISTE
    // =========================================================

    public static bool ExisteRevisionPendiente =>
        File.Exists(
            RutaArchivo);


    // =========================================================
    // GUARDAR
    // =========================================================

    public static void Guardar(
        ResultadoAnalisisJwHub? analisis)
    {
        if (analisis is null)
        {
            Eliminar();
            return;
        }


        var archivo =
            new RevisionPersistida
            {
                Version =
                    1,

                GuardadoUtc =
                    DateTime.UtcNow,

                EsPruebaLimitada =
                    analisis.EsPruebaLimitada,

                Conflictos =
                    analisis
                        .Conflictos
                        .Select(
                            CrearItemPersistido)
                        .ToList(),

                Nuevos =
                    analisis
                        .Nuevos
                        .Select(
                            CrearItemPersistido)
                        .ToList(),

                Bajas =
                    analisis
                        .Bajas
                        .Select(
                            CrearItemPersistido)
                        .ToList(),

                Iguales =
                    analisis
                        .Iguales
                        .Select(
                            CrearItemPersistido)
                        .ToList()
            };


        string json =
            JsonSerializer.Serialize(
                archivo,
                OpcionesJson);


        string temporal =
            RutaArchivo +
            ".tmp";


        // Escritura atómica:
        //
        // si Android mata el proceso durante la escritura,
        // evitamos dejar corrupto el archivo anterior.

        File.WriteAllText(
            temporal,
            json);


        File.Move(
            temporal,
            RutaArchivo,
            true);
    }


    // =========================================================
    // CARGAR
    // =========================================================

    public static ResultadoAnalisisJwHub?
        Cargar()
    {
        if (!File.Exists(
                RutaArchivo))
        {
            return null;
        }


        try
        {
            string json =
                File.ReadAllText(
                    RutaArchivo);


            RevisionPersistida? archivo =
                JsonSerializer
                    .Deserialize<RevisionPersistida>(
                        json,
                        OpcionesJson);


            if (archivo is null)
            {
                return null;
            }


            // Si en el futuro cambiamos el formato,
            // aquí podremos migrarlo de forma controlada.

            if (archivo.Version != 1)
            {
                return null;
            }


            var resultado =
                new ResultadoAnalisisJwHub
                {
                    EsPruebaLimitada =
                        archivo.EsPruebaLimitada
                };


            RestaurarLista(
                archivo.Conflictos,
                resultado.Conflictos);


            RestaurarLista(
                archivo.Nuevos,
                resultado.Nuevos);


            RestaurarLista(
                archivo.Bajas,
                resultado.Bajas);


            RestaurarLista(
                archivo.Iguales,
                resultado.Iguales);


            return resultado;
        }
        catch
        {
            // No borramos automáticamente el archivo.
            //
            // Si hubo un fallo temporal leyendo SQLite,
            // podemos volver a intentarlo posteriormente.

            return null;
        }
    }


    // =========================================================
    // ELIMINAR
    // =========================================================

    public static void Eliminar()
    {
        try
        {
            if (File.Exists(
                    RutaArchivo))
            {
                File.Delete(
                    RutaArchivo);
            }


            string temporal =
                RutaArchivo +
                ".tmp";


            if (File.Exists(
                    temporal))
            {
                File.Delete(
                    temporal);
            }
        }
        catch
        {
            // La eliminación no debe hacer caer la aplicación.
        }
    }


    // =========================================================
    // CREAR ELEMENTO PERSISTIDO
    // =========================================================

    private static ItemPersistido
        CrearItemPersistido(
            JwHubSyncItemAnalisis item)
    {
        var persistido =
            new ItemPersistido
            {
                Tipo =
                    item.Tipo,

                PersonaDbId =
                    item.PersonaDB?.Id,

                NombreVisual =
                    item.NombreVisual ?? "",

                Descripcion =
                    item.Descripcion ?? "",

                CamposCambiados =
                    item.CamposCambiados?
                        .ToList()
                    ??
                    new List<string>(),

                PuedeConfirmarBaja =
                    item.PuedeConfirmarBaja
            };


        // =====================================================
        // PERSONA HUB
        //
        // Conflicto:
        // necesitamos los valores Hub para poder resolverlo.
        //
        // Nuevo:
        // necesitamos la persona completa porque aún no existe
        // en SQLite.
        //
        // Baja:
        // no hay PersonaHub.
        //
        // Igual:
        // ya no necesitamos almacenar la copia Hub.
        // =====================================================

        if (item.Tipo ==
                TipoAnalisisJwHub.Conflicto
            ||
            item.Tipo ==
                TipoAnalisisJwHub.Nuevo)
        {
            bool conservarFoto =
                item.Tipo ==
                    TipoAnalisisJwHub.Nuevo
                ||
                (
                    item.Tipo ==
                        TipoAnalisisJwHub.Conflicto
                    &&
                    item.CamposCambiados
                        .Any(c =>
                            string.Equals(
                                c,
                                "Fotografía",
                                StringComparison
                                    .OrdinalIgnoreCase))
                );


            persistido.PersonaHub =
                CopiarPersonaHub(
                    item.PersonaHub,
                    conservarFoto);
        }


        return persistido;
    }


    // =========================================================
    // RESTAURAR LISTA
    // =========================================================

    private static void RestaurarLista(
        IEnumerable<ItemPersistido> origen,
        ICollection<JwHubSyncItemAnalisis> destino)
    {
        foreach (ItemPersistido guardado
                 in origen)
        {
            Persona? personaDb =
                null;


            if (guardado.PersonaDbId
                is int id
                &&
                id > 0)
            {
                personaDb =
                    DatabaseHelper
                        .ObtenerPersonaPorId(
                            id);
            }


            // =================================================
            // Si era una persona existente y ya no está en BD,
            // no podemos restaurar ese elemento de forma segura.
            // =================================================

            bool necesitaPersonaDb =
                guardado.Tipo ==
                    TipoAnalisisJwHub.Conflicto
                ||
                guardado.Tipo ==
                    TipoAnalisisJwHub.Baja
                ||
                guardado.Tipo ==
                    TipoAnalisisJwHub.Igual;


            if (necesitaPersonaDb
                &&
                personaDb is null)
            {
                continue;
            }


            // Los Nuevos necesitan los datos Hub.

            if (guardado.Tipo ==
                    TipoAnalisisJwHub.Nuevo
                &&
                guardado.PersonaHub is null)
            {
                continue;
            }


            destino.Add(
                new JwHubSyncItemAnalisis
                {
                    Tipo =
                        guardado.Tipo,

                    PersonaDB =
                        personaDb,

                    PersonaHub =
                        guardado.PersonaHub,

                    NombreVisual =
                        guardado.NombreVisual,

                    Descripcion =
                        guardado.Descripcion,

                    CamposCambiados =
                        guardado.CamposCambiados
                            ?? new List<string>(),

                    PuedeConfirmarBaja =
                        guardado.PuedeConfirmarBaja
                });
        }
    }


    // =========================================================
    // COPIAR PERSONA HUB
    //
    // Para Conflictos guardamos la foto únicamente cuando
    // "Fotografía" forma parte de CamposCambiados.
    //
    // Para Nuevos la conservamos siempre porque necesitamos
    // la ficha completa después de reiniciar la aplicación.
    // =========================================================

    private static Persona?
        CopiarPersonaHub(
            Persona? origen,
            bool conservarFoto)
    {
        if (origen is null)
        {
            return null;
        }


        return new Persona
        {
            Id =
                origen.Id,

            Grupo =
                origen.Grupo,

            Nombre =
                origen.Nombre ?? "",

            FechaNacimiento =
                origen.FechaNacimiento ?? "",

            Sexo =
                origen.Sexo ?? "",

            FechaBautismo =
                origen.FechaBautismo ?? "",

            Esperanza =
                origen.Esperanza ?? "",

            Nombramiento =
                origen.Nombramiento ?? "",

            Servicio =
                origen.Servicio ?? "",

            CongregacionCodigo =
                origen.CongregacionCodigo ?? "",

            CongregacionNombre =
                origen.CongregacionNombre ?? "",

            Telefono =
                origen.Telefono ?? "",

            Email =
                origen.Email ?? "",

            FechaInicioPrec =
                origen.FechaInicioPrec ?? "",

            FechaUltimaEscuela =
                origen.FechaUltimaEscuela ?? "",

            NombreJWHub =
                origen.NombreJWHub ?? "",

            FechaCreacion =
                origen.FechaCreacion ?? "",

            FechaEliminacion =
                origen.FechaEliminacion ?? "",

            Solicitudes =
                origen.Solicitudes ?? "",

            Privilegios =
                origen.Privilegios ?? "",

            Foto =
                conservarFoto
                    ? origen.Foto ?? ""
                    : "",

            NombreComun =
                origen.NombreComun ?? "",

            Direccion =
                origen.Direccion ?? "",

            Latitud =
                origen.Latitud,

            Longitud =
                origen.Longitud,

            ConyugeId =
                origen.ConyugeId,

            EmailJW =
                origen.EmailJW ?? "",

            PersonaRaizId =
                origen.PersonaRaizId,

            MesHistorico =
                origen.MesHistorico,

            FechaExactaHistorico =
                origen.FechaExactaHistorico ?? "",

            Notas =
                origen.Notas ?? ""
        };
    }


    // =========================================================
    // DTO DEL ARCHIVO
    // =========================================================

    private sealed class RevisionPersistida
    {
        public int Version
        {
            get;
            set;
        }


        public DateTime GuardadoUtc
        {
            get;
            set;
        }


        public bool EsPruebaLimitada
        {
            get;
            set;
        }


        public List<ItemPersistido> Conflictos
        {
            get;
            set;
        } = new();


        public List<ItemPersistido> Nuevos
        {
            get;
            set;
        } = new();


        public List<ItemPersistido> Bajas
        {
            get;
            set;
        } = new();


        public List<ItemPersistido> Iguales
        {
            get;
            set;
        } = new();
    }


    private sealed class ItemPersistido
    {
        public TipoAnalisisJwHub Tipo
        {
            get;
            set;
        }


        public int? PersonaDbId
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


        public bool PuedeConfirmarBaja
        {
            get;
            set;
        }
    }
}