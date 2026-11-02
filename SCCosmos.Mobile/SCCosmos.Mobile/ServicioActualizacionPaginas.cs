using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile;

// =============================================================
// ACTUALIZACIÓN AUTOMÁTICA DE LAS PANTALLAS (MÓVIL)
//
// Cuando el motor aplica cambios llegados de otro dispositivo,
// la página abierta se refresca sola. Las páginas que saben
// recargar sus datos implementan IActualizableMobile; el resto
// se actualiza al volver a ellas.
// =============================================================

public interface IActualizableMobile
{
    void DatosActualizados();
}


public static class ActualizacionPaginasMobile
{
    private static bool _iniciado;

    private static bool _pendiente;

    private static System.Threading.Timer?
        _temporizador;

    private static System.Threading.Timer?
        _temporizadorAlertas;


    public static void Iniciar(
        Shell shell)
    {
        if (_iniciado)
        {
            return;
        }

        _iniciado =
            true;

        try
        {
            SincronizacionMobile.DatosRecibidos +=
                n =>
                {
                    Programar();

                    // El estado de alertas leídas puede haber cambiado en
                    // otro dispositivo: se refresca la notificación. Agrupado,
                    // como las páginas: en una puesta al día llegan muchos
                    // lotes seguidos y recalcularlas con cada uno no aporta.
                    ProgramarAlertas();
                };
        }
        catch
        {
        }

        try
        {
            shell.Navigated +=
                (s, e) =>
                {
                    // Si llegó algo mientras estabas en otra
                    // pantalla, se aplica al volver a una que
                    // sepa refrescarse.
                    if (_pendiente)
                    {
                        Disparar();
                    }
                };
        }
        catch
        {
        }
    }


    private static void Programar()
    {
        try
        {
            _pendiente =
                true;

            _temporizador ??=
                new System.Threading.Timer(
                    _ => Disparar(),
                    null,
                    Timeout.Infinite,
                    Timeout.Infinite);

            _temporizador.Change(
                2000,
                Timeout.Infinite);
        }
        catch
        {
        }
    }


    private static void ProgramarAlertas()
    {
        try
        {
            _temporizadorAlertas ??=
                new System.Threading.Timer(
                    estado => _ = AlertasNotificacionService.ActualizarAsync(),
                    null,
                    Timeout.Infinite,
                    Timeout.Infinite);

            _temporizadorAlertas.Change(
                2000,
                Timeout.Infinite);
        }
        catch
        {
        }
    }


    private static void Disparar()
    {
        try
        {
            _temporizador?.Change(
                Timeout.Infinite,
                Timeout.Infinite);

            if (!_pendiente)
            {
                return;
            }

            _pendiente =
                false;

            MainThread.BeginInvokeOnMainThread(
                () =>
                {
                    try
                    {
                        Page? pagina =
                            Shell.Current?
                                .CurrentPage;

                        if (pagina is IActualizableMobile
                            actualizable)
                        {
                            actualizable
                                .DatosActualizados();
                        }
                    }
                    catch
                    {
                    }
                });
        }
        catch
        {
        }
    }
}
