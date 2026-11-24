#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SCCosmos.Sincro;

/// <summary>
/// Prueba del coche por el OBDLink (un ELM327): qué órdenes de combustible y
/// kilómetros responde. Solo se leen datos (modos 01 y 22) y solo se pregunta
/// al motor (7DF, 7E0): no se borra ni se cambia nada del coche. La conexión
/// Bluetooth va en la app del móvil; aquí, qué se pregunta y cómo se lee.
/// </summary>
public static class PruebaCoche
{
    /// <summary>
    /// Una orden de la prueba. Motor: se le pregunta solo al motor (7E0);
    /// si no, a todos a la vez (7DF), como las estándar.
    /// </summary>
    public sealed record Orden(string Grupo, string Nombre, string Peticion, bool Motor, Func<byte[], string> Valor);

    /// <summary>Lo que dice el coche a una orden: si la tiene y su valor (o por qué no).</summary>
    public sealed record Resultado(bool Responde, string Texto);

    /// <summary>
    /// Para preparar el adaptador: se reinicia, sin eco, con cabeceras (para
    /// saber quién responde) y buscando él solo el protocolo del coche.
    /// </summary>
    public static readonly string[] Preparacion = { "ATWS", "ATE0", "ATL0", "ATS1", "ATH1", "ATSP0" };

    /// <summary>Qué datos estándar dice tener el coche, de 32 en 32.</summary>
    public static readonly string[] OrdenesSoportados = { "0100", "0120", "0140", "0160", "0180", "01A0", "01C0" };

    // Las de Opel son de la base de datos abierta OBDb (VauxhallOpel), sin
    // comprobar en el Astra K; las 22 F4xx son las estándar pedidas al motor.
    public static readonly IReadOnlyList<Orden> Ordenes = new List<Orden>
    {
        new("Combustible", "Estándar (01 2F)", "012F", false, d => Porcentaje(d[0])),
        new("Combustible", "Estándar al motor (22 F42F)", "22F42F", true, d => Porcentaje(d[0])),
        new("Combustible", "Opel (22 0227)", "220227", true, d => Porcentaje(d[0])),
        new("Combustible", "Opel, media (22 A043)", "22A043", true, d => d[0] + " %"),
        new("Combustible", "Opel, en litros (22 040F)", "22040F", true, d => Litros((short)Entero(d, 2) * 711 / 1250.0)),
        new("Kilómetros", "Estándar (01 A6)", "01A6", false, d => Km(Entero(d, 4) / 10.0, 1)),
        new("Kilómetros", "Estándar al motor (22 F4A6)", "22F4A6", true, d => Km(Entero(d, 4) / 10.0, 1)),
        new("Kilómetros", "Opel (22 03BC)", "2203BC", true, d => Km(Entero(d, 4) / 1000.0, 1)),
        new("Kilómetros", "Opel, en millas (22 043B)", "22043B", true, d => Km(Entero(d, 2) * 8.192 * 1.609344, 0)),
        new("Otros", "Velocidad (01 0D)", "010D", false, d => d[0] + " km/h"),
        new("Otros", "Km desde que se borraron las averías (01 31)", "0131", false, d => Km(Entero(d, 2), 0)),
        new("Otros", "Tipo de combustible (01 51)", "0151", false, d => TipoCombustible(d[0])),
    };

    /// <summary>Lo que ha respondido el coche a una orden, ya interpretado.</summary>
    public static Resultado Leer(Orden orden, string? recibido)
    {
        string texto = Limpiar(recibido);

        if (texto.Length == 0)
        {
            return new(false, "sin respuesta");
        }

        if (texto.Contains("NO DATA"))
        {
            return new(false, "nadie responde");
        }

        if (texto == "?")
        {
            return new(false, "el adaptador no entiende la orden");
        }

        if (texto.Contains("ERROR") || texto.Contains("UNABLE TO CONNECT") || texto.Contains("STOPPED"))
        {
            return new(false, $"error de comunicación ({texto.Split('\n')[0]})");
        }

        byte[] peticion = Bytes(orden.Peticion);
        Resultado? rechazo = null;

        foreach (byte[] datos in Tramas(texto))
        {
            // Sí: el modo más 0x40, lo que se pidió y el dato.
            if (datos.Length >= peticion.Length
                && datos[0] == peticion[0] + 0x40
                && datos.Skip(1).Take(peticion.Length - 1).SequenceEqual(peticion.Skip(1)))
            {
                try
                {
                    return new(true, orden.Valor(datos[peticion.Length..]));
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
                {
                    return new(false, "respuesta corta");
                }
            }

            // No: 7F, el modo y por qué.
            if (datos.Length >= 3 && datos[0] == 0x7F && datos[1] == peticion[0])
            {
                rechazo ??= new(false, Rechazo(datos[2]));
            }
        }

        return rechazo ?? new(false, "respuesta que no se entiende");
    }

    /// <summary>Los datos de la respuesta buena a una petición (sin el modo ni lo pedido); null si no la hay.</summary>
    public static byte[]? DatosDe(string peticion, string? recibido)
    {
        byte[] orden = Bytes(peticion);

        foreach (byte[] datos in Tramas(Limpiar(recibido)))
        {
            if (datos.Length >= orden.Length
                && datos[0] == orden[0] + 0x40
                && datos.Skip(1).Take(orden.Length - 1).SequenceEqual(orden.Skip(1)))
            {
                return datos[orden.Length..];
            }
        }

        return null;
    }

    /// <summary>Una línea del informe: ✓ o ✗, la orden, lo que da y lo que respondió tal cual.</summary>
    public static string Linea(Orden orden, string? recibido)
    {
        Resultado resultado = Leer(orden, recibido);
        string crudo = Limpiar(recibido).Replace("\n", " | ");

        return $"{(resultado.Responde ? "✓" : "✗")} {orden.Nombre}: {resultado.Texto}"
            + (crudo.Length > 0 ? $" [{crudo}]" : "");
    }

    /// <summary>Los datos estándar (PID) que dice tener el coche en la respuesta a 0100, 0120…</summary>
    public static IEnumerable<int> PidsSoportados(string peticion, string? recibido)
    {
        byte desde = Bytes(peticion)[1];

        foreach (byte[] d in Tramas(Limpiar(recibido)))
        {
            if (d.Length < 6 || d[0] != 0x41 || d[1] != desde)
            {
                continue;
            }

            for (int i = 0; i < 32; i++)
            {
                int pid = desde + i + 1;

                // El último de cada tanda solo dice si hay más tandas.
                if (pid % 0x20 != 0 && (d[2 + i / 8] >> (7 - i % 8) & 1) == 1)
                {
                    yield return pid;
                }
            }
        }
    }

    public static string TextoPids(IEnumerable<int> pids) =>
        string.Join(" ", pids.Distinct().OrderBy(p => p).Select(p => p.ToString("X2")));

    /// <summary>El protocolo que ha encontrado el adaptador (respuesta a ATDPN: «A6», «6»…).</summary>
    public static string Protocolo(string? recibido)
    {
        string dpn = Limpiar(recibido);

        string nombre = dpn.TrimStart('A') switch
        {
            "6" => "CAN 11 bits a 500 kbit/s",
            "7" => "CAN 29 bits a 500 kbit/s",
            "8" => "CAN 11 bits a 250 kbit/s",
            "9" => "CAN 29 bits a 250 kbit/s",
            "3" => "ISO 9141-2",
            "4" or "5" => "KWP2000",
            "1" or "2" => "J1850",
            _ => ""
        };

        return nombre.Length > 0 ? $"{dpn} ({nombre})" : dpn;
    }

    /// <summary>La respuesta sin el «>», sin «SEARCHING...» ni líneas vacías: una línea por trama.</summary>
    public static string Limpiar(string? recibido) =>
        string.Join("\n", (recibido ?? "").Replace(">", "").Split('\r', '\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("SEARCHING", StringComparison.OrdinalIgnoreCase)));

    // Los datos de cada trama. Con cabeceras (ATH1) llegan como «7E8 03 41 2F 9E»
    // (11 bits) o «18 DA F1 10 03 41 2F 9E» (29 bits), con el largo delante; sin
    // ellas, solo los datos. Solo tramas sueltas (hasta 7 bytes): lo que se
    // pregunta aquí cabe en una.
    private static IEnumerable<byte[]> Tramas(string texto)
    {
        foreach (string linea in texto.Split('\n'))
        {
            string[] p = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (p.Length == 0 || !p.All(EsHex))
            {
                continue;
            }

            int cabecera = p[0].Length == 3 ? 1 : p.Length > 5 && p[0] == "18" && p[1] == "DA" ? 4 : 0;

            if (cabecera == 0)
            {
                if (p.All(b => b.Length == 2))
                {
                    yield return p.Select(b => Convert.ToByte(b, 16)).ToArray();
                }

                continue;
            }

            string[] resto = p[cabecera..];

            if (resto.Length < 2 || !resto.All(b => b.Length == 2))
            {
                continue;
            }

            int largo = Convert.ToInt32(resto[0], 16);

            if (largo >= 1 && largo <= 7 && resto.Length > largo)
            {
                yield return resto[1..(largo + 1)].Select(b => Convert.ToByte(b, 16)).ToArray();
            }
        }
    }

    private static bool EsHex(string s) => s.Length is 2 or 3 && s.All(Uri.IsHexDigit);

    private static byte[] Bytes(string hex) =>
        Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

    private static long Entero(byte[] d, int bytes)
    {
        if (d.Length < bytes)
        {
            throw new ArgumentException("Faltan bytes.", nameof(d));
        }

        long n = 0;

        for (int i = 0; i < bytes; i++)
        {
            n = n << 8 | d[i];
        }

        return n;
    }

    private static readonly NumberFormatInfo Numeros = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private static string Porcentaje(byte valor) => (valor * 100.0 / 255).ToString("N0", Numeros) + " %";

    private static string Km(double km, int decimales) => km.ToString("N" + decimales, Numeros) + " km";

    private static string Litros(double litros) => litros.ToString("N1", Numeros) + " l";

    private static string Rechazo(byte codigo) => codigo switch
    {
        0x11 => "no admite esa clase de petición",
        0x12 or 0x31 => "no lo tiene",
        0x22 => "ahora no (¿con el motor en marcha?)",
        0x33 => "protegido",
        _ => $"no (código {codigo:X2})"
    };

    private static string TipoCombustible(byte tipo) => tipo switch
    {
        1 => "gasolina",
        4 => "diésel",
        5 => "GLP",
        6 => "gas natural",
        8 => "eléctrico",
        9 => "bifuel, con gasolina",
        12 => "bifuel, con GLP",
        _ => $"código {tipo}"
    };
}
