#nullable enable

using System;

namespace SCCosmos.Sincro;

/// <summary>
/// De quién es una tarea y quién la ve, igual en el móvil y en Windows.
///
/// Una tarea es de quien figura en CreadaPor («SC» o «Esposa»): la ve su
/// dueño, y los dos si es compartida (las antiguas sin dueño, todos). «Solo
/// para» el cónyuge la pone a su nombre sin compartir, así que también la
/// ven y la avisan las versiones antiguas de su app. Las tareas del otro
/// solo se ven si él lo permite en Configuración (una clave por persona,
/// que se sincroniza).
/// </summary>
public static class TareasConyuge
{
    public const string SC = "SC";

    public const string Esposa = "Esposa";

    /// <summary>Para quién es una tarea, en el orden de <see cref="TextosPara"/>.</summary>
    public enum Para
    {
        Mi,
        LosDos,
        Conyuge
    }


    public static string Conyuge(string usuario) =>
        EsSC(usuario) ? Esposa : SC;


    /// <summary>«Para mí» · «Para los dos» · «Solo para Rebeca».</summary>
    public static string[] TextosPara(string nombreConyuge) =>
        new[] { "Para mí", "Para los dos", "Solo para " + nombreConyuge };


    /// <summary>Si la ve «usuario» en su lista (y le avisa): compartida, suya o sin dueño.</summary>
    public static bool Visible(bool compartida, string? creadaPor, string usuario) =>
        compartida
        || string.IsNullOrWhiteSpace(creadaPor)
        || string.Equals(creadaPor.Trim(), usuario, StringComparison.OrdinalIgnoreCase);


    /// <summary>Para quién es una tarea, vista por «usuario».</summary>
    public static Para ParaDe(bool compartida, string? creadaPor, string usuario) =>
        compartida ? Para.LosDos
        : Visible(false, creadaPor, usuario) ? Para.Mi
        : Para.Conyuge;


    /// <summary>
    /// CreadaPor y Compartida al guardar. «Para los dos» conserva el dueño que
    /// tuviera; «Para mí» y «Solo para» la ponen a nombre de uno.
    /// </summary>
    public static (string CreadaPor, bool Compartida) Aplicar(Para para, string usuario, string? creadaPor) =>
        para switch
        {
            Para.LosDos => (string.IsNullOrWhiteSpace(creadaPor) ? usuario : creadaPor.Trim(), true),
            Para.Conyuge => (Conyuge(usuario), false),
            _ => (usuario, false)
        };


    /// <summary>La clave de Configuración en la que «usuario» deja (o no) que su cónyuge vea sus tareas.</summary>
    public static string ClavePermiso(string usuario) =>
        "TareasVisiblesConyuge_" + (EsSC(usuario) ? SC : Esposa);


    /// <summary>Sin marcar (o sin guardar), no se ven.</summary>
    public static bool Permite(string? valor) =>
        string.Equals(valor?.Trim(), "1", StringComparison.Ordinal);


    public static string ValorPermiso(bool permite) =>
        permite ? "1" : "0";


    private static bool EsSC(string? usuario) =>
        string.Equals(usuario?.Trim(), SC, StringComparison.OrdinalIgnoreCase);
}
