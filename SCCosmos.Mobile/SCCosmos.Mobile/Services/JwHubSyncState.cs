using System;
using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Services;


// =============================================================
// ESTADOS POSIBLES DE LA SINTONIZACIÓN JW HUB
// =============================================================

public enum EstadoJwHubSync
{
    Inactivo,
    Preparando,
    Sintonizando,
    Cancelando,
    Completado,
    Cancelado,
    Error,
    RequiereInicioSesion
}


// =============================================================
// FOTO INSTANTÁNEA DEL ESTADO ACTUAL
// =============================================================

public record JwHubSyncSnapshot(
    EstadoJwHubSync Estado,
    int Actual,
    int Total,
    string Mensaje,
    bool Automatico)
{
    // =========================================================
    // INDICA SI HAY UNA SINTONIZACIÓN ACTIVA
    // =========================================================

    public bool EnCurso =>
        Estado == EstadoJwHubSync.Preparando
        ||
        Estado == EstadoJwHubSync.Sintonizando
        ||
        Estado == EstadoJwHubSync.Cancelando;


    // =========================================================
    // PROGRESO ENTRE 0 Y 1
    // =========================================================

    public double Progreso =>
        Total > 0
            ? Math.Clamp(
                (double)Actual / Total,
                0,
                1)
            : 0;
}


// =============================================================
// ESTADO GLOBAL DE LA SINTONIZACIÓN
// =============================================================

public static class JwHubSyncState
{
    // =========================================================
    // CLAVES DE PREFERENCES
    // =========================================================

    private const string ClaveUltimoEstado =
        "JwHub_UltimoEstado";

    private const string ClaveUltimoMensaje =
        "JwHub_UltimoMensaje";

    private const string ClaveUltimaSintonizacion =
        "JwHub_UltimaSintonizacion";

    private const string ClaveUltimoActual =
        "JwHub_UltimoActual";

    private const string ClaveUltimoTotal =
        "JwHub_UltimoTotal";

    private const string ClaveUltimoAutomatico =
        "JwHub_UltimoAutomatico";


    // =========================================================
    // BLOQUEO
    // =========================================================

    private static readonly object _bloqueo =
        new();


    // =========================================================
    // ESTADO EN MEMORIA
    // =========================================================

    private static JwHubSyncSnapshot _actual =
        CrearEstadoInicial();


    // =========================================================
    // EVENTO DE CAMBIO
    // =========================================================

    public static event Action? Cambiado;


    // =========================================================
    // ESTADO ACTUAL
    // =========================================================

    public static JwHubSyncSnapshot Actual
    {
        get
        {
            lock (_bloqueo)
            {
                return _actual;
            }
        }
    }


    // =========================================================
    // INDICADOR RÁPIDO
    // =========================================================

    public static bool EstaSintonizando =>
        Actual.EnCurso;


    // =========================================================
    // ÚLTIMA SINTONIZACIÓN CORRECTA
    // =========================================================

    public static string UltimaSintonizacion =>
        Preferences.Default.Get(
            ClaveUltimaSintonizacion,
            "");


    // =========================================================
    // ESTABLECER NUEVO ESTADO
    // =========================================================

    public static void Establecer(
        EstadoJwHubSync estado,
        int actual = 0,
        int total = 0,
        string mensaje = "",
        bool automatico = false)
    {
        // Evitamos valores imposibles.
        if (actual < 0)
        {
            actual = 0;
        }


        if (total < 0)
        {
            total = 0;
        }


        if (total > 0 &&
            actual > total)
        {
            actual = total;
        }


        mensaje ??=
            "";


        JwHubSyncSnapshot nuevoEstado =
            new(
                estado,
                actual,
                total,
                mensaje,
                automatico);


        lock (_bloqueo)
        {
            _actual =
                nuevoEstado;
        }


        // =====================================================
        // GUARDAR INFORMACIÓN BÁSICA EN PREFERENCES
        // =====================================================

        try
        {
            Preferences.Default.Set(
                ClaveUltimoEstado,
                estado.ToString());


            Preferences.Default.Set(
                ClaveUltimoMensaje,
                mensaje);


            Preferences.Default.Set(
                ClaveUltimoActual,
                actual);


            Preferences.Default.Set(
                ClaveUltimoTotal,
                total);


            Preferences.Default.Set(
                ClaveUltimoAutomatico,
                automatico);


            if (estado ==
                EstadoJwHubSync.Completado)
            {
                Preferences.Default.Set(
                    ClaveUltimaSintonizacion,
                    DateTime.Now.ToString(
                        "dd/MM/yyyy HH:mm:ss"));
            }
        }
        catch
        {
            // Un problema escribiendo Preferences
            // no debe detener la sintonización.
        }


        // =====================================================
        // AVISAR A LA INTERFAZ
        //
        // El consumidor se encargará de pasar al hilo
        // principal cuando sea necesario.
        // =====================================================

        Cambiado?.Invoke();
    }


    // =========================================================
    // CREAR ESTADO INICIAL
    //
    // Recuperamos el último resultado informativo,
    // pero NUNCA restauramos una sintonización como "EnCurso".
    //
    // Si Android ha cerrado el proceso, no podemos afirmar
    // que la operación anterior siga ejecutándose.
    // =========================================================

    private static JwHubSyncSnapshot CrearEstadoInicial()
    {
        try
        {
            string estadoTexto =
                Preferences.Default.Get(
                    ClaveUltimoEstado,
                    EstadoJwHubSync
                        .Inactivo
                        .ToString());


            EstadoJwHubSync estado;


            if (!Enum.TryParse(
                    estadoTexto,
                    ignoreCase: true,
                    out estado))
            {
                estado =
                    EstadoJwHubSync.Inactivo;
            }


            // =================================================
            // NO RESTAURAR ESTADOS ACTIVOS
            // =================================================

            if (estado ==
                    EstadoJwHubSync.Preparando
                ||
                estado ==
                    EstadoJwHubSync.Sintonizando
                ||
                estado ==
                    EstadoJwHubSync.Cancelando)
            {
                return new JwHubSyncSnapshot(
                    EstadoJwHubSync.Inactivo,
                    0,
                    0,
                    "",
                    false);
            }


            string mensaje =
                Preferences.Default.Get(
                    ClaveUltimoMensaje,
                    "");


            int actual =
                Preferences.Default.Get(
                    ClaveUltimoActual,
                    0);


            int total =
                Preferences.Default.Get(
                    ClaveUltimoTotal,
                    0);


            bool automatico =
                Preferences.Default.Get(
                    ClaveUltimoAutomatico,
                    false);


            return new JwHubSyncSnapshot(
                estado,
                actual,
                total,
                mensaje,
                automatico);
        }
        catch
        {
            return new JwHubSyncSnapshot(
                EstadoJwHubSync.Inactivo,
                0,
                0,
                "",
                false);
        }
    }
}