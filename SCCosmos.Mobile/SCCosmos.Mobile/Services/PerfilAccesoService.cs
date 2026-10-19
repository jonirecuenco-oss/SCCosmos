using Microsoft.Maui.Storage;

using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;


// ============================================================================
// PERFIL DE USUARIO Y NIVEL DE ACCESO
// ============================================================================
//
// El dispositivo puede usarlo el superintendente (él) o su esposa.
// Solo con perfil "Hombre" Y el código correcto se accede a todas las
// funciones. En caso contrario, solo Inicio, Calendario, Personas y
// Configuración.

public static class PerfilAccesoService
{
    // Los códigos de acceso no se guardan en claro: solo su verificación
    // (PBKDF2-SHA256 con sal aleatoria). Así no aparecen en el código
    // publicado en GitHub.
    private const string SalCompleto =
        "9I02tLJEMceNsOEBrGjH4A==";

    private const string ClaveCompleto =
        "iFarzSpfFX4MVfyEKpLLh6IYB+JS0g8GE+1x4bQDeQs=";

    private const string SalDescargas =
        "faU8Wp8KD8g1TE+DS1B7dw==";

    private const string ClaveDescargas =
        "WlvHNJAcrEgW5iYqyDBXWnIBGCN5FgJU9tbaaurpjYE=";

    /// <summary>
    /// Texto que se muestra en el campo del código cuando ya está
    /// validado (el código real ya no se puede volver a mostrar).
    /// </summary>
    public const string MarcadorCodigo =
        "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    /// <summary>¿El código es el de acceso completo?</summary>
    public static bool CodigoEsCompleto(string codigo) =>
        CodigoAcceso.Verificar(
            codigo,
            SalCompleto,
            ClaveCompleto);

    /// <summary>
    /// ¿El código es el especial, que además activa la página de
    /// Descargas para siempre (en Windows; en el móvil no existe esa
    /// página, pero el código vale igual)?
    /// </summary>
    public static bool CodigoEsDescargas(string codigo) =>
        CodigoAcceso.Verificar(
            codigo,
            SalDescargas,
            ClaveDescargas);

    public const string PefilHombre =
        "Hombre";

    public const string PerfilEsposa =
        "Esposa";

    private const string ClaveRol =
        "SCCosmos_PerfilUsuario";

    private const string ClaveCodigoOk =
        "SCCosmos_CodigoHombreOk";

    private const string ClaveDescargasActivadas =
        "SCCosmos_DescargasActivadas";

    public static string Rol =>
        Preferences.Default.Get(
            ClaveRol,
            "");

    public static bool Configurado =>
        !string.IsNullOrWhiteSpace(
            Rol);

    public static bool EsHombre =>
        string.Equals(
            Rol,
            PefilHombre,
            StringComparison.OrdinalIgnoreCase);

    public static bool CodigoValidado =>
        Preferences.Default.Get(
            ClaveCodigoOk,
            false);

    /// <summary>
    /// La página de Descargas se activa para siempre al usar el
    /// código especial en el perfil.
    /// </summary>
    public static bool DescargasActivadas =>
        Preferences.Default.Get(
            ClaveDescargasActivadas,
            false);

    /// <summary>
    /// Hay acceso a todas las funciones solo si es el hermano y el
    /// código es válido. Si no hay perfil configurado todavía, se
    /// considera acceso completo para no bloquear (se preguntará al
    /// arrancar).
    /// </summary>
    public static bool AccesoCompleto =>
        !Configurado
        || (EsHombre && CodigoValidado);


    public static List<string> PaginasPermitidas =>
        AccesoCompleto
            ? new List<string>
            {
                "inicio",
                "correspondencia",
                "circuito",
                "Calendario",
                "personas",
                "escuelas",
                "asambleas",
                "archivo",
                "configuracion"
            }
            : new List<string>
            {
                "inicio",
                "Calendario",
                "personas",
                "rsc",
                "configuracion"
            };


    public static void EstablecerRol(
        string rol)
    {
        bool esHombre =
            string.Equals(
                rol,
                PefilHombre,
                StringComparison.OrdinalIgnoreCase);

        Preferences.Default.Set(
            ClaveRol,
            esHombre
                ? PefilHombre
                : PerfilEsposa);

        if (!esHombre)
        {
            // Una esposa nunca mantiene el código validado.
            Preferences.Default.Set(
                ClaveCodigoOk,
                false);
        }
    }


    public static bool ValidarCodigo(
        string codigo)
    {
        string limpio =
            (codigo ?? "").Trim();

        bool esDescargas =
            CodigoEsDescargas(limpio);

        bool ok =
            esDescargas
            || CodigoEsCompleto(limpio);

        // El código especial activa la página de Descargas para siempre.
        if (esDescargas)
        {
            Preferences.Default.Set(
                ClaveDescargasActivadas,
                true);
        }

        Preferences.Default.Set(
            ClaveCodigoOk,
            ok);

        return ok;
    }
}
