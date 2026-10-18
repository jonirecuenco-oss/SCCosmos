using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCCosmos.Sincro;

// ============================================================
// CONFIGURACIÓN DEL MOTOR DE SINCRONIZACIÓN ENTRE DISPOSITIVOS
//
// Para una misma aplicación (Windows y Android comparten este
// archivo) se guarda en un JSON pequeño.
// ============================================================

public sealed class SincroConfiguracion
{
    /// <summary>
    /// Sincronización entre dispositivos activada.
    /// </summary>
    public bool Activo
    {
        get;
        set;
    } = true;


    /// <summary>
    /// Identificador único y estable de este dispositivo.
    /// </summary>
    public string IdDispositivo
    {
        get;
        set;
    } = "";


    /// <summary>
    /// Nombre visible del dispositivo ("PC-Joni", "Móvil Joni"...).
    /// </summary>
    public string NombreDispositivo
    {
        get;
        set;
    } = "";


    /// <summary>
    /// Clave compartida entre los dispositivos de casa. Se escribe
    /// una vez en cada dispositivo para poder "vincularlos".
    /// </summary>
    public string Clave
    {
        get;
        set;
    } = "";


    /// <summary>
    /// Puerto TCP de sincronización.
    /// </summary>
    public int Puerto
    {
        get;
        set;
    } = PuertoPredeterminado;


    /// <summary>
    /// Direcciones manuales ("192.168.1.50", "100.64.0.7",
    /// "micasa.duckdns.org:47311") para cuando no se descubre
    /// solo (datos móviles, Tailscale, etc.).
    /// </summary>
    public List<string> Direcciones
    {
        get;
        set;
    } = new();


    /// <summary>
    /// Ids de dispositivos desmarcados: no se sincroniza con ellos.
    /// </summary>
    public List<string> Deshabilitados
    {
        get;
        set;
    } = new();


    public bool EstaHabilitado(
        string id)
    {
        if (string.IsNullOrWhiteSpace(
                id))
        {
            return true;
        }

        foreach (string deshabilitado in
                 Deshabilitados)
        {
            if (string.Equals(
                    deshabilitado,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }


    /// <summary>
    /// Descubrimiento automático por la red local (UDP). En las
    /// pruebas se desactiva para forzar el canal por carpeta.
    /// </summary>
    public bool Descubrimiento
    {
        get;
        set;
    } = true;


    [JsonIgnore]
    public const int PuertoPredeterminado =
        47311;


    [JsonIgnore]
    public const int PuertoDescubrimiento =
        47312;


    // ========================================================
    // VALORES NUEVOS
    // ========================================================

    public static string GenerarId()
    {
        return Guid
            .NewGuid()
            .ToString("N");
    }


    /// <summary>
    /// Clave numérica de 8 dígitos: fácil de copiar de un
    /// dispositivo a otro.
    /// </summary>
    public static string GenerarClave()
    {
        int valor =
            Random.Shared.Next(
                10_000_000,
                100_000_000);

        return valor.ToString();
    }


    /// <summary>
    /// Completa los valores que falten (id, clave, nombre...).
    /// </summary>
    public void Completar(
        string nombrePredeterminado)
    {
        if (string.IsNullOrWhiteSpace(
                IdDispositivo))
        {
            IdDispositivo =
                GenerarId();
        }

        if (string.IsNullOrWhiteSpace(
                Clave))
        {
            Clave =
                GenerarClave();
        }

        if (string.IsNullOrWhiteSpace(
                NombreDispositivo))
        {
            NombreDispositivo =
                nombrePredeterminado;
        }

        if (Puerto < 1024
            ||
            Puerto > 65000)
        {
            Puerto =
                PuertoPredeterminado;
        }
    }


    // ========================================================
    // GUARDAR / CARGAR
    // ========================================================

    private static readonly JsonSerializerOptions
        Opciones =
            new()
            {
                WriteIndented =
                    true,

                DefaultIgnoreCondition =
                    JsonIgnoreCondition
                        .WhenWritingNull
            };


    public void Guardar(
        string ruta)
    {
        string? carpeta =
            Path.GetDirectoryName(
                ruta);

        if (!string.IsNullOrWhiteSpace(
                carpeta))
        {
            Directory.CreateDirectory(
                carpeta);
        }

        string temporal =
            ruta +
            ".tmp";

        File.WriteAllText(
            temporal,
            JsonSerializer.Serialize(
                this,
                Opciones));

        File.Move(
            temporal,
            ruta,
            true);
    }


    public static SincroConfiguracion Cargar(
        string ruta,
        string nombrePredeterminado)
    {
        SincroConfiguracion? config =
            null;

        try
        {
            if (File.Exists(
                    ruta))
            {
                config =
                    JsonSerializer
                        .Deserialize<SincroConfiguracion>(
                            File.ReadAllText(
                                ruta),
                            Opciones);
            }
        }
        catch
        {
            // Si el archivo estuviera corrupto se empieza de cero.
        }

        config ??=
            new SincroConfiguracion();

        config.Completar(
            nombrePredeterminado);

        return config;
    }


    /// <summary>
    /// "host" o "host:puerto" -> (host, puerto).
    /// </summary>
    public static (string Host, int Puerto) SepararDireccion(
        string direccion,
        int puertoPredeterminado)
    {
        string limpio =
            (direccion ?? "").Trim();

        if (limpio.Length == 0)
        {
            return (
                "",
                puertoPredeterminado);
        }

        int dosPuntos =
            limpio.LastIndexOf(
                ':');

        if (dosPuntos > 0
            &&
            int.TryParse(
                limpio[(dosPuntos + 1)..],
                out int puerto)
            &&
            puerto > 0
            &&
            puerto <= 65000
            &&
            !limpio.Contains(
                "]"))
        {
            return (
                limpio[..dosPuntos].Trim(),
                puerto);
        }

        return (
            limpio,
            puertoPredeterminado);
    }
}
