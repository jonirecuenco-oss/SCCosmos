using System.Globalization;
using System.Text.Json.Nodes;

namespace SCCosmos.Sincro;

// ============================================================
// MENSAJES ENTRE DISPOSITIVOS (JSON)
//
// Tipos:
//   hola / hola_ok : presentación (id, nombre)
//   lote           : bloque de cambios (con ack implícito)
//   recibido       : confirmación de hasta qué Seq se aplicó
//   ping / pong    : mantener viva la conexión
// ============================================================

public static class SincroMensajes
{
    // ========================================================
    // CREAR
    // ========================================================

    public static string Hola(
        SincroConfiguracion config)
    {
        return new JsonObject
        {
            ["t"] =
                "hola",

            ["v"] =
                1,

            ["id"] =
                config.IdDispositivo,

            ["nom"] =
                config.NombreDispositivo,

            ["puerto"] =
                config.Puerto
        }
        .ToJsonString();
    }


    public static string HolaOk(
        SincroConfiguracion config)
    {
        return new JsonObject
        {
            ["t"] =
                "hola_ok",

            ["v"] =
                1,

            ["id"] =
                config.IdDispositivo,

            ["nom"] =
                config.NombreDispositivo,

            ["puerto"] =
                config.Puerto
        }
        .ToJsonString();
    }


    public static string Lote(
        IReadOnlyList<CambioSincro> cambios)
    {
        var lista =
            new JsonArray();

        foreach (CambioSincro cambio in
                 cambios)
        {
            lista.Add(
                CambioANodo(
                    cambio));
        }

        return new JsonObject
        {
            ["t"] =
                "lote",

            ["cambios"] =
                lista
        }
        .ToJsonString();
    }


    public static string Recibido(
        long hasta)
    {
        return new JsonObject
        {
            ["t"] =
                "recibido",

            ["hasta"] =
                hasta
        }
        .ToJsonString();
    }


    public static string Ping()
    {
        return new JsonObject
        {
            ["t"] =
                "ping"
        }
        .ToJsonString();
    }


    /// <summary>
    /// Confirmación dirigida a un dispositivo concreto (canal por
    /// carpeta: los mensajes viajan en archivos compartidos).
    /// </summary>
    public static string RecibidoPara(
        string para,
        long hasta)
    {
        return new JsonObject
        {
            ["t"] =
                "recibido",

            ["para"] =
                para,

            ["hasta"] =
                hasta
        }
        .ToJsonString();
    }


    public static string Pong()
    {
        return new JsonObject
        {
            ["t"] =
                "pong"
        }
        .ToJsonString();
    }


    // ========================================================
    // DESCUBRIMIENTO (UDP)
    // ========================================================

    public static string Descubrimiento(
        SincroConfiguracion config)
    {
        return new JsonObject
        {
            ["t"] =
                "descubrir",

            ["id"] =
                config.IdDispositivo,

            ["nom"] =
                config.NombreDispositivo,

            ["puerto"] =
                config.Puerto
        }
        .ToJsonString();
    }


    public static string AquiEstoy(
        SincroConfiguracion config)
    {
        return new JsonObject
        {
            ["t"] =
                "aqui",

            ["id"] =
                config.IdDispositivo,

            ["nom"] =
                config.NombreDispositivo,

            ["puerto"] =
                config.Puerto
        }
        .ToJsonString();
    }


    // ========================================================
    // CONVERTIR CAMBIO <-> JSON
    // ========================================================

    public static JsonObject CambioANodo(
        CambioSincro cambio)
    {
        var nodo =
            new JsonObject
            {
                ["tb"] =
                    cambio.Tabla,

                ["cl"] =
                    cambio.Clave,

                ["ti"] =
                    cambio.Tipo,

                ["mo"] =
                    cambio.Momento,

                ["or"] =
                    cambio.Origen,

                ["sq"] =
                    cambio.Seq
            };

        if (cambio.Fila is not null
            &&
            cambio.Tipo != "D")
        {
            var fila =
                new JsonObject();

            foreach (KeyValuePair<string, object?> campo in
                     cambio.Fila)
            {
                fila[campo.Key] =
                    ValorANodo(
                        campo.Value);
            }

            nodo["fi"] =
                fila;
        }

        return nodo;
    }


    public static List<CambioSincro> LeerLote(
        JsonObject mensaje)
    {
        var resultado =
            new List<CambioSincro>();

        if (mensaje["cambios"]
            is not JsonArray lista)
        {
            return resultado;
        }

        foreach (JsonNode? nodo in
                 lista)
        {
            if (nodo is not JsonObject cambio)
            {
                continue;
            }

            resultado.Add(
                CambioDesdeNodo(
                    cambio));
        }

        return resultado;
    }


    public static CambioSincro CambioDesdeNodo(
        JsonObject nodo)
    {
        var cambio =
            new CambioSincro
            {
                Tabla =
                    nodo["tb"]?.GetValue<string>()
                    ?? "",

                Clave =
                    nodo["cl"]?.GetValue<string>()
                    ?? "",

                Tipo =
                    nodo["ti"]?.GetValue<string>()
                    ?? "U",

                Momento =
                    nodo["mo"]?.GetValue<string>()
                    ?? "",

                Origen =
                    nodo["or"]?.GetValue<string>()
                    ?? "",

                Seq =
                    nodo["sq"]?.GetValue<long>()
                    ?? 0
            };

        if (nodo["fi"]
            is JsonObject fila)
        {
            var datos =
                new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, JsonNode?> campo in
                     fila)
            {
                datos[campo.Key] =
                    NodoAValor(
                        campo.Value);
            }

            cambio.Fila =
                datos;
        }

        return cambio;
    }


    // ========================================================
    // VALORES
    // ========================================================

    private static JsonNode? ValorANodo(
        object? valor)
    {
        switch (valor)
        {
            case null:
            case DBNull:
                return null;

            case string texto:
                return JsonValue.Create(
                    texto);

            case bool booleano:
                return JsonValue.Create(
                    booleano);

            case byte entero8:
                return JsonValue.Create(
                    (long)entero8);

            case short entero16:
                return JsonValue.Create(
                    (long)entero16);

            case int entero32:
                return JsonValue.Create(
                    (long)entero32);

            case long entero64:
                return JsonValue.Create(
                    entero64);

            case float real32:
                return JsonValue.Create(
                    (double)real32);

            case double real64:
                return JsonValue.Create(
                    real64);

            case decimal decimales:
                return JsonValue.Create(
                    (double)decimales);

            case byte[] bytes:
                return JsonValue.Create(
                    Convert.ToBase64String(
                        bytes));

            default:
                return JsonValue.Create(
                    Convert.ToString(
                        valor,
                        CultureInfo.InvariantCulture)
                    ?? "");
        }
    }


    private static object? NodoAValor(
        JsonNode? nodo)
    {
        if (nodo is null)
        {
            return null;
        }

        if (nodo is not JsonValue valor)
        {
            return nodo.ToJsonString();
        }

        switch (valor.GetValueKind())
        {
            case System.Text.Json
                .JsonValueKind.String:

                return valor.GetValue<string>();

            case System.Text.Json
                .JsonValueKind.Number:

                if (valor.TryGetValue(
                        out long entero))
                {
                    return entero;
                }

                if (valor.TryGetValue(
                        out double real))
                {
                    return real;
                }

                return valor.ToJsonString();

            case System.Text.Json
                .JsonValueKind.True:
            case System.Text.Json
                .JsonValueKind.False:

                return valor.GetValue<bool>();

            case System.Text.Json
                .JsonValueKind.Null:

                return null;

            default:

                return valor.ToJsonString();
        }
    }
}
