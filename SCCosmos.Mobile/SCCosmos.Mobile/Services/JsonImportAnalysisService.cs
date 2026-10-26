using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;


// ============================================================================
// COMPARADOR JSON S-21 <-> SQLITE
// ============================================================================
//
// Fuente de cada nombre:
//   Persona.Nombre       -> S-21 / JSON
//   Persona.NombreJWHub  -> JW Hub
//   Persona.NombreComun  -> JW Hub
//
// La importación JSON nunca sobrescribe NombreJWHub ni NombreComun.
// ============================================================================

public static class JsonImportAnalysisService
{
    public static ResultadoImportacionJsonMobile Analizar(
        string congregacionCodigo,
        string congregacionNombre,
        int semanaId,
        string mesReferencia,
        List<PersonaJsonMobile> personasJson,
        List<TerritorioJsonMobile>? territorios)
    {
        ArgumentNullException.ThrowIfNull(personasJson);

        List<Persona> personasDB = DatabaseHelper
            .ObtenerPersonas()
            .Where(p => string.Equals(
                p.CongregacionCodigo,
                congregacionCodigo,
                StringComparison.OrdinalIgnoreCase))
            .Where(PersonaEstaActiva)
            .ToList();

        var procesadosDB = new HashSet<int>();

        var resultado = new ResultadoImportacionJsonMobile
        {
            CongregacionCodigo = congregacionCodigo,
            CongregacionNombre = congregacionNombre,
            SemanaId = semanaId,
            MesReferencia = mesReferencia,
            PersonasJson = personasJson,
            Territorios = territorios
        };

        // Una persona puede venir en varios años de servicio (una tarjeta
        // por año): la ficha se revisa una sola vez; la actividad de cada
        // año se guarda al finalizar (PersonasJson conserva todas).
        var nombresProcesados =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (PersonaJsonMobile json in personasJson)
        {
            string nombreJson =
                (json.Nombre ?? "").Trim();

            if (nombreJson.Length > 0
                && !nombresProcesados.Add(nombreJson))
            {
                continue;
            }

            Persona? match = BuscarMatchSeguro(json, personasDB, procesadosDB);

            if (match is null)
            {
                var nuevo = new JsonImportItemMobile
                {
                    Tipo = TipoAnalisisJsonMobile.Nuevo,
                    PersonaJson = json,
                    NombreVisual = json.Nombre,
                    Descripcion =
                        "Aparece en el S-21 pero no se ha podido vincular " +
                        "de forma segura con una ficha existente. Puede ser " +
                        "una persona sin cuenta de JW Hub o la misma persona " +
                        "con un nombre distinto."
                };

                resultado.Nuevos.Add(nuevo);
                resultado.TodosItems.Add(nuevo);
                continue;
            }

            procesadosDB.Add(match.Id);
            List<string> campos = ObtenerCamposCambiados(match, json);

            if (campos.Count > 0)
            {
                var conflicto = new JsonImportItemMobile
                {
                    Tipo = TipoAnalisisJsonMobile.Conflicto,
                    PersonaDB = match,
                    PersonaJson = json,
                    NombreVisual = ObtenerNombreVisual(match),
                    Descripcion = "Cambios S-21: " + string.Join(", ", campos),
                    CamposCambiados = campos
                };

                resultado.Conflictos.Add(conflicto);
                resultado.TodosItems.Add(conflicto);
            }
            else
            {
                var igual = new JsonImportItemMobile
                {
                    Tipo = TipoAnalisisJsonMobile.Igual,
                    PersonaDB = match,
                    PersonaJson = json,
                    NombreVisual = ObtenerNombreVisual(match),
                    Descripcion =
                        "La ficha S-21 coincide con los datos actuales. " +
                        "La actividad mensual se importará al finalizar."
                };

                resultado.Iguales.Add(igual);
                resultado.TodosItems.Add(igual);
            }
        }

        foreach (Persona persona in personasDB)
        {
            if (procesadosDB.Contains(persona.Id))
                continue;

            var baja = new JsonImportItemMobile
            {
                Tipo = TipoAnalisisJsonMobile.Baja,
                PersonaDB = persona,
                NombreVisual = ObtenerNombreVisual(persona),
                Descripcion =
                    "Esta ficha activa no aparece emparejada en el S-21. " +
                    "Antes de confirmar una baja, comprueba si corresponde " +
                    "a alguno de los registros clasificados como nuevos."
            };

            resultado.Bajas.Add(baja);
            resultado.TodosItems.Add(baja);
        }

        return resultado;
    }

    private static Persona? BuscarMatchSeguro(
        PersonaJsonMobile json,
        List<Persona> personasDB,
        HashSet<int> procesados)
    {
        string claveJson = ClaveNombre(json.Nombre);
        if (string.IsNullOrWhiteSpace(claveJson))
            return null;

        List<Persona> candidatos = personasDB
            .Where(p => !procesados.Contains(p.Id))
            .Where(p => NombresEquivalentes(claveJson, p))
            .Where(p =>
                !FechasIncompatibles(p.FechaNacimiento, json.FechaNacimiento) &&
                !FechasIncompatibles(p.FechaBautismo, json.FechaBautismo))
            .ToList();

        if (candidatos.Count == 1)
            return candidatos[0];

        if (candidatos.Count > 1)
        {
            List<Persona> porFechas = candidatos
                .Where(p =>
                    FechaExactaCoincide(p.FechaNacimiento, json.FechaNacimiento) &&
                    (string.IsNullOrWhiteSpace(json.FechaBautismo) ||
                     FechaExactaCoincide(p.FechaBautismo, json.FechaBautismo)))
                .ToList();

            if (porFechas.Count == 1)
                return porFechas[0];
        }

        return null;
    }

    private static bool NombresEquivalentes(string claveJson, Persona persona)
    {
        string[] nombres = [persona.Nombre, persona.NombreJWHub, persona.NombreComun];

        return nombres
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(ClaveNombre)
            .Any(clave => clave == claveJson);
    }

    public static List<string> ObtenerCamposCambiados(
        Persona db,
        PersonaJsonMobile json)
    {
        var campos = new List<string>();

        if (!string.IsNullOrWhiteSpace(json.Nombre) && !Iguales(db.Nombre, json.Nombre))
            campos.Add("Nombre S-21");

        if (!string.IsNullOrWhiteSpace(json.Sexo) && !Iguales(db.Sexo, json.Sexo))
            campos.Add("Sexo");

        if (!string.IsNullOrWhiteSpace(json.Esperanza) && !Iguales(db.Esperanza, json.Esperanza))
            campos.Add("Esperanza");

        // En nombramiento y servicio el vacío puede ser significativo.
        if (!Iguales(db.Nombramiento, json.Nombramiento))
            campos.Add("Nombramiento");

        if (!Iguales(db.Servicio, json.Servicio))
            campos.Add("Servicio");

        if (!string.IsNullOrWhiteSpace(json.FechaNacimiento) &&
            !FechasEquivalentes(db.FechaNacimiento, json.FechaNacimiento))
            campos.Add("Fecha de nacimiento");

        if (!string.IsNullOrWhiteSpace(json.FechaBautismo) &&
            !FechasEquivalentes(db.FechaBautismo, json.FechaBautismo))
            campos.Add("Fecha de bautismo");

        if (json.Grupo > 0 && db.Grupo != json.Grupo)
            campos.Add("Grupo");

        return campos;
    }

    public static Persona CrearPersonaDesdeJson(
        PersonaJsonMobile json,
        string congregacionCodigo)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new Persona
        {
            Nombre =
                json.Nombre ?? "",

            // Una persona nacida desde S-21 todavía no tiene identidad JW.
            NombreJWHub =
                "",

            // Regla acordada: mientras no exista cuenta JW, el nombre común
            // coincide con el nombre completo S-21.
            NombreComun =
                json.Nombre ?? "",

            CongregacionCodigo =
                congregacionCodigo ?? "",

            Grupo =
                json.Grupo,

            FechaNacimiento =
                json.FechaNacimiento ?? "",

            Sexo =
                json.Sexo ?? "",

            FechaBautismo =
                json.FechaBautismo ?? "",

            Esperanza =
                json.Esperanza ?? "",

            Nombramiento =
                json.Nombramiento ?? "",

            Servicio =
                json.Servicio ?? ""
        };
    }


    public static void AplicarValorJson(
        Persona destino,
        PersonaJsonMobile json,
        string clave)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(json);

        switch (clave)
        {
            case "Nombre":
                destino.Nombre =
                    json.Nombre ?? "";

                // Si no existe identidad JW, NombreComun sigue al S-21.
                if (string.IsNullOrWhiteSpace(
                        destino.NombreJWHub))
                {
                    destino.NombreComun =
                        destino.Nombre;
                }

                break;

            case "Sexo":
                destino.Sexo =
                    json.Sexo ?? "";
                break;

            case "Esperanza":
                destino.Esperanza =
                    json.Esperanza ?? "";
                break;

            case "Nombramiento":
                destino.Nombramiento =
                    json.Nombramiento ?? "";
                break;

            case "Servicio":
                destino.Servicio =
                    json.Servicio ?? "";
                break;

            case "FechaNacimiento":
                destino.FechaNacimiento =
                    json.FechaNacimiento ?? "";
                break;

            case "FechaBautismo":
                destino.FechaBautismo =
                    json.FechaBautismo ?? "";
                break;

            case "Grupo":
                destino.Grupo =
                    json.Grupo;
                break;
        }
    }


    public static void AsegurarReglaIdentidad(
        Persona persona)
    {
        ArgumentNullException.ThrowIfNull(persona);

        if (string.IsNullOrWhiteSpace(
                persona.NombreJWHub))
        {
            persona.NombreComun =
                persona.Nombre ?? "";
        }
    }


    public static List<CandidatoVinculoJsonMobile> ObtenerCandidatosVinculo(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item)
    {
        if (item.PersonaJson is null)
            return new();

        PersonaJsonMobile json = item.PersonaJson;

        return resultado.Bajas
            .Where(b => b.PersonaDB is not null)
            .Select(b => new CandidatoVinculoJsonMobile
            {
                Persona = b.PersonaDB!,
                Puntuacion = CalcularPuntuacion(b.PersonaDB!, json)
            })
            .OrderByDescending(c => c.Puntuacion)
            .ThenBy(c => c.NombreVisual)
            .ToList();
    }

    public static int CalcularPuntuacion(Persona db, PersonaJsonMobile json)
    {
        int simNombre = new[] { db.Nombre, db.NombreJWHub, db.NombreComun }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => SimilitudNombre(n, json.Nombre))
            .DefaultIfEmpty(0)
            .Max();

        int puntos = (int)Math.Round(simNombre * 0.45);

        if (FechaExactaCoincide(db.FechaNacimiento, json.FechaNacimiento))
            puntos += 35;
        else if (!FechasIncompatibles(db.FechaNacimiento, json.FechaNacimiento))
            puntos += 8;

        if (FechaExactaCoincide(db.FechaBautismo, json.FechaBautismo))
            puntos += 15;
        else if (!FechasIncompatibles(db.FechaBautismo, json.FechaBautismo))
            puntos += 4;

        if (!string.IsNullOrWhiteSpace(json.Sexo) && Iguales(db.Sexo, json.Sexo))
            puntos += 5;

        return Math.Clamp(puntos, 0, 100);
    }

    public static void MarcarResuelto(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item,
        Persona personaActualizada,
        string descripcion)
    {
        QuitarDeCategorias(resultado, item);

        item.Tipo =
            TipoAnalisisJsonMobile.Igual;

        item.PersonaDB =
            personaActualizada;

        item.NombreVisual =
            ObtenerNombreVisual(
                personaActualizada);

        item.Descripcion =
            descripcion;

        item.CamposCambiados.Clear();
        item.IgnorarImportacion = false;
        item.ConfirmarBaja = false;

        if (!resultado.Iguales.Contains(
                item))
        {
            resultado.Iguales.Add(
                item);
        }
    }


    public static void VincularNuevo(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile itemNuevo,
        Persona personaExistente,
        Persona personaActualizada)
    {
        JsonImportItemMobile? bajaCorrespondiente =
            resultado.Bajas
                .FirstOrDefault(b =>
                    b.PersonaDB?.Id ==
                    personaExistente.Id);

        if (bajaCorrespondiente is not null)
        {
            resultado.Bajas.Remove(
                bajaCorrespondiente);

            bajaCorrespondiente.IgnorarImportacion =
                true;

            bajaCorrespondiente.ConfirmarBaja =
                false;
        }

        itemNuevo.CrearPersonaNueva =
            false;

        MarcarResuelto(
            resultado,
            itemNuevo,
            personaActualizada,
            "Vinculado con una ficha existente. Se conservará su " +
            "identidad JW y se asociará el nombre S-21 al finalizar.");
    }


    public static void PrepararPersonaNueva(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile itemNuevo,
        Persona personaNueva)
    {
        itemNuevo.CrearPersonaNueva =
            true;

        MarcarResuelto(
            resultado,
            itemNuevo,
            personaNueva,
            "Revisado como persona nueva. Se añadirá a la base de datos " +
            "al finalizar la importación.");
    }


    public static void ConfirmarBaja(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item)
    {
        resultado.Bajas.Remove(
            item);

        item.ConfirmarBaja =
            true;

        item.IgnorarImportacion =
            false;
    }


    public static void OmitirNuevo(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item)
    {
        resultado.Nuevos.Remove(
            item);

        item.IgnorarImportacion =
            true;

        item.CrearPersonaNueva =
            false;
    }


    private static void QuitarDeCategorias(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item)
    {
        resultado.Conflictos.Remove(item);
        resultado.Nuevos.Remove(item);
        resultado.Bajas.Remove(item);
        resultado.Iguales.Remove(item);
    }

    public static string ObtenerNombreVisual(Persona persona)
    {
        if (!string.IsNullOrWhiteSpace(persona.NombreComun)) return persona.NombreComun;
        if (!string.IsNullOrWhiteSpace(persona.Nombre)) return persona.Nombre;
        if (!string.IsNullOrWhiteSpace(persona.NombreJWHub)) return persona.NombreJWHub;
        return "(Sin nombre)";
    }

    public static string ClaveNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return "";

        string descompuesto = nombre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (char c in descompuesto)
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark)
                continue;

            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        string[] tokens = Regex.Split(sb.ToString(), @"\s+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        return string.Join(" ", tokens);
    }

    private static int SimilitudNombre(string a, string b)
    {
        string x = ClaveNombre(a);
        string y = ClaveNombre(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 100;

        int distancia = DistanciaLevenshtein(x, y);
        int max = Math.Max(x.Length, y.Length);
        double porcentaje = 100.0 * (1.0 - (double)distancia / max);
        return (int)Math.Round(Math.Clamp(porcentaje, 0, 100));
    }

    private static int DistanciaLevenshtein(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int coste = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + coste);
            }
        }

        return d[a.Length, b.Length];
    }

    private static bool Iguales(string? a, string? b) =>
        string.Equals(
            (a ?? "").Trim(),
            (b ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool FechasEquivalentes(string? a, string? b)
    {
        if (Iguales(a, b)) return true;
        DateTime? da = ParseFecha(a);
        DateTime? db = ParseFecha(b);
        return da.HasValue && db.HasValue && da.Value.Date == db.Value.Date;
    }

    private static bool FechaExactaCoincide(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return FechasEquivalentes(a, b);
    }

    private static bool FechasIncompatibles(string? dbDate, string? jsonDate)
    {
        if (string.IsNullOrWhiteSpace(dbDate) || string.IsNullOrWhiteSpace(jsonDate))
            return false;

        DateTime? db = ParseFecha(dbDate);
        DateTime? json = ParseFecha(jsonDate);
        bool dbAprox = dbDate.Trim().StartsWith("01/01/", StringComparison.Ordinal);
        bool jsonAprox = jsonDate.Trim().StartsWith("01/01/", StringComparison.Ordinal);

        if (db.HasValue && json.HasValue && !dbAprox && !jsonAprox)
            return db.Value.Date != json.Value.Date;

        int anioDb = ObtenerAnio(dbDate);
        int anioJson = ObtenerAnio(jsonDate);
        return anioDb > 0 && anioJson > 0 && Math.Abs(anioDb - anioJson) > 2;
    }

    private static int ObtenerAnio(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha)) return 0;
        Match match = Regex.Match(fecha, @"\b(\d{4})\b", RegexOptions.RightToLeft);
        return match.Success && int.TryParse(match.Groups[1].Value, out int anio) ? anio : 0;
    }

    private static DateTime? ParseFecha(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha)) return null;

        string[] formatos = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"];
        foreach (string formato in formatos)
        {
            if (DateTime.TryParseExact(
                    fecha.Trim(),
                    formato,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime exacta))
                return exacta;
        }

        if (DateTime.TryParse(
                fecha,
                CultureInfo.GetCultureInfo("es-ES"),
                DateTimeStyles.None,
                out DateTime espanola))
            return espanola;

        return null;
    }

    private static bool PersonaEstaActiva(Persona persona)
    {
        if (string.IsNullOrWhiteSpace(persona.FechaEliminacion))
            return true;

        DateTime? eliminacion = ParseFecha(persona.FechaEliminacion);
        return !eliminacion.HasValue || eliminacion.Value.Date > DateTime.Today;
    }
}
