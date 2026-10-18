using Microsoft.Maui.Storage;

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
    public const string CodigoCompleto =
        "CODIGO_OCULTO";

    /// <summary>
    /// Código especial: da acceso completo y además activa para
    /// siempre la página de Descargas (en Windows; en el móvil no
    /// existe esa página, pero el código vale igual).
    /// </summary>
    public const string CodigoAlternativo =
        "CODIGO_OCULTO_DESCARGAS";

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

        bool ok =
            string.Equals(
                limpio,
                CodigoCompleto,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                limpio,
                CodigoAlternativo,
                StringComparison.OrdinalIgnoreCase);

        // El código especial activa la página de Descargas para siempre.
        if (string.Equals(
                limpio,
                CodigoAlternativo,
                StringComparison.OrdinalIgnoreCase))
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
